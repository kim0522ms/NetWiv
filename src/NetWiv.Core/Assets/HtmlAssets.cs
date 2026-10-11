using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace NetWiv;

// Behavior ported from tauri-utils/src/html.rs (pinned by upstream-lock.json).
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
internal static class HtmlAssets
{
    internal const string ScriptNonceToken = "__TAURI_SCRIPT_NONCE__";
    internal const string StyleNonceToken = "__TAURI_STYLE_NONCE__";

    internal static IHtmlDocument Parse(string html) => new HtmlParser(new HtmlParserOptions { IsScripting = true }).ParseDocument(html);
    internal static string Serialize(IHtmlDocument document) => document.ToHtml();
    internal static string NormalizeScript(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    internal static string Hash(string script) => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeScript(script))))}'";

    internal static void InjectNonceTokens(IHtmlDocument document, IReadOnlySet<string> disabled)
    {
        if (!disabled.Contains("script-src")) Inject("script[src^='http']", ScriptNonceToken);
        if (!disabled.Contains("style-src")) Inject("style", StyleNonceToken);
        void Inject(string selector, string token)
        {
            foreach (var element in document.QuerySelectorAll(selector))
                if (!element.HasAttribute("nonce")) element.SetAttribute("nonce", token);
        }
    }

    internal static IEnumerable<string> ScriptHashes(IHtmlDocument document) =>
        document.QuerySelectorAll("script:not(:empty)").Select(static script => Hash(script.TextContent));

    internal static void InjectCsp(IHtmlDocument document, string policy)
    {
        var meta = document.CreateElement("meta");
        meta.SetAttribute("http-equiv", "Content-Security-Policy");
        meta.SetAttribute("content", policy);
        document.Head!.AppendChild(meta);
    }

    internal static void PrependScript(IHtmlDocument document, string script, string? nonce = null)
    {
        var element = document.CreateElement("script");
        if (nonce is not null) element.SetAttribute("nonce", nonce);
        element.TextContent = script;
        document.Head!.InsertBefore(element, document.Head.FirstChild);
    }

    internal static void AppendScript(IHtmlDocument document, string script)
    {
        var element = document.CreateElement("script");
        element.TextContent = script;
        document.Head!.AppendChild(element);
    }

    internal static void InlineScripts(IHtmlDocument document, Func<string, string> readFile)
    {
        foreach (var script in document.QuerySelectorAll("script[src]"))
        {
            script.TextContent += readFile(script.GetAttribute("src")!);
            script.RemoveAttribute("src");
        }
    }
}
