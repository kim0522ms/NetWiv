using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv;
using NetWiv.Runtime;

namespace NetWiv.Tests;

// Shared by the managed generator tests and the executable Core NativeAOT suite.
internal static class InstanceCommandScenarios
{
    public static async Task Lifetimes()
    {
        foreach (var lifetime in new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton })
        {
            var log = new CommandLog();
            var services = Services(log);
            services.Add(new ServiceDescriptor(typeof(InstanceCommands), typeof(InstanceCommands), lifetime));
            await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
            {
                using var app = App(provider);
                var first = await app.Handle.InvokeAsync("instance_identity", JsonValue.EmptyObject);
                var second = await app.Handle.InvokeAsync("instance_identity", JsonValue.EmptyObject);
                Check((first.GetString() == second.GetString()) == (lifetime == ServiceLifetime.Singleton), "Receiver lifetime changed.");
                Check(log.Disposed == (lifetime == ServiceLifetime.Singleton ? 0 : 2), "Receiver disposed at the wrong time.");
                Check(log.DependenciesDisposed == 2, "Invocation services did not leave their scopes.");
            }
            Check(log.Disposed == (lifetime == ServiceLifetime.Singleton ? 1 : 2), "Provider did not dispose its receiver exactly once.");
        }
    }

    public static async Task ConstructorAndParameterShareScope()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<ScopedCommands>().BuildServiceProvider(true);
        using var app = App(provider, "instance_scoped");
        await app.Handle.InvokeAsync("instance_scoped", JsonValue.EmptyObject);
        await app.Handle.InvokeAsync("instance_scoped", JsonValue.EmptyObject);
        Check(log.DependenciesDisposed == 2, "Constructor and method dependency did not share one invocation scope.");
    }

    public static async Task ConcurrentAwaitAndDisposal()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<InstanceCommands>().BuildServiceProvider(true);
        using var app = App(provider);
        var first = app.Handle.InvokeAsync("instance_wait", JsonValue.EmptyObject).AsTask();
        var second = app.Handle.InvokeAsync("instance_wait", JsonValue.EmptyObject).AsTask();
        await log.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(log.Created == 2 && log.Disposed == 0, "Concurrent calls shared a receiver or disposed before await completed.");
        log.Release.SetResult();
        var results = await Task.WhenAll(first, second);
        Check(results[0].GetString() != results[1].GetString(), "Concurrent calls shared a receiver.");
        Check(log.Disposed == 2, "Async disposal was not awaited.");
    }

    public static async Task FailureAndCancellation()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<InstanceCommands>().BuildServiceProvider(true);
        using var app = App(provider);
        await Fails<InvalidOperationException>(() => app.Handle.InvokeAsync("instance_throw", JsonValue.EmptyObject).AsTask());
        Check(log.Disposed == 1, "Synchronous exception leaked receiver.");
        await Fails<InvalidOperationException>(() => app.Handle.InvokeAsync("instance_throw_async", JsonValue.EmptyObject).AsTask());
        Check(log.Disposed == 2, "Async exception leaked receiver.");
        using var cancellation = new CancellationTokenSource();
        var pending = app.Handle.InvokeAsync("instance_cancel", JsonValue.EmptyObject, cancellation.Token).AsTask();
        await log.CancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(log.Disposed == 2, "Cancelled receiver disposed before its task ended.");
        cancellation.Cancel();
        await Fails<OperationCanceledException>(() => pending);
        Check(log.Disposed == 3, "Cancellation leaked receiver.");
        await app.Handle.InvokeAsync("instance_identity", JsonValue.EmptyObject);
        Check(log.Disposed == 4, "Failure prevented the next invocation.");
    }

    public static async Task MissingRegistrationAndConstructorFailure()
    {
        var log = new CommandLog();
        await using var missing = Services(log).BuildServiceProvider(true);
        using (var app = App(missing))
        {
            await Fails<InvalidOperationException>(() => app.Handle.InvokeAsync("instance_identity", JsonValue.EmptyObject).AsTask());
            Check(log.Created == 0, "Unregistered command was automatically constructed.");
        }
        using (var noProvider = new Builder().UseRuntime(new ProbeRuntime()).InvokeHandler("instance_identity").Build())
            await Fails<InvalidOperationException>(() => noProvider.Handle.InvokeAsync("instance_identity", JsonValue.EmptyObject).AsTask());
        await using var failing = Services(log).AddScoped<BrokenCommands>().BuildServiceProvider(true);
        using (var app = App(failing, "instance_broken"))
            await Fails<InvalidOperationException>(() => app.Handle.InvokeAsync("instance_broken", JsonValue.EmptyObject).AsTask());
        Check(log.DependenciesDisposed == 1, "Constructor failure leaked a resolved dependency.");
        await using var missingDependency = new ServiceCollection().AddScoped<ScopedCommands>().BuildServiceProvider();
        using (var app = App(missingDependency, "instance_scoped"))
            await Fails<InvalidOperationException>(() => app.Handle.InvokeAsync("instance_scoped", JsonValue.EmptyObject).AsTask());
    }

    public static async Task GuestIpcAndAuthorization()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<InstanceCommands>().BuildServiceProvider(true);
        var runtime = new ProbeRuntime();
        using var app = new Builder().UseRuntime(runtime).UseServiceProvider(provider).InvokeHandler("instance_echo").Build();
        app.Run();
        runtime.Send("instance_echo", "{}", "https://untrusted.example");
        Check((await runtime.Next()).Contains("not allowed", StringComparison.Ordinal), "Remote guest bypassed ACL.");
        runtime.Send("instance_echo", "{}", "tauri://localhost");
        Check((await runtime.Next()).Contains("missing required key", StringComparison.Ordinal), "Invalid JSON arguments accepted.");
        Check(log.Created == 0, "Denied or malformed request activated a receiver.");
        runtime.Send("instance_echo", "{\"value\":\"hello\"}", "tauri://localhost");
        Check((await runtime.Next()).Contains("runCallback(1,\"hello\")", StringComparison.Ordinal), "Guest did not receive serialized instance result.");
        Check(log.Created == 1 && log.Disposed == 1, "Guest completion did not dispose receiver.");
        runtime.Send("instance_identity", "{}", "tauri://localhost");
        Check((await runtime.Next()).Contains("not found", StringComparison.Ordinal), "Unselected command was exposed.");
        Check(log.Created == 1, "Unselected command activated a receiver.");
    }

    public static async Task PluginInstanceCommandsKeepTheirScopeAndPermissions()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<InstancePlugin>().BuildServiceProvider(true);
        var runtime = new ProbeRuntime();
        using var app = new Builder().UseRuntime(runtime).UseServiceProvider(provider)
            .Plugin(new InstancePlugin(log))
            .Capability(new("plugin-instance", ["instance-probe:allow-identity"], Windows: ["main"]))
            .Build();
        app.Run();
        runtime.Send("plugin:instance-probe|identity", "{}", "https://untrusted.example");
        Check((await runtime.Next()).Contains("not allowed", StringComparison.Ordinal), "Plugin instance bypassed ACL.");
        Check(log.Created == 1 && log.Disposed == 0, "Denied plugin call activated a DI receiver.");
        runtime.Send("plugin:instance-probe|identity", "{}", "tauri://localhost");
        Check((await runtime.Next()).Contains("runCallback(1,\"2\")", StringComparison.Ordinal), "Plugin handler did not resolve its DI receiver.");
        Check(log.Disposed == 1, "Plugin receiver was not disposed with invocation scope.");
        runtime.Send("identity", "{}", "tauri://localhost");
        Check((await runtime.Next()).Contains("not found", StringComparison.Ordinal), "Plugin instance leaked to app registry.");
        Check(log.Created == 2, "App command activated plugin receiver.");
    }

    public static async Task NativeCallbackDoesNotCaptureCallerContext()
    {
        var log = new CommandLog();
        await using var provider = Services(log).AddScoped<InstanceCommands>().BuildServiceProvider(true);
        var runtime = new ProbeRuntime();
        using var app = new Builder().UseRuntime(runtime).UseServiceProvider(provider).InvokeHandler("instance_wait").Build();
        app.Run();
        var held = new HoldingContext();
        try
        {
            var caller = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(held);
                runtime.Send("instance_wait", "{}", "tauri://localhost");
                runtime.Send("instance_wait", "{}", "tauri://localhost");
                Check(ReferenceEquals(SynchronizationContext.Current, held), "Probe changed the caller's context.");
            }
            finally { SynchronizationContext.SetSynchronizationContext(caller); }
            await log.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            log.Release.SetResult();
            var responses = Task.WhenAll(runtime.Next(), runtime.Next());
            await Task.WhenAny(responses, held.Posted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            var captured = held.Posted.Task.IsCompleted;
            // Let a broken probe finish before failing, rather than disposing
            // the app and suppressing its delayed callbacks during cleanup.
            held.Release.SetResult();
            var scripts = await responses;
            Check(scripts.All(script => script.Contains("runCallback(1,", StringComparison.Ordinal)) && log.Disposed == 2,
                "Native callbacks did not complete after invocation scope disposal.");
            Check(!captured, "Native callback probe captured the caller's synchronization context.");
        }
        finally { log.Release.TrySetResult(); held.Release.TrySetResult(); }
    }

    private sealed class HoldingContext : SynchronizationContext
    {
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posted.TrySetResult();
            _ = Resume(callback, state);
        }
        private async Task Resume(SendOrPostCallback callback, object? state)
        {
            await Release.Task.ConfigureAwait(false);
            callback(state);
        }
    }

    private static IServiceCollection Services(CommandLog log)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<InvocationDependency>();
        return services;
    }
    private static App App(IServiceProvider services, params string[] names) => new Builder().UseRuntime(new ProbeRuntime())
        .UseServiceProvider(services).InvokeHandler(names.Length == 0 ?
            ["instance_identity", "instance_wait", "instance_throw", "instance_throw_async", "instance_cancel"] : names).Build();
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Fails<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}

