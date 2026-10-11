using System.Text.Json;

namespace NetWiv.Plugin.Store;

/// <summary>Persistent JSON stores addressed by the official @tauri-apps/plugin-store guest.</summary>
public sealed class StorePlugin : IPlugin
{
    private readonly Dictionary<string, StoreData> _stores = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly object _gate = new();
    private static readonly string[] Commands =
        ["load", "get_store", "set", "get", "has", "delete", "clear", "reset", "keys", "values", "entries", "length", "reload", "save"];

    public string Name => "store";

    public void Initialize(PluginContext context)
    {
        foreach (var command in Commands)
        {
            var name = command;
            context.RegisterCommand(name, (invoke, args, token) => Execute(name, invoke, args, token));
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.PermissionSet("default", Commands.Select(name => "allow-" + name.Replace('_', '-')).ToArray());
    }

    private async ValueTask<JsonElement> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command is "load" or "get_store")
        {
            var path = await ResolvePath(context.App, Text(args, "path"), token);
            lock (_gate)
            {
                if (!_stores.TryGetValue(path, out var store))
                {
                    if (command == "get_store") return JsonValue.Null;
                    store = StoreData.Load(path, Option(args, "options"));
                    _stores.Add(path, store);
                }
                var loadedHandle = new StoreHandle(store, context.WebviewLabel, context.DocumentId);
                loadedHandle.Id = context.AddResource(loadedHandle);
                return JsonValue.From(loadedHandle.Id);
            }
        }

