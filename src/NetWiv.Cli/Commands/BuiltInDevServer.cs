using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace NetWiv.Cli;

internal sealed class BuiltInDevServer : IAsyncDisposable
{
    private const string ReloadPath = "/.netwiv/reload";
    private const string ReloadScriptPath = "/.netwiv/reload.js";
    private const string ReloadScript = "new EventSource('/.netwiv/reload').addEventListener('reload',()=>location.reload());";
    private readonly string _root;
    private readonly HttpListener _listener = new();
    private readonly FileSystemWatcher _watcher;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<long, Channel<bool>> _clients = new();
    private readonly Timer _reloadTimer;
    private readonly Task _acceptLoop;
    private long _nextClient;

    private BuiltInDevServer(string root, int port)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _reloadTimer = new Timer(static state => ((BuiltInDevServer)state!).BroadcastReload(), this, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        FileSystemEventHandler changed = (_, _) => Reload();
        RenamedEventHandler renamed = (_, _) => Reload();
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += renamed;
        _acceptLoop = AcceptAsync();
    }

    public string Url { get; }

    public static BuiltInDevServer Start(string root, int port, bool autoPort = false)
    {
        var fullPath = Path.GetFullPath(root);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"build.frontendDist does not exist: {fullPath}");
        if (!File.Exists(Path.Combine(fullPath, "index.html"))) throw new FileNotFoundException($"build.frontendDist must contain index.html: {fullPath}");
        while (true)
        {
            try { return new BuiltInDevServer(fullPath, port); }
            catch (HttpListenerException) when (autoPort && port < 65535) { port++; }
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception exception) when (_stopping.IsCancellationRequested && exception is HttpListenerException or ObjectDisposedException) { break; }
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == ReloadPath) { await StreamReloadsAsync(context.Response); return; }
            if (path == ReloadScriptPath)
            {
                await WriteAsync(context.Response, Encoding.UTF8.GetBytes(ReloadScript), "text/javascript; charset=utf-8", context.Request.HttpMethod == "HEAD");
                return;
            }
            if (context.Request.HttpMethod is not "GET" and not "HEAD")
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Close();
                return;
            }

            var file = ResolveFiles(path).FirstOrDefault(File.Exists);
            if (file is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }
            var bytes = await File.ReadAllBytesAsync(file, _stopping.Token);
            if (Path.GetExtension(file).Equals(".html", StringComparison.OrdinalIgnoreCase))
                bytes = InjectReloadScript(bytes);
            await WriteAsync(context.Response, bytes, ContentType(file), context.Request.HttpMethod == "HEAD");
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or OperationCanceledException) { context.Response.Abort(); }
    }

    private IEnumerable<string> ResolveFiles(string urlPath)
    {
        string relative;
        try { relative = Uri.UnescapeDataString(urlPath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar); }
        catch (UriFormatException) { yield break; }
        if (relative.IndexOf('\0') >= 0) yield break;
        if (relative.Length == 0) { yield return Path.Combine(_root, "index.html"); yield break; }
        var fullPath = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) yield break;
        yield return fullPath;
        yield return fullPath + ".html";
        yield return Path.Combine(fullPath, "index.html");
        yield return Path.Combine(_root, "index.html");
    }

    private async Task StreamReloadsAsync(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;
        var id = Interlocked.Increment(ref _nextClient);
        var channel = Channel.CreateBounded<bool>(1);
        _clients[id] = channel;
        try
        {
            await response.OutputStream.WriteAsync("retry: 250\n\n"u8.ToArray(), _stopping.Token);
            await response.OutputStream.FlushAsync(_stopping.Token);
            await foreach (var _ in channel.Reader.ReadAllAsync(_stopping.Token))
            {
                await response.OutputStream.WriteAsync("event: reload\ndata: changed\n\n"u8.ToArray(), _stopping.Token);
                await response.OutputStream.FlushAsync(_stopping.Token);
            }
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or OperationCanceledException) { }
        finally { _clients.TryRemove(id, out _); response.Close(); }
    }

    private static async Task WriteAsync(HttpListenerResponse response, byte[] bytes, string contentType, bool headersOnly)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentLength64 = bytes.Length;
        if (!headersOnly) await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static byte[] InjectReloadScript(byte[] bytes)
    {
        var html = Encoding.UTF8.GetString(bytes);
        const string tag = "<script src=\"/.netwiv/reload.js\"></script>";
        var body = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return Encoding.UTF8.GetBytes(body < 0 ? html + tag : html.Insert(body, tag));
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".wasm" => "application/wasm",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    private void Reload()
    {
        _reloadTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    private void BroadcastReload()
    {
        foreach (var client in _clients.Values) client.Writer.TryWrite(true);
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        await _reloadTimer.DisposeAsync();
        _stopping.Cancel();
        foreach (var client in _clients.Values) client.Writer.TryComplete();
        _listener.Close();
        try { await _acceptLoop; } catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { }
        _stopping.Dispose();
    }
}
