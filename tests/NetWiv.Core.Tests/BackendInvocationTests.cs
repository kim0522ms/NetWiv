using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class BackendInvocationTests
{
    internal static async Task TrustedContextAndGeneratedInjection()
    {
        var runtime = new FakeRuntime();
        var app = new Builder().UseRuntime(runtime)
            .Capability(new("backend-denial", ["backend-invocation:allow", "backend-invocation:deny"], Windows: ["main"]))
            .Build();
        app.Run();

        var guest = await GuestIpc.Invoke(runtime, "plugin:backend-invocation|inspect", "{}");
        Assert.Equal("error", guest.Headers!["Tauri-Response"]);
        Assert.True(GuestIpc.Body(guest).Contains("explicitly denied", StringComparison.Ordinal) ||
            GuestIpc.Body(guest).Contains("not allowed", StringComparison.Ordinal), "The guest command did not fail authorization.");

        var result = await app.Handle.InvokeAsync("plugin:backend-invocation|inspect", JsonValue.EmptyObject);
        Assert.Equal("true|native|native|netwiv://native/|false|false|false|true|true|0|0", result.GetString());
        app.Dispose();
    }

    internal static async Task GeneratedChannelUsesNativeCallbackTarget()
    {
        var runtime = new BackendRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();

        await app.Handle.InvokeAsync("plugin:backend-invocation|send", JsonValue.Parse("{\"updates\":\"__CHANNEL__:47\"}"));

        Assert.Equal("native", runtime.LastScriptLabel);
        Assert.Contains("runCallback(47", runtime.LastScript!);
        Assert.Contains("\"message\":\"ready\"", runtime.LastScript!);

        await app.Handle.InvokeAsync("plugin:backend-invocation|send_bare", JsonValue.Parse("{\"updates\":\"__CHANNEL__:48\"}"));
        Assert.Equal("native", runtime.LastScriptLabel);
        Assert.Contains("runCallback(48", runtime.LastScript!);

        var invalid = await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(
            "plugin:backend-invocation|send", JsonValue.Parse("{\"updates\":\"not-a-channel\"}")).AsTask());
        Assert.Equal("The value is not a valid Tauri channel identifier.", invalid.Message);
        var missing = await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(
            "plugin:backend-invocation|send", JsonValue.EmptyObject).AsTask());
        Assert.Contains("missing required key updates", missing.Message);
    }

    internal static async Task CancellationAndDocumentLifetime()
    {
        var runtime = new BackendRuntime();
        var app = new Builder().UseRuntime(runtime).Build();
        app.Run();
        using var caller = new CancellationTokenSource();
        var invocation = app.Handle.InvokeAsync("plugin:backend-invocation|wait", JsonValue.EmptyObject, caller.Token).AsTask();
        await BackendInvocationPlugin.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Context!.OnDocumentChanged!(new("native", 2, false));
        Assert.True(!BackendInvocationPlugin.WaitToken.IsCancellationRequested, "A document change canceled a trusted backend invocation.");

        caller.Cancel();
        try { await invocation; throw new InvalidOperationException("Caller cancellation did not cancel the command."); }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { }

        BackendInvocationPlugin.ResetWait();
        var shutdownInvocation = app.Handle.InvokeAsync("plugin:backend-invocation|wait", JsonValue.EmptyObject).AsTask();
        await BackendInvocationPlugin.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        app.Dispose();
        try { await shutdownInvocation; throw new InvalidOperationException("Application disposal did not cancel the command."); }
        catch (OperationCanceledException) { }
    }

    internal static async Task BackendResourcesBelongToTheApplication()
    {
        var runtime = new BackendRuntime();
        var app = new Builder().UseRuntime(runtime).Build();
        app.Run();

        var id = uint.Parse((await app.Handle.InvokeAsync("plugin:backend-invocation|own_resource", JsonValue.EmptyObject)).GetString()!);
        var resource = BackendInvocationPlugin.Resource!;
        runtime.Context!.OnDocumentChanged!(new("native", 2, false));
        Assert.True(!resource.Disposed, "A document change disposed an application-owned resource.");
        Assert.True(ReferenceEquals(resource, app.Handle.Resources.TryGet(id)), "The backend resource was not registered in the app resource table.");

        app.Dispose();
        Assert.True(resource.Disposed, "Application disposal did not dispose the backend resource.");
    }

    internal static async Task ExceptionIdentityAndPayloadArePreserved()
    {
        var structured = new InvokeException(JsonValue.Parse("{\"code\":\"backend\",\"detail\":42}"));
        var synchronous = new InvalidOperationException("backend sync failure");
        var asynchronous = new InvalidOperationException("backend async failure");
        using var app = new Builder().UseRuntime(new BackendRuntime())
            .RegisterCommand("structured_failure", (_, _, _) => ValueTask.FromException<JsonElement>(structured))
            .RegisterCommand("sync_failure", (_, _, _) => throw synchronous)
            .RegisterCommand("async_failure", async (_, _, _) =>
            {
                await Task.Yield();
                throw asynchronous;
            })
            .Build();

        var structuredError = await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync("structured_failure", JsonValue.EmptyObject).AsTask());
        Assert.True(ReferenceEquals(structured, structuredError), "The structured invocation exception instance was replaced.");
        Assert.Equal("{\"code\":\"backend\",\"detail\":42}", structuredError.ErrorPayload.GetRawText());

        var syncError = await CaptureAsync<InvalidOperationException>(() => app.Handle.InvokeAsync("sync_failure", JsonValue.EmptyObject).AsTask());
        Assert.True(ReferenceEquals(synchronous, syncError), "The synchronous exception instance was replaced.");
        var asyncError = await CaptureAsync<InvalidOperationException>(() => app.Handle.InvokeAsync("async_failure", JsonValue.EmptyObject).AsTask());
        Assert.True(ReferenceEquals(asynchronous, asyncError), "The asynchronous exception instance was replaced.");
    }

    internal static async Task SynchronousHandlerStartsOnCallerThread()
    {
        var handlerThread = 0;
        using var app = new Builder().UseRuntime(new BackendRuntime())
            .RegisterCommand("sync_before_await", (_, _, _) =>
            {
                handlerThread = Environment.CurrentManagedThreadId;
                return ValueTask.FromResult(JsonValue.Null);
            })
            .Build();
        var callerThread = Environment.CurrentManagedThreadId;

        var invocation = app.Handle.InvokeAsync("sync_before_await", JsonValue.EmptyObject, default);

        Assert.Equal(callerThread, handlerThread);
        await invocation;
    }

    private static async Task<T> CaptureAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}

