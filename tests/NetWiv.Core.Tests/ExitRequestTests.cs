using NetWiv;
using NetWiv.Runtime;
using System.Collections.Concurrent;
using System.Text.Json;

internal static class ExitRequestTests
{
    internal static async Task ReentrancyAndHandlerSnapshot()
    {
        var runtime = new ExitRuntime();
        var calls = new List<string>();
        var registered = false;
        var rejected = false;
        using var app = new Builder().UseRuntime(runtime)
            .OnExitRequested(async (handle, _) =>
            {
                calls.Add("first");
                try { await handle.RequestExitAsync(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!registered)
                {
                    registered = true;
                    handle.OnExitRequested((_, _) => { calls.Add("added"); });
                }
            })
            .OnExitRequested((_, _) => { calls.Add("second"); }).Build();
        Assert.True(await app.RequestExitAsync(), "Approved request did not exit.");
        Assert.True(rejected, "A handler reentered the pending exit request.");
        Assert.Equal("first,second", string.Join(',', calls));
        calls.Clear();
        Assert.True(await app.RequestExitAsync(), "The request gate remained held after approval.");
        Assert.Equal("first,second,added", string.Join(',', calls));
        Assert.Equal(2, runtime.Exits.Count);
    }

    internal static async Task ApprovalVetoAndOrdering()
    {
        var runtime = new ExitRuntime();
        var calls = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var app = new Builder().UseRuntime(runtime)
            .OnExitRequested(async (_, value) => { calls.Add("save"); Assert.Equal(7, value.ExitCode); await release.Task; })
            .Build();
        app.OnExitRequested((_, _) => { calls.Add("app"); });
        app.Handle.OnExitRequested((_, _) => { calls.Add("handle"); });
        var request = app.RequestExitAsync(7);
        Assert.Equal("save", string.Join(',', calls));
        Assert.Equal(0, runtime.Exits.Count);
        try { await app.RequestExitAsync(); throw new Exception("Concurrent exit request was accepted."); }
        catch (InvalidOperationException) { }
        release.SetResult();
        Assert.True(await request, "Approved request did not exit.");
        Assert.Equal("save,app,handle", string.Join(',', calls));
        Assert.Equal(7, runtime.Exits.Single());

        var vetoRuntime = new ExitRuntime();
        using var veto = new Builder().UseRuntime(vetoRuntime)
            .OnExitRequested((_, value) => { calls.Add("veto"); value.PreventDefault(); })
            .OnExitRequested((_, _) => { calls.Add("unexpected"); }).Build();
        Assert.True(!await veto.RequestExitAsync(), "A vetoed request exited.");
        Assert.Equal(0, vetoRuntime.Exits.Count);
        Assert.Equal("veto", calls[^1]);
        Assert.True(!await veto.RequestExitAsync(), "The request gate was not released after a veto.");
    }

    internal static async Task CancellationAndFailures()
    {
        var runtime = new ExitRuntime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        using var app = new Builder().UseRuntime(runtime)
            .OnExitRequested(async (_, value) =>
            {
                Assert.Equal(stop.Token, value.CancellationToken);
                await release.Task;
                throw new InvalidOperationException("late handler failure");
            }).Build();
        app.Handle.EventHandlerFailed += (value, error) =>
        {
            Assert.Equal("netwiv://exit-requested", value.Name);
            if (error.Message == "late handler failure") lateFailure.TrySetResult();
        };
        var request = app.RequestExitAsync(cancellationToken: stop.Token);
        stop.Cancel();
        try { await request; throw new Exception("Cancelled request was approved."); }
        catch (OperationCanceledException) { }
        release.SetResult();
        await lateFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, runtime.Exits.Count);

        foreach (var asynchronous in new[] { false, true })
        {
            var failingRuntime = new ExitRuntime();
            using var failing = new Builder().UseRuntime(failingRuntime)
                .OnExitRequested((Func<AppHandle, ExitRequestedEvent, Task>)((_, _) => asynchronous
                    ? Task.FromException(new InvalidOperationException("save failed"))
                    : throw new InvalidOperationException("save failed"))).Build();
            var reports = 0;
            failing.Handle.EventHandlerFailed += (_, _) => reports++;
            try { await failing.RequestExitAsync(); throw new Exception("A failed request was approved."); }
            catch (InvalidOperationException error) when (error.Message == "save failed") { }
            Assert.Equal(1, reports);
            Assert.Equal(0, failingRuntime.Exits.Count);
        }
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        using var noHandlers = new Builder().UseRuntime(runtime).Build();
        try { await noHandlers.RequestExitAsync(cancellationToken: preCancelled.Token); throw new Exception("Pre-cancelled request exited."); }
        catch (OperationCanceledException) { }
        Assert.Equal(0, runtime.Exits.Count);

        using var beforeApproval = new CancellationTokenSource();
        var beforeRuntime = new ExitRuntime();
        var laterCalls = 0;
        using var before = new Builder().UseRuntime(beforeRuntime)
            .OnExitRequested((_, _) => { beforeApproval.Cancel(); })
            .OnExitRequested((_, _) => { laterCalls++; }).Build();
        try { await before.RequestExitAsync(cancellationToken: beforeApproval.Token); throw new Exception("Cancellation before approval exited."); }
        catch (OperationCanceledException) { }
        Assert.Equal(0, laterCalls);
        Assert.Equal(0, beforeRuntime.Exits.Count);

        using var afterApproval = new CancellationTokenSource();
        var afterRuntime = new ExitRuntime { OnExit = _ => afterApproval.Cancel() };
        using var after = new Builder().UseRuntime(afterRuntime).Build();
        Assert.True(await after.RequestExitAsync(cancellationToken: afterApproval.Token), "Cancellation after approval retracted an admitted exit.");
        Assert.Equal(1, afterRuntime.Exits.Count);
    }

    internal static async Task ForceExitAndDisposalInvalidatePending()
    {
        foreach (var dispose in new[] { false, true })
        {
            var runtime = new ExitRuntime();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var laterCalls = 0;
            using var app = new Builder().UseRuntime(runtime)
                .OnExitRequested(async (_, _) => { await release.Task; })
                .OnExitRequested((_, _) => { laterCalls++; }).Build();
            var request = app.RequestExitAsync(3);
            if (dispose) app.Dispose();
            else app.Handle.Exit(9);
            release.SetResult();
            Assert.True(!await request, "An invalidated request submitted a stale exit.");
            Assert.Equal(0, laterCalls);
            Assert.Equal(dispose ? 0 : 1, runtime.Exits.Count);
            if (!dispose) Assert.Equal(9, runtime.Exits.Single());
            else
            {
                Assert.Throws<ObjectDisposedException>(() => app.OnExitRequested((_, _) => { }));
                try { await app.RequestExitAsync(); throw new Exception("Disposed app accepted an exit request."); }
                catch (ObjectDisposedException) { }
            }
        }
        var plainRuntime = new ExitRuntime();
        using var plain = new Builder().UseRuntime(plainRuntime).Build();
        Assert.True(await plain.RequestExitAsync(4), "A request without handlers did not exit.");
        Assert.Equal(4, plainRuntime.Exits.Single());
    }

    private sealed class ExitRuntime : IDesktopRuntime
    {
        internal ConcurrentQueue<int> Exits { get; } = new();
        internal Action<int>? OnExit { get; init; }
        public void Run(RuntimeContext context) => context.OnReady?.Invoke();
        public void Exit(int exitCode = 0) { OnExit?.Invoke(exitCode); Exits.Enqueue(exitCode); }
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string webviewLabel, string script) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
    }
}
