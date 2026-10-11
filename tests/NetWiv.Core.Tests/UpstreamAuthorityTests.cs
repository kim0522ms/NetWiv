// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv;

internal static class UpstreamAuthorityTests
{
    internal static Task WindowGlobPatternMatches()
    {
        var authority = Create(new("main", ["allow-command"], Windows: ["main-*"]));
        Check(authority, "main-something", "other-*", "tauri://localhost", true);
        return Task.CompletedTask;
    }
    internal static Task WebviewGlobPatternMatches()
    {
        var authority = Create(new("main", ["allow-command"], Windows: ["other-*"], Webviews: ["main-*"]));
        Check(authority, "other-*", "main-something", "tauri://localhost", true);
        return Task.CompletedTask;
    }
    internal static Task RemoteDomainMatches()
    {
        var authority = Create(new("main", ["allow-command"], Windows: ["main"], Local: false, RemoteUrls: ["https://tauri.app"]));
        Check(authority, "main", "main", "https://tauri.app", true);
        return Task.CompletedTask;
    }
    internal static Task RemoteDomainGlobPatternMatches()
    {
        var authority = Create(new("main", ["allow-command"], Windows: ["main"], Local: false, RemoteUrls: ["http://tauri.*"]));
        Check(authority, "main", "main", "http://tauri.studio", true);
        return Task.CompletedTask;
    }
    internal static Task RemoteContextDenied()
    {
        Check(Create(new("main", ["allow-command"], Windows: ["main"])), "main", "main", "https://tauri.app", false);
        return Task.CompletedTask;
    }
    internal static Task DeniedCommandTakesPrecedence()
    {
        var authority = new AclAuthorizer([new("allow-command", "my-command"), new("deny-command", [], ["my-command"])],
            [new("main", ["allow-command", "deny-command"], Windows: ["main"])], Origins(), true);
        Check(authority, "main", "main", "tauri://localhost", false);
        return Task.CompletedTask;
    }
    internal static async Task ReleaseDenialMessage()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).AppCommandManifest("restricted")
            .RegisterCommand("restricted", (_, _, _) => ValueTask.FromResult(JsonValue.Null)).Build();
        app.Run();
        app.Handle.DebugAssertions = false;
        const string marker = "__NETWIV_INVOKE_KEY__: '";
        var key = runtime.Context!.InitializationScript.Split(marker, StringSplitOptions.None)[1][..64];
        runtime.Context!.OnMessage(new("main", new Uri("tauri://localhost"),
            "{\"cmd\":\"restricted\",\"callback\":1,\"error\":2,\"payload\":{},\"__NETWIV_INVOKE_KEY__\":\"" + key + "\"}"));
        Assert.Contains("Command restricted not allowed by ACL", await runtime.NextScript());
    }
    internal static Task ResolveAccessMessage()
    {
        const string first = "plugin:myplugin|my-command-window";
        const string second = "plugin:myplugin|my-command-webview-window";
        var window = new AclAuthorizer([new("allow-command", first)],
            [new("maincap", ["allow-command"], Windows: ["main-*"])], Origins(), true, ["myplugin"]);
        var both = new AclAuthorizer([new("allow-command", second)],
            [new("maincap", ["allow-command"], Windows: ["main-*"], Webviews: ["webview-*"], RemoteUrls: ["http://localhost:8080"])], Origins(), true, ["myplugin"]);
        Assert.Equal("unknown-plugin.my-command-window not allowed. Plugin not found",
            window.DebugMessage(new("plugin:unknown-plugin|my-command-window", "main-*", "webview-*", new("tauri://localhost"))));
        Assert.Equal("myplugin.unknown-command not allowed. Command not found",
            window.DebugMessage(new("plugin:myplugin|unknown-command", "main-*", "webview-*", new("tauri://localhost"))));
        Assert.Equal("myplugin.my-command-window not allowed on window \"other-window\", webview \"any-webview\", URL: local\n\nallowed on: [windows: \"main-*\", URL: local]\n\nreferenced by: capability: maincap, permission: allow-command",
            window.DebugMessage(new(first, "other-window", "any-webview", new("tauri://localhost"))));
        Assert.Equal("myplugin.my-command-window not allowed on window \"main-*\", webview \"any-webview\", URL: http://localhst/\n\nallowed on: [windows: \"main-*\", URL: local]\n\nreferenced by: capability: maincap, permission: allow-command",
            window.DebugMessage(new(first, "main-*", "any-webview", new("http://localhst"))));
        Assert.Equal("myplugin.my-command-webview-window not allowed on window \"other-window\", webview \"other-webview\", URL: local\n\nallowed on: [windows: \"main-*\", webviews: \"webview-*\", URL: local], [windows: \"main-*\", webviews: \"webview-*\", URL: http://localhost:8080]\n\nreferenced by: capability: maincap, permission: allow-command || capability: maincap, permission: allow-command",
            both.DebugMessage(new(second, "other-window", "other-webview", new("tauri://localhost"))));
        Assert.Equal("myplugin.my-command-webview-window not allowed on window \"main-*\", webview \"webview-*\", URL: http://localhost:123/\n\nallowed on: [windows: \"main-*\", webviews: \"webview-*\", URL: local], [windows: \"main-*\", webviews: \"webview-*\", URL: http://localhost:8080]\n\nreferenced by: capability: maincap, permission: allow-command || capability: maincap, permission: allow-command",
            both.DebugMessage(new(second, "main-*", "webview-*", new("http://localhost:123"))));
        return Task.CompletedTask;
    }
    internal static Task DiagnosticBoundaries()
    {
        const string command = "plugin:example|read";
        var definitions = new PermissionDefinition[] { new("example:allow-read", command), new("example:deny-read", [], [command]),
            new("example:default", [], []) { References = ["example:allow-read"] } };
        var empty = new AclAuthorizer(definitions, [], Origins(), false);
        Assert.Equal("example.read not allowed. Permissions associated with this command: example:allow-read, example:default",
            empty.DebugMessage(new(command, "main", "main", new("tauri://localhost"))));
        var allowed = new AclAuthorizer(definitions, [new("main", ["example:allow-read"], Windows: ["main"])], Origins(), false);
        Assert.Equal("allowed", allowed.DebugMessage(new(command, "main", "main", new("tauri://localhost"))));
        var denied = new AclAuthorizer(definitions, [new("main", ["example:deny-read"], Windows: ["other"])], Origins(), false);
        Assert.Equal("example.read explicitly denied on origin remote: https://example.com/\n\nreferenced by: capability: main, permission: example:deny-read",
            denied.DebugMessage(new(command, "main", "main", new("https://example.com"))));
        var inactive = new AclAuthorizer(definitions, [new("inactive", ["example:deny-read"], Local: false),
            new("main", ["example:allow-read"], Windows: ["main"])], Origins(), false);
        Assert.Equal(true, inactive.Authorize(new(command, "main", "main", new("tauri://localhost"))).Allowed);
        return Task.CompletedTask;
    }
    private static HashSet<string> Origins() => new(StringComparer.Ordinal) { "tauri://localhost" };
    private static AclAuthorizer Create(Capability capability) => new([new("allow-command", "my-command")], [capability], Origins(), true);
    private static void Check(AclAuthorizer authority, string window, string webview, string source, bool allowed)
    {
        var result = authority.Authorize(new("my-command", window, webview, new(source)));
        Assert.Equal(allowed, result.Allowed);
        Assert.Equal(0, result.Scope.Allow.Count); Assert.Equal(0, result.Scope.Deny.Count);
    }
}
