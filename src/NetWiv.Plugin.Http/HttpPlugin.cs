using System.Net;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Plugin.Http;

/// <summary>Scoped native HTTP transport for the official plugin-http fetch guest.</summary>
public sealed class HttpPlugin : IPlugin
{
    private static readonly string[] Commands = ["fetch", "fetch_send", "fetch_cancel", "fetch_read_body", "fetch_cancel_body"];
    public string Name => "http";

    public void Initialize(PluginContext context)
    {
        foreach (var command in Commands)
        {
            var name = command;
            if (name == "fetch_read_body")
                context.RegisterResponseCommand(name, ReadBody);
            else
                context.RegisterCommand(name, (invoke, args, token) => Execute(name, invoke, args, token));
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.PermissionSet("default", Commands.Select(name => "allow-" + name.Replace('_', '-')).ToArray());
    }

    private static async ValueTask<JsonElement> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        if (command == "fetch") return Create(context, args);
        var rid = args.GetProperty("rid").GetUInt32();
        var operation = context.App.Resources.Get<HttpOperation>(rid);
        EnsureOwner(context, operation);
        switch (command)
        {
            case "fetch_send":
                if (operation.Response is not null) throw new InvokeException("The HTTP request was already sent.");
                try { return await Send(context, operation, token); }
                catch
                {
                    context.App.Resources.Close(operation.RequestId);
                    if (operation.BodyId != 0) context.App.Resources.Close(operation.BodyId);
                    throw;
                }
            case "fetch_cancel":
            case "fetch_cancel_body":
                operation.Cancel();
                context.App.Resources.Close(operation.RequestId);
                if (operation.BodyId != 0) context.App.Resources.Close(operation.BodyId);
                return JsonValue.Null;
            default: throw new InvokeException("Unknown HTTP command.");
        }
    }

