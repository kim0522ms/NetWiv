using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Desktop;

internal static class SampleAssets
{
    public static AssetResponse? Resolve(string root, AssetRequest request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return null;
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var relativePath = Uri.UnescapeDataString(request.Uri.AbsolutePath).TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPath, comparison) || !File.Exists(candidate)) return null;
        var contentType = Path.GetExtension(candidate).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
        return new AssetResponse(200, contentType, File.ReadAllBytes(candidate));
    }
}
