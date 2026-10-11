using NetWiv;
using NetWiv.Plugin.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal static class InvocationLifetimeTests
{
    internal static async Task CancelOnReloadAndDispose()
    {
        foreach (var dispose in new[] { false, true })
        foreach (var postMessage in new[] { false, true })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).RegisterCommand("wait", async (_, _, token) =>
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.SetResult(); throw; }
                return JsonValue.Null;
            }).Build();
            app.Run(); runtime.Context!.OnDocumentChanged!(new("main", 1, false));
            Task<NetWiv.Runtime.AssetResponse>? pending = null;
            if (postMessage)
            {
                const string marker = "__NETWIV_INVOKE_KEY__: '";
                var script = runtime.Context.InitializationScript;
                var key = script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
                runtime.Context.OnMessage(new("main", new Uri("tauri://localhost"),
                    "{\"cmd\":\"wait\",\"payload\":{},\"callback\":1,\"error\":2,\"__NETWIV_INVOKE_KEY__\":\"" + key + "\"}", "main", 1));
            }
            else pending = GuestIpc.Invoke(runtime, "wait", "{}", 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (dispose) app.Dispose(); else runtime.Context.OnDocumentChanged(new("main", 2, false));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (pending is not null)
            {
                var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal("error", response.Headers!["Tauri-Response"]);
            }
            Assert.Equal(0, runtime.PendingScripts);
        }
    }

    internal static async Task HttpResourcesOnReload()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new HttpPlugin())
            .Capability(new("main", ["http:default"], Windows: ["main"], PermissionScopes:
                [new("http:default", [JsonValue.Parse("""{"url":"http://127.0.0.1:1/**"}""")], [])])).Build();
        app.Run(); runtime.Context!.OnDocumentChanged!(new("main", 1, false));
        var created = await GuestIpc.Invoke(runtime, "plugin:http|fetch", """{"clientConfig":{"url":"http://127.0.0.1:1/test"}}""", 1);
        Assert.Equal("ok", created.Headers!["Tauri-Response"]);
        var rid = JsonValue.Parse(GuestIpc.Body(created)).GetUInt32();
        Assert.True(app.Handle.Resources.TryGet(rid) is not null, "HTTP resource was not created.");
        runtime.Context.OnDocumentChanged(new("main", 2, false));
        Assert.True(app.Handle.Resources.TryGet(rid) is null, "Reload retained the old HTTP resource.");
    }

    internal static async Task HttpStreamingOnClose()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/body";
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            var stream = client.GetStream(); _ = await stream.ReadAsync(new byte[4096], stop.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\na"), stop.Token);
            Assert.Equal(0, await stream.ReadAsync(new byte[1], stop.Token));
        });
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new HttpPlugin())
                .Capability(new("main", ["http:default"], Windows: ["main"], PermissionScopes:
                    [new("http:default", [JsonValue.Parse("{\"url\":" + JsonValue.From(url).GetRawText() + "}")], [])])).Build();
            app.Run(); runtime.Context!.OnDocumentChanged!(new("main", 1, false));
            var created = await GuestIpc.Invoke(runtime, "plugin:http|fetch", "{\"clientConfig\":{\"url\":" + JsonValue.From(url).GetRawText() + "}}", 1);
            var rid = JsonValue.Parse(GuestIpc.Body(created)).GetUInt32();
            var sent = await GuestIpc.Invoke(runtime, "plugin:http|fetch_send", $"{{\"rid\":{rid}}}", 1);
            var bodyId = JsonValue.Parse(GuestIpc.Body(sent)).GetProperty("rid").GetUInt32();
            await GuestIpc.Invoke(runtime, "plugin:http|fetch_read_body", $"{{\"rid\":{bodyId}}}", 1);
            var pending = GuestIpc.Invoke(runtime, "plugin:http|fetch_read_body", $"{{\"rid\":{bodyId}}}", 1);
            runtime.Context.OnDocumentChanged(new("main", 1, true));
            Assert.Equal("error", (await pending.WaitAsync(TimeSpan.FromSeconds(3))).Headers!["Tauri-Response"]);
            Assert.True(app.Handle.Resources.TryGet(rid) is null && app.Handle.Resources.TryGet(bodyId) is null, "Closed document retained HTTP handles.");
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { stop.Cancel(); listener.Stop(); }
    }
}
