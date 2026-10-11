using System.Reflection;
using NetWiv.Runtime;

namespace NetWiv;

internal sealed class EmbeddedAssets
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _resources;
    private readonly Lazy<IReadOnlyList<string>> _scriptHashes;

    public EmbeddedAssets(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        _assembly = assembly;
        _resources = new(StringComparer.Ordinal);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal)) continue;
            var path = resourceName[resourcePrefix.Length..].Replace('\\', '/');
            _resources[path] = resourceName;
        }
        _scriptHashes = new(() => _resources.Where(static pair => Path.GetExtension(pair.Key) is ".js" or ".mjs")
            .Select(pair => HtmlAssets.Hash(System.Text.Encoding.UTF8.GetString(ReadBytes(pair.Value)))).ToArray());
    }

    internal IReadOnlyList<string> ScriptHashes => _scriptHashes.Value;

    private byte[] ReadBytes(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded asset '{resourceName}' is missing.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public AssetResponse? Resolve(AssetRequest request)
    {
        var origin = request.Uri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(origin, "tauri://localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(origin, "http://tauri.localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(origin, "https://tauri.localhost", StringComparison.OrdinalIgnoreCase)) return null;
        var path = request.Uri.AbsolutePath;
        if (path.EndsWith('/')) path = path[..^1];
        path = Uri.UnescapeDataString(path).Replace('\\', '/');
        if (path.StartsWith('/')) path = path[1..];
        if (path.Length == 0) path = "index.html";
        if (!_resources.TryGetValue(path, out var resourceName) &&
            !_resources.TryGetValue(path + ".html", out resourceName) &&
            !_resources.TryGetValue(path + "/index.html", out resourceName) &&
            !_resources.TryGetValue("index.html", out resourceName))
            return new(500, "text/plain", System.Text.Encoding.UTF8.GetBytes($"asset not found: {path}"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Access-Control-Allow-Origin"] = request.WindowOrigin ?? "null" });

        var bytes = ReadBytes(resourceName);
        return new(200, FileAssetMimeType.Parse(bytes, path), bytes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Length"] = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Access-Control-Allow-Origin"] = request.WindowOrigin ?? "null"
            }) { IsHtmlAsset = resourceName.EndsWith(".html", StringComparison.Ordinal) };
    }
}
