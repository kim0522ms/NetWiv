using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class IpcBoundaryMatrixTests
{
    internal static async Task IsolationRequiresEnvelope()
    {
        var runtime = new FakeRuntime();
        var calls = 0;
        using var app = Builder(runtime, true).RegisterCommand("probe", (_, _, _) =>
        {
            calls++;
            return ValueTask.FromResult(JsonValue.Null);
        }).Build();
        app.Run();
        var headers = Headers(runtime, new Uri("tauri://localhost"));
        foreach (var bytes in new[] { Array.Empty<byte>(), "{}"u8.ToArray() })
        {
            var response = await runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/probe"), "POST", headers, bytes,
                WebviewLabel: "main", WindowLabel: "main"));
            Assert.Equal(500, response.StatusCode);
            Assert.Equal(0, calls);
        }
        var encrypted = IsolationTests.Encrypt(IsolationTests.Key(app.Handle.Isolation!), "{}"u8.ToArray(), false);
        var allowed = await runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/probe"), "POST", headers,
            Encoding.UTF8.GetBytes(encrypted.GetRawText()), WebviewLabel: "main", WindowLabel: "main"));
        Assert.Equal("ok", allowed.Headers!["Tauri-Response"]);
        Assert.Equal(1, calls);
        var standard = new FakeRuntime();
        using var standardApp = Builder(standard, false).RegisterCommand("probe", (_, args, _) => ValueTask.FromResult(args)).Build();
        standardApp.Run();
        var empty = await standard.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/probe"), "POST",
            Headers(standard, new Uri("tauri://localhost")), WebviewLabel: "main", WindowLabel: "main"));
        Assert.Equal("ok", empty.Headers!["Tauri-Response"]);
        Assert.Equal("{}", GuestIpc.Body(empty));
    }

    internal static async Task OriginAndLifetime()
    {
        foreach (var isolation in new[] { false, true })
        foreach (var fallback in new[] { false, true })
        foreach (var close in new[] { false, true })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resource = new TrackedResource();
            uint resourceId = 0;
            var calls = 0;
            var runtime = new FakeRuntime();
            using var app = Builder(runtime, isolation)
                .RegisterCommand("probe", (context, _, _) =>
                {
                    calls++;
                    Assert.Equal(new Uri("tauri://localhost"), context.Source);
                    Assert.Equal(false, context.IsTrusted);
                    return ValueTask.FromResult(JsonValue.From("control"));
                })
                .RegisterCommand("pending", async (context, _, token) =>
                {
                    resourceId = context.AddResource(resource);
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    entered.SetResult();
                    // Complete even after cancellation, to exercise late callback suppression.
                    await release.Task;
                    Assert.Throws<InvokeException>(() => context.AddResource(new TrackedResource()));
                    return JsonValue.From("old-document");
                }).Build();
            app.Run();
            runtime.Context!.OnDocumentChanged!(new("main", 1, false));
            var control = await Invoke(runtime, app, isolation, fallback, "probe", 1);
            Assert.Equal("ok", control.Headers!["Tauri-Response"]);
            Assert.Equal(1, calls);
            // The fallback's user-supplied origin header and payload cannot attest origin.
            var denied = await Invoke(runtime, app, isolation, fallback, "probe", 1, new Uri("https://untrusted.example"));
            Assert.Equal("error", denied.Headers!["Tauri-Response"]);
            Assert.Equal(1, calls);
            var pending = Invoke(runtime, app, isolation, fallback, "pending", 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(app.Handle.Resources.TryGet(resourceId) is not null, "Positive control did not register a document resource.");
            runtime.Context.OnDocumentChanged(new("main", close ? 1UL : 2UL, close));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, resource.DisposeCount);
            Assert.True(app.Handle.Resources.TryGet(resourceId) is null, "Document replacement retained its resource.");
            release.SetResult();
            if (!fallback) Assert.Equal("ok", (await pending.WaitAsync(TimeSpan.FromSeconds(2))).Headers!["Tauri-Response"]);
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                Assert.True(await app.Handle.WaitForInvocationsAsync(timeout.Token), "Late command did not finish.");
                Assert.Equal(0, runtime.PendingScripts);
            }
            if (!close)
            {
                Assert.Equal("ok", (await Invoke(runtime, app, isolation, fallback, "probe", 2)).Headers!["Tauri-Response"]);
                Assert.Equal(2, calls);
            }
        }
    }

    private static Builder Builder(FakeRuntime runtime, bool isolation)
    {
        var builder = new Builder().UseRuntime(runtime);
        if (isolation) builder.UseEmbeddedConfig(assembly: typeof(IsolationTests).Assembly, resourceName: "isolation-config.json");
        return builder.Window("main", "IPC boundary", "tauri://localhost");
    }

    private static Dictionary<string, string> Headers(FakeRuntime runtime, Uri source) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tauri-Invoke-Key"] = IsolationTests.InvokeKey(runtime), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
        ["Origin"] = source.GetLeftPart(UriPartial.Authority), ["Content-Type"] = "application/json"
    };

    private static async Task<AssetResponse> Invoke(FakeRuntime runtime, App app, bool isolation, bool fallback, string command, ulong document, Uri? source = null)
    {
        source ??= new Uri("tauri://localhost");
        var payload = JsonValue.Parse("""{"origin":"tauri://localhost","source":"tauri://localhost"}""");
        if (isolation) payload = IsolationTests.Encrypt(IsolationTests.Key(app.Handle.Isolation!), Encoding.UTF8.GetBytes(payload.GetRawText()), false);
        if (!fallback)
        {
            var response = await runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/" + command), "POST", Headers(runtime, source),
                Encoding.UTF8.GetBytes(payload.GetRawText()), WebviewLabel: "main", WindowLabel: "main", DocumentId: document));
            Assert.True(response.Headers!.ContainsKey("Tauri-Response"), GuestIpc.Body(response));
            return response;
        }
        runtime.Context!.OnMessage(new("main", source, $$$$"""{"cmd":"{{{{command}}}}","callback":1,"error":2,"__NETWIV_INVOKE_KEY__":"{{{{IsolationTests.InvokeKey(runtime)}}}}","payload":{{{{payload.GetRawText()}}}},"options":{"customProtocolIpcBlocked":true,"headers":{"origin":"tauri://localhost"}}}""", "main", document));
        if (command == "pending") return new(200, "application/json", ReadOnlyMemory<byte>.Empty);
        var script = await runtime.NextScript();
        return new(200, "application/json", ReadOnlyMemory<byte>.Empty,
            new Dictionary<string, string> { ["Tauri-Response"] = script.Contains("runCallback(1,", StringComparison.Ordinal) ? "ok" : "error" });
    }
}
