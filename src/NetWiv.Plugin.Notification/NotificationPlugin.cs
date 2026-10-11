using System.Buffers;
using System.Text.Json;

namespace NetWiv.Plugin.Notification;

/// <summary>Connects the official desktop notification guest to native OS notifications.</summary>
public sealed class NotificationPlugin : IPlugin
{
    public string Name => "notification";
    public string InitializationScript => """
        (() => {
          const invoke = (command, args) => window.__TAURI_INTERNALS__.invoke(command, args);
          function DesktopNotification(title, options = {}) {
            invoke('plugin:notification|notify', { options: { ...options, title } });
          }
          DesktopNotification.requestPermission = () => invoke('plugin:notification|request_permission');
          Object.defineProperty(DesktopNotification, 'permission', { get: () => 'granted' });
          window.Notification = DesktopNotification;
        })();
        """;

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("is_permission_granted", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)));
        context.RegisterCommand("request_permission", (_, _, _) => ValueTask.FromResult(JsonValue.From("granted")));
        context.RegisterCommand("notify", async (invoke, args, token) =>
        {
            if (!args.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Object)
                throw new InvokeException("Notification options must be an object.");
            var payload = NativePayload(options, invoke.App.Metadata);
            var response = await invoke.InvokePlatformAsync("plugin:notification|notify", payload, token);
            if (response.IsBinary) throw new InvokeException("Notification returned unexpected binary data.");
            return response.Json;
        });
        foreach (var command in new[] { "is_permission_granted", "request_permission", "notify" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default", "allow-is-permission-granted", "allow-request-permission", "allow-notify");
    }

    private static JsonElement NativePayload(JsonElement options, AppMetadata metadata)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("identifier", metadata.Identifier);
            writer.WritePropertyName("options"); writer.WriteStartObject();
            foreach (var property in options.EnumerateObject())
                if (property.Name != "title") property.WriteTo(writer);
            writer.WriteString("title", options.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? title.GetString() : metadata.Name);
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
