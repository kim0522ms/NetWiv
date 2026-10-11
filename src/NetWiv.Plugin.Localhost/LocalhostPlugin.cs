using System.Net;
using System.Net.Sockets;
using System.Text;
using NetWiv.Runtime;

namespace NetWiv.Plugin.Localhost;

public sealed class LocalhostRequest(string url)
{
    public string Url { get; } = url;
}

public sealed class LocalhostResponse(IDictionary<string, string> headers)
{
    public void AddHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')) ||
            value.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Invalid HTTP response header.");
        headers[name] = value;
    }
}

/// <summary>Serves bundled frontend assets over HTTP on a selected local port.</summary>
public sealed class LocalhostPlugin(ushort port, string host = "localhost",
    Action<LocalhostRequest, LocalhostResponse>? onRequest = null) : IPlugin
{
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _loop;

    public string Name => "localhost";
    public int BoundPort { get; private set; }
    public void Initialize(PluginContext context) { }

    public void Setup(AppHandle app)
    {
        var address = host == "localhost" ? IPAddress.Loopback :
            IPAddress.TryParse(host, out var parsed) ? parsed : Dns.GetHostAddresses(host).First();
        _listener = new TcpListener(address, port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => AcceptAsync(app, _stop.Token));
    }

    private async Task AcceptAsync(AppHandle app, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    try { await RespondAsync(client.GetStream(), app, timeout.Token); }
                    catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                    { /* Clients may disconnect before receiving the response. */ }
                }
            });
        }
    }

    private async Task RespondAsync(NetworkStream stream, AppHandle app, CancellationToken token)
    {
        var first = await ReadLineAsync(stream, 8192, token);
        if (first is null) return;
        var parts = first.Split(' ', 3);
        if (parts.Length != 3 || parts[2] != "HTTP/1.1" && parts[2] != "HTTP/1.0") return;
        var total = first.Length;
        while (true)
        {
            var line = await ReadLineAsync(stream, 8192, token);
            if (line is null) return;
            total += line.Length;
            if (total > 65536) return;
            if (line.Length == 0) break;
        }
        if (parts[0] is not ("GET" or "HEAD"))
        {
            await WriteAsync(stream, 405, "text/plain", "Method Not Allowed"u8.ToArray(),
                new Dictionary<string, string> { ["Allow"] = "GET, HEAD" }, parts[0] == "HEAD", token);
            return;
        }
        if (!parts[1].StartsWith('/') || parts[1].StartsWith("//", StringComparison.Ordinal) ||
            !Uri.TryCreate("tauri://localhost" + parts[1], UriKind.Absolute, out var uri))
        {
            await WriteAsync(stream, 400, "text/plain", "Bad Request"u8.ToArray(), null, parts[0] == "HEAD", token);
            return;
        }
        var asset = app.ResolveAsset(new AssetRequest(uri, parts[0], new Dictionary<string, string>()));
        if (asset is null) return;
        var headers = asset.Headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(asset.Headers, StringComparer.OrdinalIgnoreCase);
        headers["Cache-Control"] = "no-cache";
        onRequest?.Invoke(new LocalhostRequest(parts[1]), new LocalhostResponse(headers));
        await WriteAsync(stream, asset.StatusCode, asset.ContentType, asset.Body, headers, parts[0] == "HEAD", token);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int limit, CancellationToken token)
    {
        var bytes = new byte[limit];
        var one = new byte[1];
        var length = 0;
        while (length < limit)
        {
            if (await stream.ReadAsync(one, token) == 0) return null;
            if (one[0] == '\n')
                return Encoding.ASCII.GetString(bytes, 0, length > 0 && bytes[length - 1] == '\r' ? length - 1 : length);
            bytes[length++] = one[0];
        }
        return null;
    }

    private static async Task WriteAsync(Stream stream, int status, string contentType, ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string>? headers, bool head, CancellationToken token)
    {
        var phrase = status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", 405 => "Method Not Allowed", 500 => "Internal Server Error", _ => "Response" };
        if (contentType.IndexOfAny(['\r', '\n']) >= 0) contentType = "application/octet-stream";
        var builder = new StringBuilder($"HTTP/1.1 {status} {phrase}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n");
        if (headers is not null)
            foreach (var (name, value) in headers)
                if (name is not ("Content-Type" or "Content-Length" or "Connection") &&
                    name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
                    value.IndexOfAny(['\r', '\n']) < 0)
                    builder.Append(name).Append(": ").Append(value).Append("\r\n");
        builder.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), token);
        if (!head) await stream.WriteAsync(body, token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        _stop.Dispose();
    }
}
