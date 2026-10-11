using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

internal sealed class IsolationPattern : IDisposable
{
    internal const string IframeStyle = "#__tauri_isolation__ { display: none !important }";
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _html;
    private bool _disposed;
    internal string Scheme { get; } = "isolation-" + Guid.NewGuid().ToString("D");

    internal IsolationPattern(Assembly assembly)
    {
        const string prefix = "netwiv.isolation.";
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(static name => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files.Add(name[prefix.Length..].Replace('\\', '/'), reader.ReadToEnd());
        }
        if (!files.TryGetValue("index.html", out var html)) throw new InvalidOperationException("Isolation assets are missing. Configure app.security.pattern.options.dir and import NetWiv.Build targets, or set NetWivIsolationDist.");
        if (!files.Values.Any(static contents => contents.Contains("__TAURI_ISOLATION_HOOK__", StringComparison.Ordinal)))
            throw new InvalidOperationException("The isolation application must set window.__TAURI_ISOLATION_HOOK__ to a function or null.");
        using var document = HtmlAssets.Parse(html);
        HtmlAssets.InlineScripts(document, src => files.TryGetValue(src.TrimStart('/'), out var script) ? script :
            throw new InvalidOperationException($"Isolation script '{src}' is not embedded. Only paths relative to the isolation directory are supported."));
        HtmlAssets.PrependScript(document, ReadScript("secure").Replace("__RAW_process_ipc_message_fn__", ReadScript("process-ipc-message"), StringComparison.Ordinal), HtmlAssets.ScriptNonceToken);
        HtmlAssets.InjectNonceTokens(document, new HashSet<string>());
        _html = HtmlAssets.Serialize(document);
    }

    internal static string TransportScript(string scheme) => ReadScript("transport").Replace("__NETWIV_ISOLATION_SCHEME__", JsonValue.From(scheme).GetRawText(), StringComparison.Ordinal);

    private static string ReadScript(string name)
    {
        using var stream = typeof(IsolationPattern).Assembly.GetManifestResourceStream($"netwiv.isolation-runtime.{name}.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal bool IsIsolationUri(Uri uri) => uri.Scheme == Scheme ||
        uri.Scheme is "http" or "https" && uri.Host.Equals(Scheme + ".localhost", StringComparison.Ordinal);

    internal string OriginSource(Uri documentUri) => OperatingSystem.IsWindows()
        ? $"{(documentUri.Scheme == "https" ? "https" : "http")}://{Scheme}.localhost/" : $"{Scheme}://localhost/";

    internal AssetResponse Resolve(AssetRequest request, IReadOnlySet<string> disabledModifications, IReadOnlyDictionary<string, string> configuredHeaders)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Uri.UnescapeDataString(request.Uri.AbsolutePath.Trim('/'));
        // Preserve the pinned isolation protocol's request_to_path behavior.
        path = path.Length == 0 ? "index.html" : string.Concat(path.EnumerateRunes().Skip(1));
        if (path != "index.html") return new(404, "text/plain", ReadOnlyMemory<byte>.Empty);
        if (string.IsNullOrEmpty(request.WindowOrigin) || request.WindowOrigin == "null")
            return new(500, "text/plain", Encoding.UTF8.GetBytes("Isolation requires a native document origin."));
        var frameSource = OperatingSystem.IsWindows() ? $"{request.Uri.Scheme}://{Scheme}.localhost" : Scheme + ":";
        var response = ContentSecurityPolicy.Apply($"default-src 'none'; frame-src {frameSource}",
            new(200, "text/html", Encoding.UTF8.GetBytes(_html), configuredHeaders), disabledModifications, styleHashes: [HtmlAssets.Hash(IframeStyle)]);
        var rendered = Encoding.UTF8.GetString(response.Body.Span)
            .Replace("__TEMPLATE_origin__", JsonValue.From(request.WindowOrigin).GetRawText(), StringComparison.Ordinal)
            .Replace("__TEMPLATE_runtime_aes_gcm_key__", "[" + string.Join(',', _key) + "]", StringComparison.Ordinal);
        var body = Encoding.UTF8.GetBytes(rendered);
        var headers = new Dictionary<string, string>(response.Headers!, StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Length"] = body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return response with { Body = body, Headers = headers };
    }

    internal JsonElement Decrypt(JsonElement envelope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var nonce = ReadBytes(envelope.GetProperty("nonce"));
        if (nonce.Length != 12) throw new InvokeException("Nonce: could not convert slice to array");
        var encrypted = ReadBytes(envelope.GetProperty("payload"));
        if (encrypted.Length < 16) throw new InvokeException("AES-GCM");
        var plaintext = new byte[encrypted.Length - 16];
        try
        {
            using var cipher = new AesGcm(_key, 16);
            try { cipher.Decrypt(nonce, encrypted.AsSpan(0, plaintext.Length), encrypted.AsSpan(plaintext.Length), plaintext); }
            catch (CryptographicException) { throw new InvokeException("AES-GCM"); }
            if (envelope.GetProperty("contentType").GetString() == "application/octet-stream")
                return JsonValue.Write(writer => { writer.WriteStartArray(); foreach (var value in plaintext) writer.WriteNumberValue(value); writer.WriteEndArray(); });
            using var document = JsonDocument.Parse(plaintext);
            return document.RootElement.Clone();
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static byte[] ReadBytes(JsonElement value) => value.EnumerateArray().Select(static item => item.GetByte()).ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }
}
