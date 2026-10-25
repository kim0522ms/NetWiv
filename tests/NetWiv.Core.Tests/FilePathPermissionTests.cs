using NetWiv;

internal static class FilePathPermissionTests
{
    public static async Task RequiresExplicitGrant()
    {
        foreach (var permissions in new[] { Array.Empty<string>(), new[] { "core:default" }, new[] { "core:webview:default" } })
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime)
                .Capability(new("default", permissions, Windows: ["main"]))
                .Build();
            app.Run();
            Send(runtime);
            Assert.Contains("not allowed", await runtime.NextScript());
            Assert.Equal<string?>(null, runtime.LastCommand);
        }

        var grantedRuntime = new FakeRuntime { InvokeResult = JsonValue.From("opaque-ticket") };
        using var granted = new Builder().UseRuntime(grantedRuntime)
            .Capability(new("file-path", ["core:webview:allow-get-file-path"], Webviews: ["main"]))
            .Build();
        granted.Run();
        grantedRuntime.Context!.OnDocumentChanged!(new("main", 7, false));
        Send(grantedRuntime, documentId: 7);
        Assert.Contains("runCallback(1,\"opaque-ticket\")", await grantedRuntime.NextScript());
        Assert.Equal("plugin:webview|get_file_path", grantedRuntime.LastCommand);
        Assert.Equal("main", grantedRuntime.LastContext!.WebviewLabel);
        Assert.Equal(new Uri("tauri://localhost"), grantedRuntime.LastContext.Source);
        Assert.Equal(7UL, grantedRuntime.LastContext.DocumentId);
        Assert.True(grantedRuntime.LastContext.IsAuthorized, "File tickets must use an authorized runtime context.");
    }

    public static async Task TargetOriginAndDenyBoundaries()
    {
        foreach (var source in new[] { "tauri://localhost", "https://untrusted.example" })
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime)
                .Capability(new("file-path", ["core:webview:allow-get-file-path"], Webviews: ["other"]))
                .Build();
            app.Run();
            Send(runtime, source);
            Assert.Contains("not allowed", await runtime.NextScript());
            Assert.Equal<string?>(null, runtime.LastCommand);
        }

        var remoteRuntime = new FakeRuntime();
        using var localOnly = new Builder().UseRuntime(remoteRuntime)
            .Capability(new("file-path", ["core:webview:allow-get-file-path"], Webviews: ["main"]))
            .Build();
        localOnly.Run();
        Send(remoteRuntime, "https://untrusted.example");
        Assert.Contains("not allowed", await remoteRuntime.NextScript());
        Assert.Equal<string?>(null, remoteRuntime.LastCommand);

        var deniedRuntime = new FakeRuntime();
        using var denied = new Builder().UseRuntime(deniedRuntime)
            .Capability(new("allow", ["core:webview:allow-get-file-path"], Webviews: ["main"]))
            .Capability(new("deny", ["core:webview:deny-get-file-path"], Webviews: ["main"]))
            .Build();
        denied.Run();
        Send(deniedRuntime);
        Assert.Contains("not allowed", await deniedRuntime.NextScript());
        Assert.Equal<string?>(null, deniedRuntime.LastCommand);
    }

    private static void Send(FakeRuntime runtime, string source = "tauri://localhost", ulong documentId = 0) =>
        runtime.Context!.OnMessage(new("main", new Uri(source),
            $$$$"""{"cmd":"plugin:webview|get_file_path","callback":1,"error":2,"__NETWIV_INVOKE_KEY__":"{{{{IsolationTests.InvokeKey(runtime)}}}}","payload":{"nonce":"test-nonce","generation":0}}""",
            "main", documentId));
}
