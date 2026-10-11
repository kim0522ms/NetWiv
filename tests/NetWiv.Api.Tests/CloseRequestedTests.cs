using System.Text.Json;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class CloseRequestedTests
{
    [Fact]
    public async Task CloseWaitsForHandlerAndUnlistenDoesNotCancelAlreadyRunningHandler()
    {
        var runtime = new CloseRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = app.Handle.GetWindow("main")!.OnCloseRequested(async request =>
        {
            Assert.Equal("main", request.Label);
            Assert.Equal("tauri://close-requested", request.Name);
            started.SetResult();
            await release.Task;
        });
        app.Handle.Emit("tauri://close-requested", JsonValue.Null, new RuntimeEventTarget("Window", "main"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(runtime.Destroyed.Task.IsCompleted);
        subscription.Dispose();
        release.SetResult();
        await runtime.Destroyed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, runtime.DestroyCount);
        app.Handle.Emit("tauri://close-requested", JsonValue.Null, new RuntimeEventTarget("Window", "main"));
        Assert.Equal(1, runtime.DestroyCount);
    }

    [Fact]
    public void PreventDefaultKeepsWindowAndDisposeRemovesCloseInterception()
    {
        var runtime = new CloseRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        app.Run();
        using (app.Handle.GetWebviewWindow("main")!.OnCloseRequested(request =>
        {
            request.PreventDefault();
            Assert.True(request.IsPreventDefault());
        }))
        {
            Assert.True(runtime.Context!.HasEventListeners!("tauri://close-requested", "main"));
            runtime.Context.OnEvent(new("tauri://close-requested", "main", JsonValue.Null, new("Window", "main")));
            Assert.Equal(0, runtime.DestroyCount);
        }
        Assert.False(runtime.Context!.HasEventListeners!("tauri://close-requested", "main"));
    }

    [Fact]
    public async Task HandlerAndDestroyFailuresAreReportedWithoutEscapingNativeCallback()
    {
        var runtime = new CloseRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Handle.EventHandlerFailed += (_, error) => failure.TrySetResult(error);
        var expected = new InvalidOperationException("handler failed");
        using (app.Handle.GetWindow("main")!.OnCloseRequested(_ => Task.FromException(expected)))
        {
            app.Handle.Emit("tauri://close-requested", JsonValue.Null, new RuntimeEventTarget("Window", "main"));
            Assert.Same(expected, await failure.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, runtime.DestroyCount);
        }
        failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Failure = new InvalidOperationException("destroy failed");
        using (app.Handle.GetWindow("main")!.OnCloseRequested(_ => Task.CompletedTask))
        {
            app.Handle.Emit("tauri://close-requested", JsonValue.Null, new RuntimeEventTarget("Window", "main"));
            Assert.Same(runtime.Failure, await failure.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class CloseRuntime : IDesktopRuntime
    {
        public RuntimeContext? Context;
        public Exception? Failure;
        public int DestroyCount;
        public TaskCompletionSource Destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Run(RuntimeContext context) => Context = context;
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string label, string script) { }
        public void Exit(int exitCode = 0) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            Assert.Equal("plugin:window|destroy", command);
            Assert.Equal("main", arguments.GetProperty("label").GetString());
            if (Failure is not null) return Task.FromException<JsonElement>(Failure);
            Interlocked.Increment(ref DestroyCount);
            Destroyed.TrySetResult();
            return Task.FromResult(JsonValue.Null);
        }
    }
}