internal sealed class CommandLog
{
    public int Created, Disposed, DependenciesDisposed, Entered;
    public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class InvocationDependency(CommandLog log) : IDisposable
{
    public void Dispose() => Interlocked.Increment(ref log.DependenciesDisposed);
}

internal sealed class InstanceCommands(CommandLog log) : IAsyncDisposable
{
    private readonly int _id = Interlocked.Increment(ref log.Created);
    [Command("instance_identity")]
    public JsonElement Identity([FromServices] InvocationDependency dependency) => JsonValue.From(_id.ToString());
    [Command("instance_echo")]
    public JsonElement Echo(JsonElement value) => value;
    [Command("instance_wait")]
    public async Task<JsonElement> Wait()
    {
        if (Interlocked.Increment(ref log.Entered) == 2) log.BothEntered.SetResult();
        await log.Release.Task;
        return JsonValue.From(_id.ToString());
    }
    [Command("instance_throw")]
    public void Throw() => throw new InvalidOperationException("sync failure");
    [Command("instance_throw_async")]
    public async ValueTask ThrowAsync() { await Task.Yield(); throw new InvalidOperationException("async failure"); }
    [Command("instance_cancel")]
    public async Task Cancel(CancellationToken token)
    {
        log.CancelEntered.SetResult();
        await Task.Delay(Timeout.Infinite, token);
    }
    public async ValueTask DisposeAsync() { await Task.Yield(); Interlocked.Increment(ref log.Disposed); }
}

internal sealed class ScopedCommands(InvocationDependency dependency)
{
    [Command("instance_scoped")]
    public void Check([FromServices] InvocationDependency parameter) =>
        InstanceCommandScenarios.Check(ReferenceEquals(dependency, parameter), "Constructor and parameter use different scopes.");
}

internal sealed class BrokenCommands
{
    public BrokenCommands(InvocationDependency dependency) => throw new InvalidOperationException("constructor failure");
    [Command("instance_broken")] public void Run() => throw new InvalidOperationException("Should never run");
}

internal sealed class ProbeRuntime : IDesktopRuntime
{
    private RuntimeContext? _context;
    private readonly ConcurrentQueue<string> _scripts = new();
    private readonly SemaphoreSlim _ready = new(0);
    public void Run(RuntimeContext context) => _context = context;
    public void Dispatch(Action action) => action();
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected native fallback: " + command);
    public void EvaluateScript(string webviewLabel, string script) { _scripts.Enqueue(script); _ready.Release(); }
    public void Exit(int exitCode = 0) { }
    public void Send(string command, string payload, string source)
    {
        const string marker = "__NETWIV_INVOKE_KEY__: '";
        var start = _context!.InitializationScript.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var key = _context.InitializationScript.Substring(start, 64);
        var caller = SynchronizationContext.Current;
        try
        {
            // Native callbacks do not run on xUnit's synchronization context.
            // Capturing it makes IPC continuations compete with Roslyn tests.
            SynchronizationContext.SetSynchronizationContext(null);
            _context.OnMessage(new("main", new Uri(source),
            "{\"cmd\":\"" + command + "\",\"callback\":1,\"error\":2,\"payload\":" + payload +
            ",\"__NETWIV_INVOKE_KEY__\":\"" + key + "\"}"));
        }
        finally { SynchronizationContext.SetSynchronizationContext(caller); }
    }
    public async Task<string> Next()
    {
        if (!await _ready.WaitAsync(TimeSpan.FromSeconds(10))) throw new TimeoutException("Missing IPC callback");
        return _scripts.TryDequeue(out var result) ? result : throw new InvalidOperationException("Missing script");
    }
}

internal sealed class InstancePlugin(CommandLog log) : IPlugin, IAsyncDisposable
{
    private readonly int _id = Interlocked.Increment(ref log.Created);
    public string Name => "instance-probe";
    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<InstancePlugin>("identity");
        context.Permission(new PermissionDefinition("allow-identity", "identity"));
    }
    [Command("identity")] public JsonElement Identity() => JsonValue.From(_id.ToString());
    public async ValueTask DisposeAsync() { await Task.Yield(); Interlocked.Increment(ref log.Disposed); }
}
