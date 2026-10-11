using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Platform.Desktop;

internal static class RuntimeJson
{
    internal static byte[] Config(RuntimeContext context) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("initializationScript", context.InitializationScript);
        writer.WriteBoolean("ipcProtocolEnabled", context.InvokeProtocol is not null);
        writer.WriteBoolean("trackWebviewParents", true);
        writer.WriteString("localDataDirectory", context.LocalDataDirectory);
        writer.WriteString("appLocalDataDirectory", context.AppLocalDataDirectory);
        writer.WriteBoolean("assetProtocolEnabled", context.AssetProtocolEnabled);
        if (context.UriSchemeProtocols is { Count: > 0 } protocols)
        {
            writer.WriteStartArray("uriSchemeProtocols");
            foreach (var protocol in protocols) writer.WriteStringValue(protocol);
            writer.WriteEndArray();
        }
        if (context.IsolationProtocol is not null) writer.WriteString("isolationProtocol", context.IsolationProtocol);
        if (context.DefaultWindowIcon is { } icon)
        {
            writer.WritePropertyName("defaultWindowIcon");
            WriteImage(writer, icon);
        }
        writer.WriteStartArray("windows");
        foreach (var window in context.Windows)
        {
            writer.WriteStartObject();
            writer.WriteString("label", window.Label); writer.WriteString("title", window.Title);
            writer.WriteString("url", window.Url); writer.WriteNumber("width", window.Width); writer.WriteNumber("height", window.Height);
            writer.WritePropertyName("extraConfig"); Element(writer, window.ExtraConfig);
            if (window.Webviews is not null)
            {
                writer.WriteStartArray("webviews");
                foreach (var view in window.Webviews)
                {
                    writer.WriteStartObject(); writer.WriteString("label", view.Label); writer.WriteString("url", view.Url);
                    writer.WriteNumber("x", view.X); writer.WriteNumber("y", view.Y);
                    writer.WriteNumber("width", view.Width); writer.WriteNumber("height", view.Height);
                    writer.WritePropertyName("extraConfig"); Element(writer, view.ExtraConfig);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    });

    internal static byte[] Command(string command, JsonElement args, RuntimeCommandContext context, Func<uint, object?>? resolveResource) => Write(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("command", command);
        writer.WritePropertyName("args");
        WriteArguments(writer, args, resolveResource,
            command is "plugin:window|set_icon" or "plugin:window|set_overlay_icon");
        writer.WriteString("webviewLabel", context.WebviewLabel); writer.WriteString("windowLabel", context.WindowLabel);
        writer.WriteString("source", context.Source.AbsoluteUri); writer.WriteBoolean("isAuthorized", context.IsAuthorized);
        writer.WriteNumber("documentId", context.DocumentId);
        writer.WriteBoolean("isBackend", context.IsBackend);
        writer.WriteStartObject("scope");
        writer.WriteStartArray("allow"); foreach (var value in context.Scope?.Allow ?? []) value.WriteTo(writer); writer.WriteEndArray();
        writer.WriteStartArray("deny"); foreach (var value in context.Scope?.Deny ?? []) value.WriteTo(writer); writer.WriteEndArray();
        writer.WriteEndObject(); writer.WriteEndObject();
    });

    internal static byte[] Asset(AssetResponse response) => Write(writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("statusCode", response.StatusCode); writer.WriteString("contentType", response.ContentType);
        writer.WriteStartObject("headers");
        if (response.Headers is not null) foreach (var header in response.Headers) writer.WriteString(header.Key, header.Value);
        writer.WriteEndObject(); writer.WriteEndObject();
    });

    private static void WriteArguments(Utf8JsonWriter writer, JsonElement args, Func<uint, object?>? resolveResource, bool valueIsImage = false)
    {
        if (args.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in args.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if ((property.Name == "icon" || valueIsImage && property.Name == "value") && property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetUInt32(out var id) && resolveResource?.Invoke(id) is ImageData image)
                {
                    WriteImage(writer, image);
                }
                else WriteArguments(writer, property.Value, resolveResource);
            }
            writer.WriteEndObject();
        }
        else if (args.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var value in args.EnumerateArray()) WriteArguments(writer, value, resolveResource); writer.WriteEndArray();
        }
        else Element(writer, args);
    }

    private static void Element(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) writer.WriteStartObject();
        else { value.WriteTo(writer); return; }
        writer.WriteEndObject();
    }
    private static void WriteImage(Utf8JsonWriter writer, ImageData image)
    {
        writer.WriteStartObject(); writer.WriteNumber("width", image.Width); writer.WriteNumber("height", image.Height);
        writer.WriteStartArray("rgba"); foreach (var value in image.Rgba.Span) writer.WriteNumberValue(value); writer.WriteEndArray();
        writer.WriteEndObject();
    }
    private static byte[] Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { action(writer); writer.Flush(); }
        return stream.ToArray();
    }
}
