using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NetWiv.Plugin.Websocket;

/// <summary>Desktop WebSocket transport for the official @tauri-apps/plugin-websocket guest.</summary>
public sealed class WebsocketPlugin : IPlugin
{
    private readonly Dictionary<int, Connection> _connections = [];
    private readonly object _gate = new();
    private int _nextId;
    public string Name => "websocket";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("connect", Connect);
        context.RegisterCommand("send", Send);
        foreach (var command in new[] { "connect", "send" })
        {
            context.Permission(new("allow-" + command, [command], []));
            context.Permission(new("deny-" + command, [], [command]));
        }
        context.PermissionSet("default", "allow-connect", "allow-send");
    }

    public void Dispose()
    {
        Connection[] values;
        lock (_gate) { values = [.. _connections.Values]; _connections.Clear(); }
        foreach (var value in values) value.Dispose();
    }

    public void OnDocumentChanged(AppHandle app, NetWiv.Runtime.RuntimeDocumentChange change)
    {
        Connection[] stale;
        lock (_gate)
        {
            var entries = _connections.Where(entry => entry.Value.WebviewLabel == change.WebviewLabel &&
                (change.Closed || entry.Value.DocumentId != change.DocumentId)).ToArray();
            stale = entries.Select(entry => entry.Value).ToArray();
            foreach (var entry in entries) _connections.Remove(entry.Key);
        }
        foreach (var connection in stale) connection.Dispose();
    }

    private async ValueTask<JsonElement> Connect(InvokeContext context, JsonElement args, CancellationToken token)
    {
        if (!args.TryGetProperty("url", out var rawUrl) || rawUrl.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(rawUrl.GetString(), UriKind.Absolute, out var url) || url.Scheme is not ("ws" or "wss"))
            throw new InvokeException("WebSocket URL must use ws or wss.");
        var config = args.TryGetProperty("config", out var options) && options.ValueKind == JsonValueKind.Object ? options : default;
        if (config.ValueKind == JsonValueKind.Object)
        {
            foreach (var unsupported in new[] { "writeBufferSize", "maxWriteBufferSize", "maxFrameSize", "acceptUnmaskedFrames" })
                if (config.TryGetProperty(unsupported, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.False)
                    throw new InvokeException($"WebSocket option '{unsupported}' is not implemented.");
        }
        var maxBytes = 64 * 1024 * 1024;
        var readBufferSize = 8192;
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("readBufferSize", out var readSize) && readSize.ValueKind != JsonValueKind.Null)
        {
            if (!readSize.TryGetInt32(out readBufferSize) || readBufferSize is < 1 or > 1024 * 1024)
                throw new InvokeException("Invalid WebSocket readBufferSize.");
        }
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("maxMessageSize", out var max) && max.ValueKind != JsonValueKind.Null)
        {
            if (max.ValueKind == JsonValueKind.String && max.GetString() == "none") maxBytes = int.MaxValue;
            else if (max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var size) && size > 0) maxBytes = size;
            else throw new InvokeException("Invalid WebSocket maxMessageSize.");
        }
        var channel = Channel.FromJson(context, args.GetProperty("onMessage"));
        var socket = new ClientWebSocket();
        try
        {
            if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
                foreach (var header in headers.EnumerateArray())
                {
                    if (header.ValueKind != JsonValueKind.Array || header.GetArrayLength() != 2)
                        throw new InvokeException("WebSocket headers must be name-value pairs.");
                    socket.Options.SetRequestHeader(header[0].GetString()!, header[1].GetString()!);
                }
            await socket.ConnectAsync(url, token);
            var id = Interlocked.Increment(ref _nextId);
            var connection = new Connection(socket, channel, context.WebviewLabel, context.DocumentId, maxBytes, readBufferSize);
            lock (_gate) _connections.Add(id, connection);
            _ = Receive(id, connection);
            return JsonValue.From(id);
        }
        catch
        {
            socket.Dispose();
            try { channel.Dispose(); } catch (ObjectDisposedException) { }
            throw;
        }
    }

    private async ValueTask<JsonElement> Send(InvokeContext context, JsonElement args, CancellationToken token)
    {
        if (!args.TryGetProperty("id", out var rawId) || !rawId.TryGetInt32(out var id))
            throw new InvokeException("WebSocket connection ID is required.");
        Connection connection;
        lock (_gate) connection = _connections.TryGetValue(id, out var found) ? found : throw new InvokeException($"connection not found for the given id: {id}");
        if (!context.IsTrusted && (connection.WebviewLabel != context.WebviewLabel || connection.DocumentId != context.DocumentId))
            throw new InvokeException("WebSocket connection belongs to another document.");
        var message = args.GetProperty("message");
        var kind = message.GetProperty("type").GetString();
        var data = message.GetProperty("data");
        await connection.Writer.WaitAsync(token);
        try
        {
            switch (kind)
            {
                case "Text": await connection.Socket.SendAsync(Encoding.UTF8.GetBytes(data.GetString()!), WebSocketMessageType.Text, true, token); break;
                case "Binary": await connection.Socket.SendAsync(Bytes(data), WebSocketMessageType.Binary, true, token); break;
                case "Close":
                {
                    var code = data.ValueKind == JsonValueKind.Null ? WebSocketCloseStatus.NormalClosure : (WebSocketCloseStatus)data.GetProperty("code").GetInt32();
                    var reason = data.ValueKind == JsonValueKind.Null ? "" : data.GetProperty("reason").GetString() ?? "";
                    await connection.Socket.CloseOutputAsync(code, reason, token);
                    break;
                }
                case "Ping" or "Pong": throw new InvokeException($"WebSocket {kind} frames are not implemented by this .NET transport.");
                default: throw new InvokeException("Invalid WebSocket message type.");
            }
        }
        finally { connection.Writer.Release(); }
        return JsonValue.Null;
    }

    private async Task Receive(int id, Connection connection)
    {
        var buffer = new byte[connection.ReadBufferSize];
        using var output = new MemoryStream();
        try
        {
            while (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await connection.Socket.ReceiveAsync(buffer, connection.Stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    connection.Send("Close", JsonValue.Parse("{\"code\":" + (int)(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure) +
                        ",\"reason\":" + JsonValue.From(result.CloseStatusDescription ?? "").GetRawText() + "}"));
                    break;
                }
                if (output.Length + result.Count > connection.MaxBytes) throw new InvokeException("WebSocket message exceeds maxMessageSize.");
                output.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var bytes = output.ToArray();
                connection.Send(result.MessageType == WebSocketMessageType.Text ? "Text" : "Binary",
                    result.MessageType == WebSocketMessageType.Text ? JsonValue.From(Encoding.UTF8.GetString(bytes)) : ByteArray(bytes));
                output.SetLength(0);
            }
        }
        catch (Exception error) when (error is WebSocketException or IOException or InvokeException or OperationCanceledException or ObjectDisposedException)
        {
            if (!connection.Stop.IsCancellationRequested) connection.SendError(error.Message);
        }
        finally
        {
            lock (_gate) _connections.Remove(id);
            connection.Dispose();
        }
    }

    private static byte[] Bytes(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(item => item.GetByte()).ToArray()
        : throw new InvokeException("WebSocket binary data must be a byte array.");

    private static JsonElement ByteArray(byte[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        { writer.WriteStartArray(); foreach (var value in values) writer.WriteNumberValue(value); writer.WriteEndArray(); }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private sealed class Connection(ClientWebSocket socket, Channel channel, string webviewLabel, ulong documentId, int maxBytes, int readBufferSize) : IDisposable
    {
        private int _disposed;
        public ClientWebSocket Socket { get; } = socket;
        public SemaphoreSlim Writer { get; } = new(1, 1);
        public CancellationTokenSource Stop { get; } = new();
        public string WebviewLabel { get; } = webviewLabel;
        public ulong DocumentId { get; } = documentId;
        public int MaxBytes { get; } = maxBytes;
        public int ReadBufferSize { get; } = readBufferSize;
        public void Send(string kind, JsonElement payload)
        {
            using var document = JsonDocument.Parse("{\"type\":" + JsonValue.From(kind).GetRawText() + ",\"data\":" + payload.GetRawText() + "}");
            try { channel.Send(document.RootElement); } catch (ObjectDisposedException) { }
        }
        public void SendError(string error)
        {
            try { channel.Send(JsonValue.From(error)); } catch (ObjectDisposedException) { }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stop.Cancel(); Socket.Abort(); Socket.Dispose();
            try { channel.Dispose(); } catch (ObjectDisposedException) { }
            Stop.Dispose();
        }
    }
}
