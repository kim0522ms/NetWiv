// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0
// SPDX-License-Identifier: MIT
// Assertion ports from tauri-utils/src/html.rs and html2.rs at upstream-lock.json.
using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class HtmlAssetTests
{
    private static readonly HashSet<string> Enabled = [];

    public static Task Csp()
    {
        foreach (var html in new[] { "<html><head></head></html>", "<html></html>" })
        {
            using var document = HtmlAssets.Parse(html);
            HtmlAssets.InjectCsp(document, "csp-string");
            Equal("<html><head><meta http-equiv=\"Content-Security-Policy\" content=\"csp-string\"></head><body></body></html>", HtmlAssets.Serialize(document));
        }
        return Task.CompletedTask;
    }

    public static Task NormalizeScriptForCsp()
    {
        const string script = "// Copyright 2019-2024 Tauri Programme within The Commons Conservancy\r// SPDX-License-Identifier: Apache-2.0\n// SPDX-License-Identifier: MIT\r\n\r\nwindow.__TAURI_ISOLATION_HOOK__ = (payload, options) => {\r\n  return payload\r\n}\r\n";
        const string expected = "// Copyright 2019-2024 Tauri Programme within The Commons Conservancy\n// SPDX-License-Identifier: Apache-2.0\n// SPDX-License-Identifier: MIT\n\nwindow.__TAURI_ISOLATION_HOOK__ = (payload, options) => {\n  return payload\n}\n";
        Equal(expected, HtmlAssets.NormalizeScript(script));
        Equal(HtmlAssets.Hash(expected), HtmlAssets.Hash(script));
        return Task.CompletedTask;
    }

    public static Task ParseAndSerializeRoundtrips()
    {
        foreach (var html in new[] {
            "<html><head><title>Test</title></head><body><h1>Hello</h1></body></html>",
            "<!DOCTYPE html><html><head></head><body></body></html>" })
        {
            using var document = HtmlAssets.Parse(html);
            Equal(html, HtmlAssets.Serialize(document));
        }
        return Task.CompletedTask;
    }

    public static Task InjectNonceToScripts()
    {
        using var document = HtmlAssets.Parse("<html><head><script src=\"http://example.com/script.js\"></script></head><body></body></html>");
        HtmlAssets.InjectNonceTokens(document, Enabled);
        Equal("<html><head><script src=\"http://example.com/script.js\" nonce=\"__TAURI_SCRIPT_NONCE__\"></script></head><body></body></html>", HtmlAssets.Serialize(document));
        return Task.CompletedTask;
    }

    public static Task InjectNonceToStyles()
    {
        using var document = HtmlAssets.Parse("<html><head><style>body { color: red; }</style></head><body></body></html>");
        HtmlAssets.InjectNonceTokens(document, Enabled);
        Equal("<html><head><style nonce=\"__TAURI_STYLE_NONCE__\">body { color: red; }</style></head><body></body></html>", HtmlAssets.Serialize(document));
        return Task.CompletedTask;
    }

    public static Task InjectNonceSkipsExisting()
    {
        const string html = "<html><head><script src=\"http://example.com/script.js\" nonce=\"existing\"></script></head><body></body></html>";
        using var document = HtmlAssets.Parse(html);
        HtmlAssets.InjectNonceTokens(document, Enabled);
        Equal(html, HtmlAssets.Serialize(document));
        return Task.CompletedTask;
    }

    public static Task InjectNonceRespectsDisabledModification()
    {
        const string html = "<html><head><script src=\"http://example.com/script.js\"></script></head><body></body></html>";
        using var document = HtmlAssets.Parse(html);
        HtmlAssets.InjectNonceTokens(document, new HashSet<string> { "script-src", "style-src" });
        Equal(html, HtmlAssets.Serialize(document));
        return Task.CompletedTask;
    }

    public static Task InlineIsolationReplacesSrcWithContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netwiv-inline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "app.js"), "console.log('test');");
            using var document = HtmlAssets.Parse("<html><head><script src=\"/app.js\"></script></head><body></body></html>");
            HtmlAssets.InlineScripts(document, src => File.ReadAllText(Path.Combine(directory, src.TrimStart('/'))));
            Equal("<html><head><script>console.log('test');</script></head><body></body></html>", HtmlAssets.Serialize(document));
        }
        finally { Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }

    public static Task AppendScriptToHead()
    {
        using var document = HtmlAssets.Parse("<html><head></head><body></body></html>");
        HtmlAssets.AppendScript(document, "console.log('Test')");
        Equal("<html><head><script>console.log('Test')</script></head><body></body></html>", HtmlAssets.Serialize(document));
        return Task.CompletedTask;
    }

    public static Task CspUsesBrowserParsingAndNonceSources()
    {
        const string html = "<!--<script>fake()</script>--><script title='a>b'>line1\r\nline2\rline3</script><script src='/local.js'></script><script src='https://example.com/app.js'></script><style>body{color:red}</style><style nonce='kept'>body{color:blue}</style>";
        var response = ContentSecurityPolicy.Apply("default-src 'none'", new AssetResponse(200, "text/html", Encoding.UTF8.GetBytes(html)), Enabled, [HtmlAssets.Hash("global()")]);
        var policy = ContentSecurityPolicy.Parse(response.Headers!["Content-Security-Policy"]);
        Equal("'none'", policy["default-src"].Single());
        using var document = HtmlAssets.Parse(Encoding.UTF8.GetString(response.Body.Span));
        var scriptNonce = document.QuerySelector("script[src^=https]")!.GetAttribute("nonce")!;
        var styleNonce = document.QuerySelector("style")!.GetAttribute("nonce")!;
        Assert.True(ulong.TryParse(scriptNonce, out _) && ulong.TryParse(styleNonce, out _) && scriptNonce != styleNonce, "Nonce tokens were not independently randomized.");
        Assert.True(policy["script-src"].SequenceEqual(new[] { "'self'", $"'nonce-{scriptNonce}'", HtmlAssets.Hash("line1\nline2\nline3"), HtmlAssets.Hash("global()") }), "CSP script allowances differ from pinned Tauri.");
        Assert.True(policy["style-src"].SequenceEqual(new[] { "'self'", $"'nonce-{styleNonce}'" }), "CSP style allowances differ from pinned Tauri.");
        Assert.True(!document.QuerySelector("script[src='/local.js']")!.HasAttribute("nonce"), "Local script was assigned a remote script nonce.");
        Equal("kept", document.QuerySelectorAll("style")[1].GetAttribute("nonce")!);
        Equal(response.Body.Length.ToString(), response.Headers["Content-Length"]);
        var disabled = ContentSecurityPolicy.Apply("default-src 'none'", new(200, "text/html", Encoding.UTF8.GetBytes(html)), new HashSet<string> { "script-src", "style-src" });
        Equal("default-src 'none'", disabled.Headers!["Content-Security-Policy"]);
        Assert.True(!Encoding.UTF8.GetString(disabled.Body.Span).Contains("__TAURI_", StringComparison.Ordinal), "Disabled nonce injection modified HTML.");
        return Task.CompletedTask;
    }

    private static void Equal(string expected, string actual) => Assert.True(expected == actual, $"Expected {expected}, got {actual}.");
}
