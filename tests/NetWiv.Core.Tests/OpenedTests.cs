using NetWiv;
using NetWiv.Runtime;

internal static class OpenedTests
{
    internal static async Task DeliveryAndLifetime()
    {
        var runtime = new FakeRuntime();
        var calls = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var app = new Builder().UseRuntime(runtime).ExitOnLastWindowClose(false)
            .OnOpened((_, value) => { calls.Add("builder:" + value.Urls.Count); })
            .OnOpened(async (_, _) => { await release.Task; completed.TrySetResult(); })
            .Build();
        app.OnOpened((_, value) => { calls.Add("app:" + value.Urls[0].Scheme); });
        app.Handle.OnOpened((_, value) => { calls.Add("handle:" + value.Urls[1].IsFile); });
        app.Run();
        Assert.True(!runtime.Context!.ExitOnLastWindowClose, "The resident policy must reach the runtime.");
        var payload = JsonValue.Parse("""{"urls":["netwiv-test://opened/path","file:///test/selected.txt"]}""");
        app.Handle.Emit("netwiv://opened", payload);
        Assert.Equal(0, calls.Count);
        runtime.Context.OnEvent(new("netwiv://opened", null, payload, RuntimeEventTarget.App));
        Assert.Equal("builder:2,app:netwiv-test,handle:True", string.Join(',', calls));
        Assert.True(!completed.Task.IsCompleted, "Native delivery must not wait for async application work.");
        release.SetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.Dispose();
        runtime.Context.OnEvent(new("netwiv://opened", null, payload, RuntimeEventTarget.App));
        Assert.Equal(3, calls.Count);
        Assert.Throws<ObjectDisposedException>(() => app.OnOpened((_, _) => { }));
        using var defaultApp = new Builder().UseRuntime(new FakeRuntime()).Build();
        Assert.True(defaultApp.Handle.CreateRuntimeContext().ExitOnLastWindowClose, "The default last-window exit policy must be preserved.");
    }

    internal static async Task FailuresAndDisposal()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        app.Handle.EventHandlerFailed += (value, error) =>
        {
            Assert.Equal("netwiv://opened", value.Name);
            lock (errors)
            {
                errors.Add(error.Message);
                if (errors.Count == 2) reported.TrySetResult();
            }
        };
        app.OnOpened((Action<AppHandle, OpenedEvent>)((_, _) => throw new InvalidOperationException("sync")));
        app.OnOpened(async (_, _) => { await Task.Yield(); throw new InvalidOperationException("async"); });
        var calls = 0;
        app.OnOpened((_, _) => { calls++; });
        app.Run();
        var payload = JsonValue.Parse("""{"urls":["netwiv-test://opened/path"]}""");
        runtime.Context!.OnEvent(new("netwiv://opened", null, payload, RuntimeEventTarget.App));
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.True(errors.Contains("sync") && errors.Contains("async"), "Both failures must be observed.");
        app.OnOpened((_, _) => app.Dispose());
        app.OnOpened((_, _) => { calls += 100; });
        runtime.Context.OnEvent(new("netwiv://opened", null, payload, RuntimeEventTarget.App));
        Assert.Equal(2, calls);
    }
}
