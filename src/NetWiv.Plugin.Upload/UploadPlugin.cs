using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NetWiv.Plugin.Upload;

/// <summary>File upload/download transport for the official @tauri-apps/plugin-upload guest.</summary>
public sealed class UploadPlugin : IPlugin
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    public string Name => "upload";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("upload", Upload);
        context.RegisterCommand("download", Download);
        foreach (var command in new[] { "upload", "download" })
        {
            context.Permission(new("allow-" + command, [command], []));
            context.Permission(new("deny-" + command, [], [command]));
        }
        context.PermissionSet("default", "allow-upload", "allow-download");
    }

    private static async ValueTask<JsonElement> Upload(InvokeContext context, JsonElement args, CancellationToken token)
    {
        var uri = Url(args);
        var path = Path.GetFullPath(Text(args, "filePath"));
        EnsurePath(context, path);
        EnsureUrl(context, uri);
        var method = args.TryGetProperty("method", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : "POST";
        if (method is not ("POST" or "PUT" or "PATCH")) throw new InvokeException("Upload method must be POST, PUT, or PATCH.");
        using var channel = Channel.FromJson(context, args.GetProperty("onProgress"));
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        request.Content = new ProgressContent(path, channel);
        Headers(request, args);
        using var response = await Send(context, request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        EnsureSuccess(response, body);
        return JsonValue.From(body);
    }

    private static async ValueTask<JsonElement> Download(InvokeContext context, JsonElement args, CancellationToken token)
    {
        var uri = Url(args);
        var path = Path.GetFullPath(Text(args, "filePath"));
        EnsurePath(context, path);
        EnsureUrl(context, uri);
        using var channel = Channel.FromJson(context, args.GetProperty("onProgress"));
        var method = args.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? HttpMethod.Post : HttpMethod.Get;
        using var request = new HttpRequestMessage(method, uri);
        if (method == HttpMethod.Post) request.Content = new StringContent(body.GetString()!, Encoding.UTF8);
        Headers(request, args);
        using var response = await Send(context, request, token);
        if (!response.IsSuccessStatusCode)
            EnsureSuccess(response, await response.Content.ReadAsStringAsync(token));
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                var timer = Stopwatch.StartNew();
                long transferred = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    transferred += read;
                    Progress(channel, read, transferred, total, timer);
                }
                await target.FlushAsync(token);
                target.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return JsonValue.Null;
    }

    private static void EnsureUrl(InvokeContext context, Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) throw new InvokeException("Upload URL must use HTTP or HTTPS.");
        if (context.IsTrusted) return;
        static bool Matches(JsonElement entry, Uri url) => entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty("url", out var pattern) && pattern.ValueKind == JsonValueKind.String &&
            new UrlScopePattern(pattern.GetString()!).Matches(url);
        if (context.Scope.Deny.Any(entry => Matches(entry, uri)) || !context.Scope.Allow.Any(entry => Matches(entry, uri)))
            throw new InvokeException("URL is not allowed by upload scope.");
    }

    private static void EnsurePath(InvokeContext context, string path)
    {
        if (context.IsTrusted) return;
        static IEnumerable<string> Paths(IReadOnlyList<JsonElement> entries) => entries
            .Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("path", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetProperty("path").GetString()!);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("allow"); writer.WriteStartArray(); foreach (var pattern in Paths(context.Scope.Allow)) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WritePropertyName("deny"); writer.WriteStartArray(); foreach (var pattern in Paths(context.Scope.Deny)) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var scope = new FileScope(document.RootElement, context.App.Metadata.Identifier);
        var root = Path.GetPathRoot(path)!;
        var resolved = root;
        foreach (var part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            var info = new FileInfo(resolved);
            if (info.LinkTarget is not null) resolved = info.ResolveLinkTarget(true)?.FullName ?? throw new InvokeException("Upload scope link cannot be resolved.");
        }
        if (!scope.IsAllowed(path) || !scope.IsAllowed(resolved)) throw new InvokeException("File is not allowed by upload scope.");
    }

    private static async Task<HttpResponseMessage> Send(InvokeContext context, HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                EnsureUrl(context, request.RequestUri!);
                var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null) return response;
                using (response)
                {
                    if (redirects == 10) throw new InvokeException("Upload redirect limit exceeded.");
                    var next = new Uri(request.RequestUri!, response.Headers.Location);
                    EnsureUrl(context, next);
                    var method = response.StatusCode == HttpStatusCode.SeeOther ||
                        request.Method == HttpMethod.Post && (int)response.StatusCode is 301 or 302 ? HttpMethod.Get : request.Method;
                    var replacement = new HttpRequestMessage(method, next);
                    if (method == request.Method) { replacement.Content = request.Content; request.Content = null; }
                    foreach (var header in request.Headers)
                        if (next.GetLeftPart(UriPartial.Authority) == request.RequestUri!.GetLeftPart(UriPartial.Authority) ||
                            !(header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                              header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                              header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)))
                            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    request.Dispose();
                    request = replacement;
                }
            }
        }
        finally { request.Dispose(); }
    }

    private static Uri Url(JsonElement args)
    {
        var text = Text(args, "url");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvokeException("Upload URL must use HTTP or HTTPS.");
        return uri;
    }

    private static string Text(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvokeException($"Missing upload argument '{name}'.");

    private static void Headers(HttpRequestMessage request, JsonElement args)
    {
        if (!args.TryGetProperty("headers", out var headers) || headers.ValueKind == JsonValueKind.Null) return;
        if (headers.ValueKind != JsonValueKind.Object) throw new InvokeException("Upload headers must be an object.");
        foreach (var header in headers.EnumerateObject())
        {
            if (header.Value.ValueKind != JsonValueKind.String) throw new InvokeException("Upload header values must be strings.");
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString()) &&
                (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString())))
                throw new InvokeException("Invalid upload header: " + header.Name);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvokeException($"request failed with status code {(int)response.StatusCode}: {body}");
    }

    private static void Progress(Channel channel, long chunk, long transferred, long total, Stopwatch timer)
    {
        var speed = (long)(transferred / Math.Max(timer.Elapsed.TotalSeconds, 0.001));
        using var document = JsonDocument.Parse("{\"progress\":" + chunk + ",\"progressTotal\":" + transferred +
            ",\"total\":" + total + ",\"transferSpeed\":" + speed + "}");
        try { channel.Send(document.RootElement); } catch (ObjectDisposedException) { }
    }

    private sealed class ProgressContent : HttpContent
    {
        private readonly string _path;
        private readonly Channel _channel;
        private readonly long _length;
        public ProgressContent(string path, Channel channel)
        {
            _path = path; _channel = channel;
            _length = new FileInfo(path).Length;
            Headers.ContentLength = _length;
        }
        protected override bool TryComputeLength(out long length) { length = _length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var buffer = new byte[81920];
            var timer = Stopwatch.StartNew();
            long transferred = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, token)) != 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), token);
                transferred += read;
                Progress(_channel, read, transferred, _length, timer);
            }
        }
    }
}
