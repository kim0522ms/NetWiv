using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetWiv;
using NetWiv.Hosting;
using NetWiv.Runtime;

internal static class HostingTests
{
    internal static async Task ServicesConfigurationAndLifecycle()
    {
        var events = new ConcurrentQueue<string>();
        var runtime = new BlockingRuntime(events);
        var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Greeting:Prefix"] = "Hello" });
        builder.Services.Configure<GreetingOptions>(builder.Configuration.GetSection("Greeting"));
        builder.Services.AddSingleton(events);
        builder.Services.AddHostedService<RecordingHostedService>();
        builder.Desktop.UseRuntime(runtime)
            .RegisterCommand("greet", static (context, args, _) =>
            {
                var options = context.Services.GetRequiredService<IOptions<GreetingOptions>>().Value;
                return ValueTask.FromResult(JsonValue.From($"{options.Prefix} {args.GetProperty("name").GetString()}"));
            })
            .Setup(app => app.Handle.InvokeAsync("greet", JsonValue.Parse("""{"name":"Ada"}""")).AsTask()
                .ContinueWith(result => { events.Enqueue("command:" + result.Result.GetString()); app.Handle.Exit(); }, TaskScheduler.Default));
        await using var app = builder.Build();

        Assert.True(ReferenceEquals(app.Handle, app.Services.GetRequiredService<AppHandle>()), "AppHandle was not registered as a singleton.");
        Assert.True(ReferenceEquals(app.Services, app.Handle.Services), "The app does not use the host service provider.");
        Assert.True(ReferenceEquals(app.Services.GetRequiredService<ILoggerFactory>(), app.Handle.LoggerFactory), "The host logger factory was not wired.");
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.TrimEndingDirectorySeparator(app.Environment.ContentRootPath));
        app.Run();

