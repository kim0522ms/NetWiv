using System.Buffers;
using System.Text.Json;

namespace NetWiv.Plugin.Stronghold;

/// <summary>Exposes the official Stronghold guest to the native IOTA Stronghold engine.</summary>
public sealed class StrongholdPlugin(string? saltPath = null) : IPlugin
{
    private readonly string? _saltPath = saltPath;
    public string Name => "stronghold";

    public void Initialize(PluginContext context)
    {
        var commands = new[]
        {
            "initialize", "destroy", "save", "create_client", "load_client",
            "get_store_record", "save_store_record", "remove_store_record",
            "save_secret", "remove_secret", "execute_procedure"
        };
        foreach (var command in commands)
        {
            var name = command;
            context.RegisterCommand(name, (invoke, args, token) => Forward(invoke, name, args, token));
            var permission = name.Replace('_', '-');
            context.Permission(new("allow-" + permission, [name], []));
            context.Permission(new("deny-" + permission, [], [name]));
        }
        context.PermissionSet("default", "allow-create-client", "allow-get-store-record",
            "allow-initialize", "allow-execute-procedure", "allow-load-client",
            "allow-save-secret", "allow-save-store-record", "allow-save");
    }

    private async ValueTask<JsonElement> Forward(InvokeContext invoke, string command, JsonElement args, CancellationToken token)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("snapshotPath", out var snapshot) ||
            snapshot.ValueKind != JsonValueKind.String)
            throw new InvokeException("Stronghold snapshotPath must be a string.");
        if (command == "initialize" && _saltPath is not null)
            args = WithSaltPath(args, _saltPath);
        var result = await invoke.InvokePlatformAsync("plugin:stronghold|" + command, args, token);
        if (result.IsBinary) throw new InvokeException("Stronghold returned unexpected binary data.");
        return result.Json;
    }

    private static JsonElement WithSaltPath(JsonElement args, string path)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in args.EnumerateObject())
                if (property.Name != "saltPath") property.WriteTo(writer);
            writer.WriteString("saltPath", path);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
