// URL assertions ported from the pinned Tauri webview tests.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class UriSchemeTests
{
    internal static async Task RequestsAndOwnership()
    {
        var runtime = new FakeRuntime();
        var pending = new TaskCompletionSource<AssetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        UriSchemeContext? observed = null;
        AssetRequest? received = null;
        var builder = new Builder().UseRuntime(runtime)
            .RegisterUriSchemeProtocol("sync", (context, request) =>
            {
                observed = context; received = request;
                return new(201, "application/octet-stream", request.Body, new Dictionary<string, string> { ["X-Reply"] = "sync" });
            })
            .RegisterAsynchronousUriSchemeProtocol("later", (_, _) => pending.Task);
        using var app = builder.Build();
        builder.RegisterUriSchemeProtocol("sync", (_, _) => new(500, "text/plain", ReadOnlyMemory<byte>.Empty));
        app.Run();
        Assert.Equal("sync,later", string.Join(',', runtime.Context!.UriSchemeProtocols!));
        byte[] body = [0, 255, 23];
        var request = new AssetRequest(new("sync://localhost/data?q=1"), "POST", new Dictionary<string, string> { ["X-Request"] = "input" }, body,
            WebviewLabel: "child", WindowLabel: "parent", DocumentId: 9);
        var response = await runtime.Context.ResolveUriScheme!(request);
        Assert.Equal(201, response!.StatusCode);
        Assert.True(response.Body.Span.SequenceEqual(body), "Protocol response body was changed.");
        Assert.Equal("sync", response.Headers!["X-Reply"]);
        Assert.True(ReferenceEquals(app.Handle, observed!.AppHandle), "Protocol context lost the owning app.");
        Assert.Equal("child", observed.WebviewLabel);
        Assert.Equal(request, received);
        Assert.Equal<AssetResponse?>(null, await runtime.Context.ResolveUriScheme(request with { Uri = new("unregistered://localhost") }));
        var delayed = runtime.Context.ResolveUriScheme(request with { Uri = new("later://localhost") }).AsTask();
        Assert.Equal(false, delayed.IsCompleted);
        pending.SetResult(new(202, "text/plain", Encoding.UTF8.GetBytes("completed")));
        Assert.Equal(202, (await delayed)!.StatusCode);
        app.Dispose();
        try { await runtime.Context.ResolveUriScheme(request); throw new Exception("Disposed app accepted a protocol request."); }
        catch (ObjectDisposedException) { }
        Assert.Throws<ArgumentException>(() => builder.RegisterUriSchemeProtocol("not a scheme", (_, _) => response));
    }

    // Original URL assertions from pinned tauri/src/webview/mod.rs.
    internal static Task TauriProtocolIsLocal()
    {
        var authority = Authority();
        Assert.Equal(true, authority.IsLocal(new(OperatingSystem.IsWindows() ? "https://tauri.localhost/" : "tauri://localhost/")));
        return Task.CompletedTask;
    }
    internal static Task WindowsCustomProtocolRejectsSpoofedDomain()
    {
        var authority = Authority();
        Assert.Equal(true, authority.IsLocal(new("https://myproto.localhost/")));
        Assert.Equal(false, authority.IsLocal(new("https://myproto.evil.com/")));
        Assert.Equal(false, authority.IsLocal(new("https://notregistered.localhost/")));
        return Task.CompletedTask;
    }
    internal static Task OriginBoundaries()
    {
        var authority = Authority();
        var local = new Uri(OperatingSystem.IsWindows() ? "http://myproto.localhost:1234/" : "myproto://other-host/");
        Assert.Equal(true, authority.Authorize(new("custom", "main", "main", local)).Allowed);
        foreach (var source in new[] { "https://myproto.evil.com/", "https://myproto.localhost.evil.com/", "https://notregistered.localhost/" })
            Assert.Equal(false, authority.Authorize(new("custom", "main", "main", new(source))).Allowed);
        var guarded = new AclAuthorizer([], [], new HashSet<string>(), true, localProtocols: new HashSet<string> { "myproto" });
        Assert.Equal(false, guarded.Authorize(new("custom", "main", "main", local)).Allowed);
        return Task.CompletedTask;
    }
    private static AclAuthorizer Authority() => new([], [], new HashSet<string> { "tauri://localhost", "https://tauri.localhost" }, false,
        localProtocols: new HashSet<string> { "myproto" });
}
