// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv;

internal static class UpstreamPathAndUrlTests
{
    internal static async Task Basename()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime()).Build();
        foreach (var (path, extension, expected) in new[]
        {
            ("/path/to/some-json-file.json", ".json", "some-json-file"),
            ("/path/to/some-json-file.json", "json", "some-json-file."),
            ("/path/to/some-json-file.html.json", ".json", "some-json-file.html"),
            ("/path/to/some-json-file.json.json", ".json", "some-json-file.json"),
            ("/path/to/some-json-file.json.html", ".json", "some-json-file.json.html")
        }) Assert.Equal(expected, await PathApi.BaseNameAsync(app.Handle, path, extension));
    }

    internal static async Task Join()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime()).Build();
        (string[] Paths, string Expected)[] cases =
        [
            ([""], "."), (["", ""], "."), (["a"], "a"), (["", "a"], "a"),
            (["a", "b"], "a/b"), (["a", "", "b"], "a/b"), (["a", "/b", "c"], "a/b/c"),
            (["a", "b/c", "d"], "a/b/c/d"), (["a/", "b"], "a/b")
        ];
        foreach (var (paths, expected) in cases)
            Assert.Equal(OperatingSystem.IsWindows() ? expected.Replace('/', '\\') : expected,
                await PathApi.JoinAsync(app.Handle, paths));
    }

    internal static Task MimeTypeFromUri()
    {
        foreach (var (uri, expected) in new[]
        {
            ("https://unpkg.com/browse/bootstrap@4.1.0/dist/css/bootstrap-grid.css", "text/css"),
            ("https://example.com/random.csv", "text/csv"),
            ("https://icons.duckduckgo.com/ip3/microsoft.com.ico", "image/vnd.microsoft.icon"),
            ("https://tauri.app/index.html", "text/html"),
            ("https://unpkg.com/react@17.0.1/umd/react.production.min.js", "text/javascript"),
            ("https://unpkg.com/browse/react@17.0.1/build-info.json", "application/json"),
            ("https:/example.com/hello.jsonld", "application/ld+json"),
            ("https://example.com/bundled.mjs", "text/javascript"),
            ("https://example.com/video.mp4", "video/mp4"),
            ("https://example.com/document.rtf", "application/rtf"),
            ("https://example.com/picture.svg", "image/svg+xml"),
            ("https://example.com/file.txt", "text/plain"), ("wry://tauri.app", "text/html")
        }) Assert.Equal(expected, FileAssetMimeType.Parse([], uri));
        return Task.CompletedTask;
    }

    internal static Task UrlPatternDomainWildcard()
    {
        Check("http://*", true, "http://tauri.app/path", "http://tauri.app/path?q=1", "http://localhost/path", "http://localhost/path?q=1");
        Check("http://*.tauri.app", false, "http://tauri.app/path", "http://tauri.app/path?q=1", "http://localhost/path", "http://localhost/path?q=1");
        Check("http://*.tauri.app", true, "http://api.tauri.app/path", "http://api.tauri.app/path?q=1");
        return Task.CompletedTask;
    }
    internal static Task UrlPatternPathWildcard()
    {
        Check("http://localhost/*", true, "http://localhost/path", "http://localhost/path?q=1");
        return Task.CompletedTask;
    }
    internal static Task UrlPatternSchemeWildcard()
    {
        Check("*://localhost", true, "http://localhost/path", "https://localhost/path?q=1", "custom://localhost/path");
        return Task.CompletedTask;
    }
    private static void Check(string value, bool expected, params string[] urls)
    {
        var pattern = new RemoteUrlPattern(value);
        foreach (var url in urls) Assert.Equal(expected, pattern.Matches(new Uri(url)));
    }
}
