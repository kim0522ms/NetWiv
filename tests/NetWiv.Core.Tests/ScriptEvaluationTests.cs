using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class ScriptEvaluationTests
{
    internal static async Task Run()
    {
        var runtime = new EvaluationRuntime
        {
            Evaluate = (_, script, documentId, _) => Task.FromResult(JsonValue.Write(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("script", script);
                writer.WriteNumber("documentId", documentId);
                writer.WriteEndObject();
            }))
        };
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html").Build();
        app.Run();
        var context = runtime.Context!;
        await Throws<InvalidOperationException>(() => app.Handle.EvaluateScriptAsync("main", "1"));
        context.OnDocumentChanged!(new("main", 7, false));
        var value = await app.Handle.EvaluateScriptAsync("main", "40 + 2");
        Assert.Equal("40 + 2", value.GetProperty("script").GetString());
        Assert.Equal(7UL, value.GetProperty("documentId").GetUInt64());

        runtime.Evaluate = (_, _, _, _) => Task.FromException<JsonElement>(new JavaScriptException("TypeError", "bad value", "at eval"));
        var error = await Throws<JavaScriptException>(() => app.Handle.EvaluateScriptAsync("main", "throw new TypeError()"));
        Assert.Equal("TypeError", error.Name);
        Assert.Equal("bad value", error.Message);
        Assert.Equal("at eval", error.JavaScriptStackTrace);

        await CancelsWhenDocumentChanges(app, context, runtime);
        await CancelsWhenAppIsDisposed();
        await CancelsWhenCallerRequests(runtime);
        await IgnoresLateRuntimeResultsAndSkipsPreCanceledCalls();
        await Throws<NotSupportedException>(() => CreateUnsupportedRuntimeEvaluation());
    }

    private static async Task IgnoresLateRuntimeResultsAndSkipsPreCanceledCalls()
    {
        var runtime = new EvaluationRuntime();
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html").Build();
        app.Run();
        var context = runtime.Context!;
        context.OnDocumentChanged!(new("main", 1, false));

        var calls = 0;
        runtime.Evaluate = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(JsonValue.Null);
        };
        using (var preCanceled = new CancellationTokenSource())
        {
            preCanceled.Cancel();
            await Throws<OperationCanceledException>(() => app.Handle.EvaluateScriptAsync("main", "1", preCanceled.Token));
        }
        Assert.Equal(0, calls);

        await VerifyLateResultIsDiscarded(app, context, runtime, nextDocument: 2, close: false);
        await VerifyLateResultIsDiscarded(app, context, runtime, nextDocument: 2, close: true);
    }

    private static async Task VerifyLateResultIsDiscarded(App app, RuntimeContext context, EvaluationRuntime runtime, ulong nextDocument, bool close)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResult = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Evaluate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return lateResult.Task; // Deliberately ignores cancellation.
        };
        var evaluation = app.Handle.EvaluateScriptAsync("main", "1");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.OnDocumentChanged!(new("main", nextDocument, close));
        await Throws<OperationCanceledException>(() => evaluation.WaitAsync(TimeSpan.FromSeconds(2)));

        lateResult.SetResult(JsonValue.From("late result"));
        await Task.Yield();
        Assert.True(evaluation.IsCanceled, "A late runtime result must not complete an evaluation after its document ends.");
    }

    private static async Task CancelsWhenDocumentChanges(App app, RuntimeContext context, EvaluationRuntime runtime)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Evaluate = async (_, _, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return JsonValue.Null;
        };
        var evaluation = app.Handle.EvaluateScriptAsync("main", "new Promise(() => {})");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.OnDocumentChanged!(new("main", 8, false));
        await Throws<OperationCanceledException>(async () => await evaluation);

        var closed = app.Handle.EvaluateScriptAsync("main", "1");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.OnDocumentChanged(new("main", 8, true));
        await Throws<OperationCanceledException>(async () => await closed);
    }

    private static async Task CancelsWhenAppIsDisposed()
    {
        var runtime = new EvaluationRuntime();
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html").Build();
        app.Run();
        runtime.Context!.OnDocumentChanged!(new("main", 1, false));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Evaluate = async (_, _, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return JsonValue.Null;
        };
        var evaluation = app.Handle.EvaluateScriptAsync("main", "1");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        app.Dispose();
        await Throws<OperationCanceledException>(async () => await evaluation);
    }

    private static async Task CancelsWhenCallerRequests(EvaluationRuntime runtime)
    {
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html").Build();
        app.Run();
        runtime.Context!.OnDocumentChanged!(new("main", 1, false));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Evaluate = async (_, _, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return JsonValue.Null;
        };
        using var cancellation = new CancellationTokenSource();
        var evaluation = app.Handle.EvaluateScriptAsync("main", "1", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Throws<OperationCanceledException>(async () => await evaluation);
        app.Dispose();
    }

    private static async Task<JsonElement> CreateUnsupportedRuntimeEvaluation()
    {
        var runtime = new UnsupportedEvaluationRuntime();
        using var app = new Builder().UseRuntime(runtime).Window("main", "Main", "index.html").Build();
        app.Run();
        runtime.Context!.OnDocumentChanged!(new("main", 1, false));
        return await app.Handle.EvaluateScriptAsync("main", "1");
    }

    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class EvaluationRuntime : IDesktopRuntime
    {
        internal RuntimeContext? Context { get; private set; }
        internal Func<string, string, ulong, CancellationToken, Task<JsonElement>> Evaluate { get; set; } =
            static (_, _, _, _) => Task.FromResult(JsonValue.Null);

        public void Run(RuntimeContext context) { Context = context; context.OnReady?.Invoke(); }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
        public void EvaluateScript(string webviewLabel, string script) { }
        public Task<JsonElement> EvaluateScriptAsync(string webviewLabel, string script, ulong documentId, CancellationToken cancellationToken = default) =>
            Evaluate(webviewLabel, script, documentId, cancellationToken);
        public void Exit(int exitCode = 0) { }
    }

    private sealed class UnsupportedEvaluationRuntime : IDesktopRuntime
    {
        internal RuntimeContext? Context { get; private set; }
        public void Run(RuntimeContext context) { Context = context; context.OnReady?.Invoke(); }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
    }
}