        Assert.Equal("hosted:start|runtime:run|command:Hello Ada|runtime:exit|runtime:returned|hosted:stop", string.Join("|", events));
        Assert.Throws<InvalidOperationException>(app.Run);
    }

    internal static async Task GeneratedCommandsResolveServices()
    {
        var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddScoped<InvocationProbe>();
        builder.Services.AddKeyedSingleton("label", new ProbeLabel("keyed"));
        builder.Desktop.UseRuntime(new BlockingRuntime(new())).InvokeHandler("hosted_service_probe");
        await using var app = builder.Build();

        var first = await app.Handle.InvokeAsync("hosted_service_probe", JsonValue.EmptyObject);
        var second = await app.Handle.InvokeAsync("hosted_service_probe", JsonValue.EmptyObject);

        Assert.Equal("keyed:main", first.GetString()![..10]);
        Assert.True(first.GetString() != second.GetString(), "Two invocations shared one service scope.");
    }

    internal static async Task ShutdownWaitsForCommandsInFlight()
    {
        var events = new ConcurrentQueue<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<JsonElement>? pending = null;
        var builder = CreateSlowCommandBuilder(events, release);
        builder.Services.AddHostedService<RecordingHostedService>();
        builder.Desktop.Setup(app =>
        {
            pending = app.Handle.InvokeAsync("slow", JsonValue.EmptyObject).AsTask();
            app.Handle.Exit();
        });
        var app = builder.Build();
        _ = Task.Delay(300).ContinueWith(_ => release.SetResult(), TaskScheduler.Default);

        app.Run();
        await app.DisposeAsync();
        await pending!;

        Assert.Equal("hosted:start|runtime:run|runtime:exit|runtime:returned|command:done|hosted:stop|singleton:disposed", string.Join("|", events));
    }

    internal static async Task DisposeWaitsForCommandsInFlight()
    {
        foreach (var asynchronous in new[] { true, false })
        {
            var events = new ConcurrentQueue<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var app = CreateSlowCommandBuilder(events, release).Build();
            var pending = app.Handle.InvokeAsync("slow", JsonValue.EmptyObject).AsTask();
            _ = Task.Delay(300).ContinueWith(_ => release.SetResult(), TaskScheduler.Default);

            if (asynchronous) await app.DisposeAsync();
            else app.Dispose();
            await pending;

            Assert.Equal("command:done|singleton:disposed", string.Join("|", events));
        }
    }

    internal static async Task ShutdownTimeoutBoundsHungCommands()
    {
        var events = new ConcurrentQueue<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var records = new CapturingLoggerProvider();
        Task<JsonElement>? pending = null;
        var builder = CreateSlowCommandBuilder(events, release);
        builder.Logging.AddProvider(records);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMilliseconds(200));
        builder.Desktop.Setup(app =>
        {
            pending = app.Handle.InvokeAsync("slow", JsonValue.EmptyObject).AsTask();
            app.Handle.Exit();
        });
        var app = builder.Build();
        var watch = Stopwatch.StartNew();
        try
        {
            app.Run();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Shutdown waited past its timeout for a command that ignores cancellation.");
            Assert.True(records.Records.Any(record => record.Level == LogLevel.Warning && record.EventId == 2 && record.Message.Contains("shutdown timeout", StringComparison.Ordinal)),
                "An abandoned command was not reported.");
        }
        finally
        {
            release.SetResult();
            await pending!.ContinueWith(_ => { }, TaskScheduler.Default);
            await app.DisposeAsync();
        }
    }

    // The "slow" command ignores its cancellation token, then uses a singleton that fails once disposed.
    private static NetWivApplicationBuilder CreateSlowCommandBuilder(ConcurrentQueue<string> events, TaskCompletionSource release)
    {
        var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(events);
        builder.Services.AddSingleton<TrackedSingleton>();
        builder.Desktop.UseRuntime(new BlockingRuntime(events)).RegisterCommand("slow", async (context, _, _) =>
        {
            await release.Task;
            context.Services.GetRequiredService<TrackedSingleton>().Use();
            events.Enqueue("command:done");
            return JsonValue.Null;
        });
        return builder;
    }

    internal static Task StopApplicationExitsEventLoop()
    {
        // A stop requested while the event loop runs exits it; one requested during startup skips it.
        foreach (var duringStartup in new[] { false, true })
        {
            var events = new ConcurrentQueue<string>();
            var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Services.AddHostedService(services => new StoppingHostedService(
                services.GetRequiredService<IHostApplicationLifetime>(), services.GetRequiredService<AppHandle>(), duringStartup));
            builder.Desktop.UseRuntime(new BlockingRuntime(events));
            using var app = builder.Build();
            app.Run();
            Assert.Equal(duringStartup ? "" : "runtime:run|runtime:exit|runtime:returned", string.Join("|", events));
        }
        return Task.CompletedTask;
    }

    internal static int RestartProbe(string root)
    {
        var second = File.Exists(Path.Combine(root, "first"));
        File.WriteAllText(Path.Combine(root, second ? "second-started" : "first"), Environment.ProcessId.ToString());
        var builder = NetWivApplication.CreateBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddHostedService(_ => new SlowStopHostedService(root, second));
        builder.Desktop.UseRuntime(new BlockingRuntime(new())).Setup(app =>
        {
            if (second) { File.WriteAllText(Path.Combine(root, "second-ready"), "ready"); app.Handle.Exit(); }
            else app.Handle.Restart();
        });
        using var app = builder.Build();
        app.Run();
        return 0;
    }

    internal static async Task RestartWaitsForHostedServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-hosted-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? first = null;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            info.ArgumentList.Add("--hosted-restart-probe"); info.ArgumentList.Add(root);
            first = Process.Start(info)!;
            await WaitFor(Path.Combine(root, "stopping"));
            // Keep the hosted service stopping long enough for an early relaunch to become visible.
            await Task.Delay(1000);
            var early = File.Exists(Path.Combine(root, "second-started"));
            File.WriteAllText(Path.Combine(root, "release"), "release");
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, first.ExitCode);
            await WaitFor(Path.Combine(root, "second-ready"));
            await WaitFor(Path.Combine(root, "second-stopped"));
            Assert.True(!early, "The replacement process started before hosted services stopped.");
        }
        finally
        {
            File.WriteAllText(Path.Combine(root, "release"), "release");
            if (first is { HasExited: false }) { first.Kill(true); await first.WaitForExitAsync(); }
            first?.Dispose();
            await Task.Delay(200);
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitFor(string path)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) await Task.Delay(20, stop.Token);
    }

    private sealed class TrackedSingleton(ConcurrentQueue<string> events) : IDisposable
    {
        private bool _disposed;
        public void Use() => ObjectDisposedException.ThrowIf(_disposed, this);
        public void Dispose() { _disposed = true; events.Enqueue("singleton:disposed"); }
    }

    internal sealed class InvocationProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    internal sealed record ProbeLabel(string Value);

    internal static class Commands
    {
        // Generated binding: two scoped parameters share the invocation scope; the keyed singleton and AppHandle resolve from DI.
        [Command("hosted_service_probe")]
        public static JsonElement ServiceProbe(
            [FromServices] InvocationProbe first,
            [FromServices] InvocationProbe second,
            [FromKeyedServices("label")] ProbeLabel label,
            [FromServices] AppHandle handle) =>
            ReferenceEquals(first, second)
                ? JsonValue.From($"{label.Value}:{(handle.GetWebview("main") is null ? "none" : "main")}:{first.Id}")
                : throw new InvalidOperationException("One invocation must share its scope.");
    }

    // The NativeAOT configuration binder generator requires public or internal option types.
    internal sealed class GreetingOptions
    {
        public string Prefix { get; set; } = "";
    }

    private sealed class RecordingHostedService(ConcurrentQueue<string> events) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { events.Enqueue("hosted:start"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { events.Enqueue("hosted:stop"); return Task.CompletedTask; }
    }

    private sealed class StoppingHostedService(IHostApplicationLifetime lifetime, AppHandle handle, bool duringStartup) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (duringStartup) lifetime.StopApplication();
            // Request the stop from a worker once the event loop is accepting work.
            else handle.Events.Listen("ready", _ => Task.Run(lifetime.StopApplication));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SlowStopHostedService(string root, bool second) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (second) { File.WriteAllText(Path.Combine(root, "second-stopped"), "done"); return; }
            File.WriteAllText(Path.Combine(root, "stopping"), "waiting");
            await WaitFor(Path.Combine(root, "release"));
        }
    }

    /// <summary>Runs Setup on the calling thread, then blocks like a native event loop until Exit.</summary>
    private sealed class BlockingRuntime(ConcurrentQueue<string> events) : IDesktopRuntime
    {
        private readonly ManualResetEventSlim _exit = new();
        private volatile bool _running;

        public void Run(RuntimeContext context)
        {
            events.Enqueue("runtime:run");
            _running = true;
            context.OnReady?.Invoke();
            context.OnEvent(new RuntimeEvent("ready", null, JsonValue.Null));
            if (!_exit.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The event loop was not exited.");
            _running = false;
            events.Enqueue("runtime:returned");
        }

        public void Exit(int exitCode = 0)
        {
            if (!_running) throw new InvalidOperationException("The NetWiv native runtime is not running.");
            events.Enqueue("runtime:exit");
            _exit.Set();
        }

        public void Dispatch(Action action) => action();
        public void EvaluateScript(string webviewLabel, string script) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
    }
}
