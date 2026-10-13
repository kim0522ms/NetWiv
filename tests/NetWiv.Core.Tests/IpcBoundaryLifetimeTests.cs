using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetWiv;
using NetWiv.Hosting;
using NetWiv.Runtime;

internal static class IpcBoundaryLifetimeTests
{
    internal static async Task ReplacementWaitsForScopeAndRejectsLateResources()
    {
        foreach (var close in new[] { false, true })
        foreach (var postMessage in new[] { false, true })
        {
            var entered = Signal();
            var cancelled = Signal();
            var release = Signal();
            var disposalEntered = Signal();
            var disposalRelease = Signal();
            var resource = new Resource();
            var lateResource = new Resource();
            var probe = new ScopedProbe(disposalEntered, disposalRelease);
            var runtime = new FakeRuntime();
            var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Services.AddScoped(_ => probe);
            uint rid = 0;
            builder.Desktop.UseRuntime(runtime).Window("main", "Main", "index.html").Window("secondary", "Secondary", "index.html")
                .RegisterCommand("held", async (context, _, token) =>
                {
                    context.Services.GetRequiredService<ScopedProbe>();
                    rid = context.AddResource(resource);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.SetResult();
                    // Cancellation must not dispose a scope still used by a noncooperative handler.
                    await release.Task;
                    Assert.True(!probe.Disposed, "Cancellation disposed a service still used by the handler.");
                    Assert.Throws<InvokeException>(() => context.AddResource(lateResource));
                    return JsonValue.From("old document");
                })
                .RegisterCommand("fresh", (context, _, _) => ValueTask.FromResult(JsonValue.From(context.AddResource(new Resource()))));
            await using var app = builder.Build();
            runtime.Run(app.Handle.CreateRuntimeContext());
            runtime.Context!.OnDocumentChanged!(new("main", 1, false));
            Task<AssetResponse>? pending = null;
            if (postMessage) Send(runtime, "held", "{}", "main", "main", 1);
            else pending = GuestIpc.Invoke(runtime, "held", "{}", 1);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                runtime.Context.OnDocumentChanged(new("main", close ? 1UL : 2UL, close));
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(1, resource.Disposals);
                Assert.True(app.Handle.Resources.TryGet(rid) is null, "Replaced document retained its resource.");
                Assert.True(!probe.Disposed && !disposalEntered.Task.IsCompleted, "Scope disposal ran before the handler returned.");
                var fresh = await GuestIpc.Invoke(runtime, "fresh", "{}", close ? 0UL : 2UL, close ? "secondary" : "main");
                Assert.Equal("ok", fresh.Headers!["Tauri-Response"]);
                var freshId = JsonValue.Parse(GuestIpc.Body(fresh)).GetUInt32();
                release.SetResult();
                await disposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(1, lateResource.Disposals);
                var drained = app.Handle.WaitForInvocationsAsync(CancellationToken.None);
                Assert.True(!drained.IsCompleted && (pending is null || !pending.IsCompleted), "Invocation completed before async scope disposal.");
                disposalRelease.SetResult();
                Assert.True(await drained.WaitAsync(TimeSpan.FromSeconds(3)), "Invocation did not drain after scope disposal.");
                if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(probe.Disposed, "Invocation did not dispose its scope.");
                Assert.True(app.Handle.Resources.TryGet(freshId) is not null, "Old scope cleanup removed a new document's resource.");
                Assert.Equal(0, runtime.PendingScripts);
            }
            finally { release.TrySetResult(); disposalRelease.TrySetResult(); }
        }
    }

    internal static async Task ReparentPreservesInFlightDocumentAndRechecksAcl()
    {
        foreach (var postMessage in new[] { false, true })
        {
            var entered = Signal();
            var cancelled = Signal();
            var release = Signal();
            var runtime = new FakeRuntime();
            var resource = new Resource();
            Channel? channel = null;
            uint rid = 0;
            using var app = new Builder().UseRuntime(runtime)
                .Window(new WindowOptions("origin", "Origin", "", 100, 100, default,
                    [new WebviewOptions("moving", "index.html", 0, 0, 100, 100, default)]))
                .Window(new WindowOptions("destination", "Destination", "", 100, 100, default, []))
                .AppCommandManifest("held")
                .Capability(new("origin-only", ["allow-held"], Windows: ["origin"]))
                .RegisterCommand("held", async (context, _, token) =>
                {
                    Assert.Equal("origin", context.WindowLabel);
                    channel = Channel.FromJson(context, JsonValue.From("__CHANNEL__:77"));
                    rid = context.AddResource(resource);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.SetResult();
                    await release.Task;
                    token.ThrowIfCancellationRequested();
                    return JsonValue.From("finished");
                }).Build();
            app.Run();
            runtime.Context!.OnDocumentChanged!(new("moving", 7, false));
            Task<AssetResponse>? pending = null;
            if (postMessage) Send(runtime, "held", "{}", "moving", "origin", 7);
            else pending = Invoke(runtime, "held", "moving", "origin", 7);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                runtime.Context.OnEvent(new("netwiv://webview-reparented", "moving", JsonValue.Parse("""{"windowLabel":"destination"}""")));
                runtime.Context.OnEvent(new("tauri://destroyed", "origin", JsonValue.Null, new("Window", "origin")));
                Assert.Equal("destination", app.Handle.GetWebview("moving")!.Window.Label);
                Assert.True(!cancelled.Task.IsCompleted && resource.Disposals == 0, "Reparent or old parent destruction ended the surviving document.");
                channel!.Send(JsonValue.From("after move"));
                Assert.Contains("runCallback(77,{\"message\":\"after move\",\"index\":0})", await runtime.NextScript());
                Assert.Equal(7UL, runtime.LastScriptDocument);
                var denied = await Invoke(runtime, "held", "moving", "destination", 7);
                Assert.Equal("error", denied.Headers!["Tauri-Response"]);
                Assert.Contains("not allowed", GuestIpc.Body(denied));
                runtime.Context.OnDocumentChanged(new("moving", 7, true));
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(1, resource.Disposals);
                Assert.True(app.Handle.Resources.TryGet(rid) is null, "Closing the moved document retained its resource.");
                channel.Send(JsonValue.From("after close")); channel.Dispose();
                release.SetResult();
                Assert.True(await app.Handle.WaitForInvocationsAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)), "Closed document command did not drain.");
                if (pending is not null) Assert.Equal("error", (await pending.WaitAsync(TimeSpan.FromSeconds(3))).Headers!["Tauri-Response"]);
                Assert.Equal(0, runtime.PendingScripts);
            }
            finally { release.TrySetResult(); channel?.Dispose(); }
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Key(FakeRuntime runtime)
    {
        const string marker = "__NETWIV_INVOKE_KEY__: '";
        var script = runtime.Context!.InitializationScript;
        return script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
    }

    private static void Send(FakeRuntime runtime, string command, string payload, string webview, string window, ulong document) =>
        runtime.Context!.OnMessage(new(webview, new Uri("tauri://localhost"),
            $"{{\"cmd\":\"{command}\",\"payload\":{payload},\"callback\":1,\"error\":2,\"__NETWIV_INVOKE_KEY__\":\"{Key(runtime)}\"}}", window, document));

    private static Task<AssetResponse> Invoke(FakeRuntime runtime, string command, string webview, string window, ulong document) =>
        runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/" + command), "POST",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["Tauri-Invoke-Key"] = Key(runtime), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
                ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
            }, "{}"u8.ToArray(), WebviewLabel: webview, WindowLabel: window, DocumentId: document)).AsTask();

    private sealed class Resource : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }

    private sealed class ScopedProbe(TaskCompletionSource entered, TaskCompletionSource release) : IAsyncDisposable
    {
        internal bool Disposed;
        public async ValueTask DisposeAsync() { entered.SetResult(); await release.Task; Disposed = true; }
    }
}