        var handle = context.App.Resources.Get<StoreHandle>(checked((uint)Number(args, "rid")));
        if (!context.IsTrusted && (handle.WebviewLabel != context.WebviewLabel || handle.DocumentId != context.DocumentId))
            throw new InvokeException("Store handle belongs to another document.");
        var data = handle.Data;
        lock (data.Gate)
        {
            switch (command)
            {
                case "set":
                {
                    var key = Text(args, "key");
                    var value = Required(args, "value").Clone();
                    data.Values[key] = value;
                    data.SaveIfAutomatic();
                    Change(context.App, handle, key, value, true);
                    return JsonValue.Null;
                }
                case "get":
                {
                    var exists = data.Values.TryGetValue(Text(args, "key"), out var value);
                    return Write(writer =>
                    {
                        writer.WriteStartArray();
                        if (exists) value.WriteTo(writer); else writer.WriteNullValue();
                        writer.WriteBooleanValue(exists); writer.WriteEndArray();
                    });
                }
                case "has": return JsonValue.From(data.Values.ContainsKey(Text(args, "key")));
                case "delete":
                {
                    var key = Text(args, "key");
                    var removed = data.Values.Remove(key);
                    if (removed) { data.SaveIfAutomatic(); Change(context.App, handle, key, JsonValue.Null, false); }
                    return JsonValue.From(removed);
                }
                case "clear":
                case "reset":
                {
                    var previous = data.Values.Keys.ToArray();
                    data.Values.Clear();
                    if (command == "reset") foreach (var entry in data.Defaults) data.Values[entry.Key] = entry.Value.Clone();
                    data.SaveIfAutomatic();
                    foreach (var key in previous.Union(data.Values.Keys))
                        Change(context.App, handle, key, data.Values.GetValueOrDefault(key), data.Values.ContainsKey(key));
                    return JsonValue.Null;
                }
                case "keys": return Array(data.Values.Keys.Select(JsonValue.From));
                case "values": return Array(data.Values.Values);
                case "entries": return Array(data.Values.Select(entry => Write(writer =>
                {
                    writer.WriteStartArray(); writer.WriteStringValue(entry.Key); entry.Value.WriteTo(writer); writer.WriteEndArray();
                })));
                case "length": return JsonValue.From(data.Values.Count);
                case "reload":
                    data.Reload(Option(args, "ignoreDefaults").ValueKind == JsonValueKind.True);
                    return JsonValue.Null;
                case "save": data.Save(); return JsonValue.Null;
                default: throw new InvokeException("Unknown store command: " + command);
            }
        }
    }

    private static async Task<string> ResolvePath(AppHandle app, string name, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Split(['/', '\\']).Any(part => part is ".." or "."))
            throw new InvokeException("Store paths must be relative to the app data directory without traversal.");
        return await app.ResolveDirectoryAsync((int)BaseDirectory.AppData, name, token);
    }

    private static void Change(AppHandle app, StoreHandle handle, string key, JsonElement value, bool exists)
    {
        var payload = Write(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("resourceId", handle.Id); writer.WriteString("key", key);
            writer.WritePropertyName("value"); if (exists) value.WriteTo(writer); else writer.WriteNullValue();
            writer.WriteBoolean("exists", exists); writer.WriteEndObject();
        });
        app.Emit("store://change", payload);
    }

    private static JsonElement Required(JsonElement args, string name) => args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var value) ? value : throw new InvokeException("Missing store argument '" + name + "'.");
    private static string Text(JsonElement args, string name) => Required(args, name).GetString() ?? throw new InvokeException("Store argument '" + name + "' must be a string.");
    private static long Number(JsonElement args, string name) => Required(args, name).GetInt64();
    private static JsonElement Option(JsonElement args, string name) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) ? value : default;
    private static JsonElement Array(IEnumerable<JsonElement> values) => Write(writer =>
    {
        writer.WriteStartArray(); foreach (var value in values) value.WriteTo(writer); writer.WriteEndArray();
    });
    private static JsonElement Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) action(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private sealed class StoreHandle(StoreData data, string webviewLabel, ulong documentId)
    {
        public StoreData Data { get; } = data;
        public string WebviewLabel { get; } = webviewLabel;
        public ulong DocumentId { get; } = documentId;
        public uint Id { get; set; }
    }

    private sealed class StoreData
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, JsonElement> Values = new(StringComparer.Ordinal);
        public readonly Dictionary<string, JsonElement> Defaults = new(StringComparer.Ordinal);
        public readonly string Path;
        public readonly bool AutoSave;

        private StoreData(string path, bool autoSave) { Path = path; AutoSave = autoSave; }

        public static StoreData Load(string path, JsonElement options)
        {
            if (options.ValueKind == JsonValueKind.Object &&
                (options.TryGetProperty("serializeFnName", out _) || options.TryGetProperty("deserializeFnName", out _)))
                throw new InvokeException("Custom Rust store serializers are unavailable in the .NET plugin.");
            var autoSave = options.ValueKind != JsonValueKind.Object || !options.TryGetProperty("autoSave", out var setting) || setting.ValueKind != JsonValueKind.False;
            var store = new StoreData(path, autoSave);
            if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
                foreach (var entry in defaults.EnumerateObject()) store.Defaults[entry.Name] = entry.Value.Clone();
            store.Reload(options.ValueKind == JsonValueKind.Object && options.TryGetProperty("overrideDefaults", out var replace) && replace.ValueKind == JsonValueKind.True,
                options.ValueKind == JsonValueKind.Object && options.TryGetProperty("createNew", out var create) && create.ValueKind == JsonValueKind.True);
            return store;
        }

        public void Reload(bool ignoreDefaults, bool createNew = false)
        {
            var replacement = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (!ignoreDefaults) foreach (var entry in Defaults) replacement[entry.Key] = entry.Value.Clone();
            try
            {
                if (!createNew && File.Exists(Path))
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(Path));
                    if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvokeException("Store data must be a JSON object.");
                    foreach (var entry in document.RootElement.EnumerateObject()) replacement[entry.Name] = entry.Value.Clone();
                }
            }
            catch (JsonException error) { throw new InvokeException(error.Message, error); }
            Values.Clear();
            foreach (var entry in replacement) Values.Add(entry.Key, entry.Value);
        }

        public void SaveIfAutomatic() { if (AutoSave) Save(); }
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var entry in Values) { writer.WritePropertyName(entry.Key); entry.Value.WriteTo(writer); }
                writer.WriteEndObject();
            }
            var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Position = 0;
                    stream.CopyTo(file);
                    file.Flush(flushToDisk: true);
                }
                if (File.Exists(Path)) File.Replace(temporary, Path, null);
                else File.Move(temporary, Path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