    private static JsonElement Create(InvokeContext context, JsonElement args)
    {
        var config = args.GetProperty("clientConfig");
        var url = new Uri(config.GetProperty("url").GetString() ?? "", UriKind.Absolute);
        EnsureUrl(context.Scope, context.IsTrusted, url);
        if (url.Scheme is not ("http" or "https")) throw new InvokeException("HTTP fetch supports only http and https URLs.");
        if (config.TryGetProperty("proxy", out var proxy) && proxy.ValueKind != JsonValueKind.Null ||
            config.TryGetProperty("danger", out var danger) && danger.ValueKind != JsonValueKind.Null)
            throw new InvokeException("Custom HTTP proxy and TLS danger settings are not implemented.");
        var method = config.TryGetProperty("method", out var methodValue) && methodValue.ValueKind == JsonValueKind.String
            ? methodValue.GetString()! : "GET";
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (config.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            var body = new byte[data.GetArrayLength()];
            var index = 0;
            foreach (var item in data.EnumerateArray()) body[index++] = item.GetByte();
            request.Content = new ByteArrayContent(body);
        }
        if (config.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
            foreach (var pair in headers.EnumerateArray())
            {
                var key = pair[0].GetString() ?? throw new InvokeException("HTTP header name is missing.");
                var value = pair[1].GetString() ?? throw new InvokeException("HTTP header value is missing.");
                if (!request.Headers.TryAddWithoutValidation(key, value))
                {
                    request.Content ??= new ByteArrayContent([]);
                    if (!request.Content.Headers.TryAddWithoutValidation(key, value))
                        throw new InvokeException("Invalid HTTP header: " + key);
                }
            }
        var redirects = config.TryGetProperty("maxRedirections", out var max) && max.ValueKind == JsonValueKind.Number
            ? max.GetInt32() : 10;
        if (redirects is < 0 or > 100) throw new InvokeException("Invalid maximum redirect count.");
        var operation = new HttpOperation(request, context, redirects);
        if (config.TryGetProperty("connectTimeout", out var timeout) && timeout.ValueKind == JsonValueKind.Number)
            operation.Timeout(TimeSpan.FromMilliseconds(timeout.GetDouble()));
        operation.RequestId = context.AddResource(operation);
        return JsonValue.From(operation.RequestId);
    }

    private static async Task<JsonElement> Send(InvokeContext context, HttpOperation operation, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Cancellation.Token);
        var request = operation.Request;
        HttpResponseMessage? response = null;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                response?.Dispose();
                response = await operation.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                if (!IsRedirect(response.StatusCode) || response.Headers.Location is null || redirects >= operation.MaxRedirects) break;
                var next = new Uri(request.RequestUri!, response.Headers.Location);
                EnsureUrl(operation.Allow, operation.Deny, operation.Trusted, next);
                if (next.Scheme is not ("http" or "https")) throw new InvokeException("HTTP redirect target is not supported.");
                var nextMethod = response.StatusCode is HttpStatusCode.SeeOther or HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently
                    ? HttpMethod.Get : request.Method;
                var previous = request;
                request = new HttpRequestMessage(nextMethod, next);
                if (nextMethod != HttpMethod.Get && previous.Content is not null)
                {
                    var bytes = await previous.Content.ReadAsByteArrayAsync(linked.Token);
                    request.Content = new ByteArrayContent(bytes);
                    foreach (var header in previous.Content.Headers) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                foreach (var header in previous.Headers)
                    if (next.GetLeftPart(UriPartial.Authority) == previous.RequestUri!.GetLeftPart(UriPartial.Authority) ||
                        !(header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                          header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                          header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)))
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                previous.Dispose();
            }
            operation.Request = request;
            operation.Response = response;
            operation.Stream = await response.Content.ReadAsStreamAsync(linked.Token);
            operation.BodyId = context.AddResource(operation);
            return Write(writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("status", (int)response.StatusCode);
                writer.WriteString("statusText", response.ReasonPhrase ?? "");
                writer.WriteString("url", response.RequestMessage?.RequestUri?.AbsoluteUri ?? request.RequestUri!.AbsoluteUri);
                writer.WriteNumber("rid", operation.BodyId);
                writer.WritePropertyName("headers"); writer.WriteStartArray();
                foreach (var header in response.Headers.Concat(response.Content.Headers))
                    foreach (var value in header.Value)
                    { writer.WriteStartArray(); writer.WriteStringValue(header.Key); writer.WriteStringValue(value); writer.WriteEndArray(); }
                writer.WriteEndArray(); writer.WriteEndObject();
            });
        }
        catch
        {
            // A reload can dispose the registered operation before a late response
            // or redirected request is assigned to it. These local values still own cleanup.
            response?.Dispose();
            request.Dispose();
            throw;
        }
    }

    private static async ValueTask<RuntimeInvokeResponse> ReadBody(InvokeContext context, JsonElement args, CancellationToken token)
    {
        var rid = args.GetProperty("rid").GetUInt32();
        var operation = context.App.Resources.Get<HttpOperation>(rid);
        EnsureOwner(context, operation);
        if (rid != operation.BodyId || operation.Stream is null) throw new InvokeException("The HTTP body is unavailable.");
        await operation.ReadGate.WaitAsync(token);
        byte[] result;
        bool completed;
        try
        {
            var buffer = new byte[64 * 1024 + 1];
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Cancellation.Token);
            var size = await operation.Stream.ReadAsync(buffer.AsMemory(0, buffer.Length - 1), linked.Token);
            completed = size == 0;
            buffer[size] = completed ? (byte)1 : (byte)0;
            result = buffer.AsMemory(0, size + 1).ToArray();
        }
        finally { operation.ReadGate.Release(); }
        if (completed)
        {
            context.App.Resources.Close(operation.RequestId);
            context.App.Resources.Close(operation.BodyId);
        }
        return RuntimeInvokeResponse.FromBinary(result);
    }

    private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static JsonElement Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) action(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void EnsureOwner(InvokeContext context, HttpOperation operation)
    {
        if (!context.IsTrusted && (context.WebviewLabel != operation.WebviewLabel || context.DocumentId != operation.DocumentId))
            throw new InvokeException("HTTP resource belongs to another document.");
    }

    private static void EnsureUrl(RuntimeCommandScope scope, bool trusted, Uri url) => EnsureUrl(scope.Allow, scope.Deny, trusted, url);

    private static void EnsureUrl(IReadOnlyList<JsonElement> allow, IReadOnlyList<JsonElement> deny, bool trusted, Uri url)
    {
        if (trusted) return;
        static bool Matches(JsonElement entry, Uri value) => entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty("url", out var pattern) && pattern.ValueKind == JsonValueKind.String &&
            new UrlScopePattern(pattern.GetString()!).Matches(value);
        if (deny.Any(entry => Matches(entry, url)) || !allow.Any(entry => Matches(entry, url)))
            throw new InvokeException("URL is not allowed by HTTP scope.");
    }

    private sealed class HttpOperation : IDisposable
    {
        public HttpRequestMessage Request;
        public readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli });
        public readonly CancellationTokenSource Cancellation = new();
        public readonly SemaphoreSlim ReadGate = new(1, 1);
        public readonly string WebviewLabel;
        public readonly ulong DocumentId;
        public readonly IReadOnlyList<JsonElement> Allow;
        public readonly IReadOnlyList<JsonElement> Deny;
        public readonly bool Trusted;
        public readonly int MaxRedirects;
        public HttpResponseMessage? Response;
        public Stream? Stream;
        public uint RequestId;
        public uint BodyId;
        private int _disposed;

        public HttpOperation(HttpRequestMessage request, InvokeContext context, int maxRedirects)
        {
            Request = request;
            WebviewLabel = context.WebviewLabel;
            DocumentId = context.DocumentId;
            Allow = context.Scope.Allow.Select(item => item.Clone()).ToArray();
            Deny = context.Scope.Deny.Select(item => item.Clone()).ToArray();
            Trusted = context.IsTrusted;
            MaxRedirects = maxRedirects;
        }

        public void Timeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(1)) throw new InvokeException("Invalid HTTP timeout.");
            Cancellation.CancelAfter(timeout);
        }

        public void Cancel()
        {
            try { if (Volatile.Read(ref _disposed) == 0) Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Cancellation.Cancel();
            Stream?.Dispose(); Response?.Dispose(); Request.Dispose(); Client.Dispose(); Cancellation.Dispose();
        }
    }
}
