using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class EmbeddedAssetTests
{
    public static Task RoutingAndProtocol()
    {
        var assets = new EmbeddedAssets(typeof(EmbeddedAssetTests).Assembly, "netwiv.routing.");
        foreach (var origin in new[] { "tauri://localhost", "http://tauri.localhost", "https://tauri.localhost" })
        {
            foreach (var (path, expected) in new[] {
                ("/route", "exact"), ("/route/", "exact"), ("/route.html", "html"),
                ("/route/index.html", "directory"), ("/page", "page"),
                ("/directory", "directory-only"), ("/%70age?ignored=1#fragment", "page") })
            {
                foreach (var method in new[] { "GET", "HEAD", "POST", "OPTIONS" })
                {
                    var response = assets.Resolve(new(new Uri(origin + path), method, new Dictionary<string, string>(), "ignored"u8.ToArray(), "https://document.example"))!;
                    Equal(200, response.StatusCode);
                    Equal(expected, Encoding.UTF8.GetString(response.Body.Span).Trim());
                    Equal("https://document.example", response.Headers!["Access-Control-Allow-Origin"]);
                    Equal(response.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), response.Headers["Content-Length"]);
                }
            }
            var root = assets.Resolve(Request(origin + "/"))!;
            foreach (var path in new[] { "/missing", "/missing/nested", "/missing.js" })
                Equal(Encoding.UTF8.GetString(root.Body.Span), Encoding.UTF8.GetString(assets.Resolve(Request(origin + path))!.Body.Span));
            Equal("null", root.Headers!["Access-Control-Allow-Origin"]);
        }
        Equal<AssetResponse?>(null, assets.Resolve(Request("https://tauri.localhost.attacker.example/")));
        var empty = new EmbeddedAssets(typeof(EmbeddedAssetTests).Assembly, "netwiv.absent.");
        var missing = empty.Resolve(Request("tauri://localhost/missing%20page"))!;
        Equal(500, missing.StatusCode);
        Equal("text/plain", missing.ContentType);
        Equal("asset not found: missing page", Encoding.UTF8.GetString(missing.Body.Span));
        return Task.CompletedTask;
    }

    public static Task CspUsesResolvedAsset()
    {
        var assets = new EmbeddedAssets(typeof(EmbeddedAssetTests).Assembly, "netwiv.routing.");
        var fallback = assets.Resolve(Request("tauri://localhost/missing.svg"))!;
        Equal("image/svg+xml", fallback.ContentType);
        var secured = ContentSecurityPolicy.Apply("default-src 'none'", fallback, new HashSet<string>());
        if (!secured.Headers!["Content-Security-Policy"].Contains("'sha256-", StringComparison.Ordinal)) throw new Exception("Fallback HTML lost CSP script authorization.");
        var exact = assets.Resolve(Request("tauri://localhost/route"))!;
        Equal("text/html", exact.ContentType);
        var unchanged = ContentSecurityPolicy.Apply("default-src 'none'", exact, new HashSet<string>());
        Equal("exact", Encoding.UTF8.GetString(unchanged.Body.Span).Trim());
        if (unchanged.Headers!.ContainsKey("Content-Security-Policy")) throw new Exception("Non-HTML asset received HTML processing from its inferred MIME type.");
        return Task.CompletedTask;
    }

    private static AssetRequest Request(string uri) => new(new Uri(uri), "GET", new Dictionary<string, string>());
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
