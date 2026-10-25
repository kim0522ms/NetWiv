using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NetWiv;

public sealed class Channel : IDisposable
{
    private readonly AppHandle _app;
    private readonly string _webviewLabel;
    private readonly ulong _documentId;
    private readonly object _gate = new();
    private long _index;
    private bool _disposed;

    public Channel(AppHandle app, string webviewLabel, uint callbackId, ulong documentId = 0)
    {
        _app = app;
        _webviewLabel = webviewLabel;
        Id = callbackId;
        _documentId = documentId;
    }

    public uint Id { get; }

    public static Channel FromJson(InvokeContext context, JsonElement value)
    {
        if (context.Authorization is not null) throw new InvokeException("Scoped backend invocations do not have guest channels.");
        var text = value.GetString();
        const string prefix = "__CHANNEL__:";
        if (text is null || !text.StartsWith(prefix, StringComparison.Ordinal) ||
            !uint.TryParse(text.AsSpan(prefix.Length), out var id))
            throw new InvokeException("The value is not a valid Tauri channel identifier.");
        return new Channel(context.App, context.WebviewLabel, id, context.DocumentId);
    }

    public void Send(JsonElement message)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var payload = JsonValue.Write(writer =>
            {
                writer.WriteStartObject();
                writer.WritePropertyName("message"); message.WriteTo(writer);
                writer.WriteNumber("index", _index++);
                writer.WriteEndObject();
            });
            _app.RunCallback(_webviewLabel, Id, payload, _documentId);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            var payload = JsonValue.Write(writer =>
            {
                writer.WriteStartObject(); writer.WriteBoolean("end", true); writer.WriteNumber("index", _index); writer.WriteEndObject();
            });
            _app.RunCallback(_webviewLabel, Id, payload, _documentId);
        }
    }
}

public sealed class Channel<T> : IDisposable
{
    private readonly Channel _channel;
    private readonly JsonTypeInfo<T> _typeInfo;
    public Channel(Channel channel, JsonTypeInfo<T> typeInfo) { _channel = channel; _typeInfo = typeInfo; }
    public uint Id => _channel.Id;
    public void Send(T message) => _channel.Send(JsonSerializer.SerializeToElement(message, _typeInfo));
    public void Dispose() => _channel.Dispose();
}