[Plugin]
internal sealed class BackendInvocationPlugin : IPlugin
{
    private static TaskCompletionSource _waitEntered = NewSignal();
    private static CancellationToken _waitToken;

    public string Name => "backend-invocation";
    internal static TaskCompletionSource WaitEntered => _waitEntered;
    internal static CancellationToken WaitToken => _waitToken;
    internal static BackendResource? Resource { get; private set; }

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<BackendInvocationPlugin>("inspect", "send", "send_bare", "wait", "own_resource");
        context.Permission(new PermissionDefinition("allow", ["inspect", "send", "send_bare", "wait", "own_resource"], [],
            [JsonValue.Parse("{\"path\":\"allowed\"}")], [JsonValue.Parse("{\"path\":\"denied\"}")]));
        context.Permission(new PermissionDefinition("deny", [], ["inspect"]));
    }

    internal static void ResetWait()
    {
        _waitEntered = NewSignal();
        _waitToken = default;
    }

    [Command]
    public static string Inspect(AppHandle app, InvokeContext context, Request request) =>
        $"{context.IsTrusted.ToString().ToLowerInvariant()}|{context.WindowLabel}|{context.WebviewLabel}|{context.Source}|" +
        $"{(context.App.GetWindow(context.WindowLabel) is not null).ToString().ToLowerInvariant()}|" +
        $"{(context.App.GetWebview(context.WebviewLabel) is not null).ToString().ToLowerInvariant()}|" +
        $"{(context.App.GetWebviewWindow(context.WebviewLabel) is not null).ToString().ToLowerInvariant()}|" +
        $"{ReferenceEquals(app, context.App).ToString().ToLowerInvariant()}|" +
        $"{(!request.Body().IsBinary && request.Body().Json.ValueKind == JsonValueKind.Object).ToString().ToLowerInvariant()}|" +
        $"{context.Scope.Allow.Count}|{context.Scope.Deny.Count}";

    [Command]
    public static void Send(Channel<string> updates) => updates.Send("ready");

    [Command]
    public static void SendBare(Channel updates) => updates.Send(JsonValue.From("ready"));

    [Command]
    public static async Task Wait(CancellationToken cancellationToken)
    {
        _waitToken = cancellationToken;
        _waitEntered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    [Command]
    public static string OwnResource(InvokeContext context)
    {
        Resource = new BackendResource();
        return context.AddResource(Resource).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class BackendResource : IDisposable
{
    internal bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class BackendRuntime : IDesktopRuntime
{
    public RuntimeContext? Context { get; private set; }
    public string? LastScriptLabel { get; private set; }
    public string? LastScript { get; private set; }
    public void Run(RuntimeContext context) { Context = context; context.OnReady?.Invoke(); }
    public void Dispatch(Action action) => action();
    public void EvaluateScript(string webviewLabel, string script) { LastScriptLabel = webviewLabel; LastScript = script; }
    public void Exit(int exitCode = 0) { }
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
}
