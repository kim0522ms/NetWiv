using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot;

internal sealed class AotStaticFiles : IDisposable
{
    private readonly IFileProvider _files;
    private readonly ManifestEmbeddedFileProvider _framework = new(typeof(WebViewManager).Assembly);
    private readonly IDisposable? _owned;
    private readonly string _hostPage;
    private readonly Uri _baseUri;

    internal AotStaticFiles(BlazorWebViewOptions options)
    {
        _baseUri = options.AppBaseUri;
        if (options.FileProvider is { } provider)
        {
            _files = provider;
            _hostPage = options.HostPage.Replace('\\', '/');
        }
        else
        {
            var path = Path.GetFullPath(options.HostPage, AppContext.BaseDirectory);
            _files = new PhysicalFileProvider(Path.GetDirectoryName(path)!);
            _owned = (IDisposable)_files;
            _hostPage = Path.GetFileName(path);
        }
        if (!_files.GetFileInfo(_hostPage).Exists)
        {
            _owned?.Dispose();
            throw new FileNotFoundException("The Blazor Hybrid host page was not found.");
        }
    }

    internal AssetResponse? TryGetResponse(AssetRequest request)
    {
        var uri = request.Uri;
        if (!BlazorAppOrigin.Matches(uri, _baseUri)) return null;
        if (request.Method is not ("GET" or "HEAD")) return Response(405, "Method not allowed");
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (path.Contains('\\') || path.Contains('\0') || path.Split('/').Any(segment => segment is "." or ".."))
            return Response(400, "Invalid asset path");

        // Keep the transport script paired with the pinned C# adapter.
        var isFramework = path.StartsWith("_framework/", StringComparison.Ordinal);
        var file = isFramework ? _framework.GetFileInfo(path) : _files.GetFileInfo(path.Length == 0 ? _hostPage : path);
        if (!file.Exists && !isFramework && (!Path.HasExtension(path) || request.Headers.Any(header =>
                header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) &&
                header.Value.Contains("text/html", StringComparison.OrdinalIgnoreCase))))
            file = _files.GetFileInfo(_hostPage);
        if (!file.Exists || file.IsDirectory) return Response(404, "Not found");

        using var content = file.CreateReadStream();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return new(200, ContentType(file.Name), buffer.ToArray(),
            new Dictionary<string, string> { ["Cache-Control"] = "no-cache" });
    }

    private static AssetResponse Response(int status, string text) =>
        new(status, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(text));

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".wasm" => "application/wasm",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };

    public void Dispose() => _owned?.Dispose();
}
