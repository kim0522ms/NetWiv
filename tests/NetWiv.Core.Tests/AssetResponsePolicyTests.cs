using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class AssetResponsePolicyTests
{
    internal static Task SharedPolicy()
    {
        var request = new AssetRequest(new Uri("http://netwiv-blazor.localhost/index.html"), "GET", new Dictionary<string, string>());
        var raw = new AssetResponse(200, "text/html", "<script src='https://example.invalid/app.js'></script>"u8.ToArray(),
            new Dictionary<string, string> { ["Cross-Origin-Opener-Policy"] = "unsafe-none", ["X-Preserved"] = "original" });
        var assembly = typeof(AssetResponsePolicyTests).Assembly;
        using var script = new StreamReader(assembly.GetManifestResourceStream("netwiv.isolation.hook.js")!);
        var globalHash = HtmlAssets.Hash(script.ReadToEnd());
        var runtime = new FakeRuntime();
        using (var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig().AppCommandManifest("scoped")
            .UseEmbeddedAssets(assembly, "netwiv.isolation.").Build())
        {
            app.Run();
            var policy = runtime.Context!.ApplyAssetPolicy ?? throw new InvalidOperationException("Configured asset policy was not exposed to the runtime.");
            var response = policy(request, raw);
            Assert.Equal("same-origin", response.Headers!["Cross-Origin-Opener-Policy"]);
            Assert.Equal("original", response.Headers["X-Preserved"]);
            Assert.Contains("default-src 'self'", response.Headers["Content-Security-Policy"]);
            Assert.Contains(globalHash, response.Headers["Content-Security-Policy"]);
            Assert.Contains("nonce=", Encoding.UTF8.GetString(response.Body.Span));
            Assert.Equal(response.Body.Length.ToString(), response.Headers["Content-Length"]);
            var resolved = app.Handle.ResolveAsset(request with { Uri = new("tauri://localhost/index.html") })!;
            Assert.Equal("same-origin", resolved.Headers!["Cross-Origin-Opener-Policy"]);
            Assert.Contains("default-src 'self'", resolved.Headers["Content-Security-Policy"]);

            var binary = policy(request, new(200, "application/octet-stream", new byte[] { 0, 1, 2 }));
            Assert.True(binary.Body.Span.SequenceEqual(new byte[] { 0, 1, 2 }), "Policy changed non-HTML response bytes.");
            Assert.Equal("same-origin", binary.Headers!["Cross-Origin-Opener-Policy"]);
            Assert.True(!binary.Headers.ContainsKey("Content-Security-Policy"), "HTML-only CSP rewriting changed a binary response.");
            Assert.Equal("unsafe-none", raw.Headers!["Cross-Origin-Opener-Policy"]);
        }

        var isolationRuntime = new FakeRuntime();
        using (var app = new Builder().UseRuntime(isolationRuntime).UseEmbeddedConfig(resourceName: "isolation-config.json").Build())
        {
            app.Run();
            var response = isolationRuntime.Context!.ApplyAssetPolicy!(request, raw);
            var origin = OperatingSystem.IsWindows()
                ? $"http://{isolationRuntime.Context.IsolationProtocol}.localhost/"
                : $"{isolationRuntime.Context.IsolationProtocol}://localhost/";
            Assert.Contains(origin, response.Headers!["Content-Security-Policy"]);
            Assert.Contains(HtmlAssets.Hash(IsolationPattern.IframeStyle), response.Headers["Content-Security-Policy"]);
            Assert.Equal("configured", response.Headers["X-Isolation-Test"]);
        }

        var plainRuntime = new FakeRuntime();
        using (var app = new Builder().UseRuntime(plainRuntime).Build())
        {
            app.Run();
            Assert.True(ReferenceEquals(raw, plainRuntime.Context!.ApplyAssetPolicy!(request, raw)), "An unconfigured policy changed the response.");
        }
        return Task.CompletedTask;
    }
}
