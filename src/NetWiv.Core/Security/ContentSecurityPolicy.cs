using System.Security.Cryptography;
using System.Text;
using NetWiv.Runtime;

namespace NetWiv;

internal static class ContentSecurityPolicy
{
    public static AssetResponse Apply(string policy, AssetResponse response, IReadOnlySet<string> disabledModifications,
        IEnumerable<string>? globalScriptHashes = null, IEnumerable<string>? styleHashes = null)
    {
        if (!(response.IsHtmlAsset ?? response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)))
            return response;

        using var document = HtmlAssets.Parse(Encoding.UTF8.GetString(response.Body.Span));
        HtmlAssets.InjectNonceTokens(document, disabledModifications);
        var html = HtmlAssets.Serialize(document);
        var directives = Parse(policy);
        if (!disabledModifications.Contains("script-src"))
            ReplaceNonces(ref html, HtmlAssets.ScriptNonceToken, directives, "script-src", HtmlAssets.ScriptHashes(document).Concat(globalScriptHashes ?? []));
        if (!disabledModifications.Contains("style-src"))
            ReplaceNonces(ref html, HtmlAssets.StyleNonceToken, directives, "style-src", styleHashes ?? []);
        var csp = Serialize(directives);
        var bytes = Encoding.UTF8.GetBytes(html);
        var headers = response.Headers is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) :
            new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase);
        headers["Content-Security-Policy"] = csp;
        headers["Content-Length"] = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return response with { Body = bytes, Headers = headers };
    }

    internal static Dictionary<string, List<string>> Parse(string policy)
    {
        var directives = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var part in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sources = part.Split(' ');
            directives[sources[0]] = sources.Skip(1).ToList();
        }
        return directives;
    }

    internal static string Serialize(Dictionary<string, List<string>> directives) =>
        string.Join("; ", directives.Select(static pair => pair.Key + (pair.Value.Count == 0 ? "" : " " + string.Join(' ', pair.Value))));

    // Tauri creates a fresh numeric nonce for every token occurrence, then adds self,
    // the nonces and hashes to that directive without inheriting default-src.
    private static void ReplaceNonces(ref string html, string token, Dictionary<string, List<string>> directives,
        string directive, IEnumerable<string> hashes)
    {
        var sources = new List<string>();
        var output = new StringBuilder();
        var last = 0;
        while (html.IndexOf(token, last, StringComparison.Ordinal) is var index && index >= 0)
        {
            var nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))).ToString(System.Globalization.CultureInfo.InvariantCulture);
            output.Append(html, last, index - last).Append(nonce);
            sources.Add($"'nonce-{nonce}'");
            last = index + token.Length;
        }
        output.Append(html, last, html.Length - last);
        html = output.ToString();
        sources.AddRange(hashes);
        if (sources.Count == 0) return;
        if (!directives.TryGetValue(directive, out var values)) directives.Add(directive, values = []);
        if (!values.Contains("'self'", StringComparer.Ordinal)) values.Add("'self'");
        values.AddRange(sources);
    }
}
