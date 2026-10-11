using System.Buffers;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Plugin.GlobalShortcut;

/// <summary>Registers desktop-wide shortcuts for the official Tauri guest package.</summary>
public sealed class GlobalShortcutPlugin : IPlugin
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, Registration> _registered = [];
    public string Name => "global-shortcut";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("register", Register);
        context.RegisterCommand("unregister", Unregister);
        context.RegisterCommand("unregister_all", UnregisterAll);
        context.RegisterCommand("is_registered", (invoke, args, token) =>
            Native(invoke, "is_registered", args, token));
        foreach (var command in new[] { "register", "unregister", "unregister_all", "is_registered" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default");
    }

    public void OnEvent(AppHandle app, RuntimeEvent runtimeEvent)
    {
        if (runtimeEvent.EventName != "netwiv://global-shortcut") return;
        var payload = runtimeEvent.Payload;
        if (!payload.TryGetProperty("id", out var value) || !value.TryGetUInt32(out var id)) return;
        Registration? registration;
        lock (_gate) _registered.TryGetValue(id, out registration);
        if (registration is null) return;
        try { registration.Channel.Send(payload); }
        catch (ObjectDisposedException) { }
    }

    public void OnDocumentChanged(AppHandle app, RuntimeDocumentChange change)
    {
        List<Registration> stale;
        lock (_gate)
        {
            stale = _registered.Values.Distinct().Where(entry => entry.WebviewLabel == change.WebviewLabel &&
                (change.Closed || entry.DocumentId != change.DocumentId)).ToList();
            foreach (var entry in stale)
                foreach (var id in entry.Ids) _registered.Remove(id);
        }
        foreach (var entry in stale) Close(entry.Channel);
        // The callback is on the UI thread; submitting a native command from here cannot block it.
        if (stale.Count > 0)
            _ = Task.Run(async () =>
            {
                foreach (var entry in stale)
                    try { await app.InvokeAsync("plugin:global-shortcut|unregister", Shortcuts(entry.Shortcuts)); }
                    catch (Exception) { /* The runtime may already be stopping. */ }
            });
    }

    public void Dispose()
    {
        Registration[] registrations;
        lock (_gate) { registrations = [.. _registered.Values.Distinct()]; _registered.Clear(); }
        foreach (var entry in registrations) Close(entry.Channel);
    }

    private async ValueTask<JsonElement> Register(InvokeContext invoke, JsonElement args, CancellationToken token)
    {
        var shortcuts = ParseShortcuts(args);
        if (!args.TryGetProperty("handler", out var rawHandler))
            throw new InvokeException("Global shortcut handler is required.");
        var channel = Channel.FromJson(invoke, rawHandler);
        try
        {
            var result = await Native(invoke, "register", Shortcuts(shortcuts), token);
            if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() != shortcuts.Length)
                throw new InvokeException("Native global shortcut registration returned invalid IDs.");
            var ids = result.EnumerateArray().Select(item => item.GetUInt32()).ToArray();
            var registration = new Registration(channel, invoke.WebviewLabel, invoke.DocumentId, ids, shortcuts);
            lock (_gate)
                foreach (var id in ids) _registered.Add(id, registration);
            return JsonValue.Null;
        }
        catch { Close(channel); throw; }
    }

    private async ValueTask<JsonElement> Unregister(InvokeContext invoke, JsonElement args, CancellationToken token)
    {
        var shortcuts = ParseShortcuts(args);
        var removed = await Native(invoke, "unregister", Shortcuts(shortcuts), token);
        // The native manager has now dropped the shortcut. Its event ID is no longer live.
        var closed = new List<Channel>();
        lock (_gate)
        {
            foreach (var id in removed.EnumerateArray().Select(value => value.GetUInt32()))
            {
                if (!_registered.Remove(id, out var registration)) continue;
                if (!registration.Ids.Any(_registered.ContainsKey)) closed.Add(registration.Channel);
            }
        }
        foreach (var channel in closed.Distinct()) Close(channel);
        return JsonValue.Null;
    }

    private async ValueTask<JsonElement> UnregisterAll(InvokeContext invoke, JsonElement args, CancellationToken token)
    {
        await Native(invoke, "unregister_all", args, token);
        Dispose();
        return JsonValue.Null;
    }

    private static async ValueTask<JsonElement> Native(InvokeContext invoke, string command, JsonElement args, CancellationToken token)
    {
        var response = await invoke.InvokePlatformAsync("plugin:global-shortcut|" + command, args, token);
        if (response.IsBinary) throw new InvokeException("Global shortcut returned unexpected binary data.");
        return response.Json;
    }

    private static string[] ParseShortcuts(JsonElement args)
    {
        if (!args.TryGetProperty("shortcuts", out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvokeException("Shortcuts must be an array.");
        return array.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvokeException("Shortcut must be a string.")).ToArray();
    }

    private static JsonElement Shortcuts(string[] shortcuts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
        writer.WriteStartObject(); writer.WritePropertyName("shortcuts"); writer.WriteStartArray();
        foreach (var shortcut in shortcuts) writer.WriteStringValue(shortcut);
        writer.WriteEndArray(); writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void Close(Channel channel)
    {
        try { channel.Dispose(); }
        catch (ObjectDisposedException) { }
    }

    private sealed record Registration(Channel Channel, string WebviewLabel, ulong DocumentId,
        uint[] Ids, string[] Shortcuts);
}
