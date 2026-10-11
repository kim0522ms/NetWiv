using System.Diagnostics;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Process;
using NetWiv.Plugin.SingleInstance;
using NetWiv.Runtime;

internal static class RestartLifecycleTests
{
    internal static int Probe(string root)
    {
        var second = File.Exists(Path.Combine(root, "first"));
        File.WriteAllText(Path.Combine(root, second ? "second-started" : "first"), Environment.ProcessId.ToString());
        using var runtime = new ProbeRuntime();
        using var app = new Builder().UseRuntime(runtime).Metadata(new("Restart test", "1.0", "dev.netwiv." + Path.GetFileName(root)))
            // Delay cleanup before SingleInstance releases its lock; startup must wait for all disposal.
            .Plugin(new DisposalBarrier(root, second))
            .Plugin(new SingleInstancePlugin((_, _, _) => File.WriteAllText(Path.Combine(root, "duplicate"), "unexpected second instance")))
            .Plugin(new ProcessPlugin())
            .Setup(value =>
            {
                if (second) { File.WriteAllText(Path.Combine(root, "second-ready"), "ready"); value.Handle.Exit(); }
                else value.Handle.InvokeAsync("plugin:process|restart", JsonValue.EmptyObject).AsTask().GetAwaiter().GetResult();
            }).Build();
        app.Run();
        return 0;
    }

    internal static async Task RestartAfterCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p1-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? first = null;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            info.ArgumentList.Add("--restart-probe"); info.ArgumentList.Add(root);
            first = Process.Start(info)!;
            await WaitFor(Path.Combine(root, "disposing"));
            // Exercise a real process launch during a deliberately slow cleanup.
            await Task.Delay(1000);
            var early = File.Exists(Path.Combine(root, "second-started"));
            File.WriteAllText(Path.Combine(root, "release"), "release");
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, first.ExitCode);
            await WaitFor(Path.Combine(root, "second-ready"));
            await WaitFor(Path.Combine(root, "second-disposed"));
            Assert.True(!early, "Replacement started before plugin cleanup and lock release.");
            Assert.True(!File.Exists(Path.Combine(root, "duplicate")), "Restart was treated as a duplicate launch.");
        }
        finally
        {
            File.WriteAllText(Path.Combine(root, "release"), "release");
            if (first is { HasExited: false }) { first.Kill(true); await first.WaitForExitAsync(); }
            first?.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitFor(string path)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) await Task.Delay(20, stop.Token);
    }

    private sealed class DisposalBarrier(string root, bool second) : IPlugin
    {
        public string Name => "restart-barrier";
        public void Initialize(PluginContext context) { }
        public void Dispose()
        {
            if (second) { File.WriteAllText(Path.Combine(root, "second-disposed"), "done"); return; }
            File.WriteAllText(Path.Combine(root, "disposing"), "waiting");
            WaitFor(Path.Combine(root, "release")).GetAwaiter().GetResult();
        }
    }

    private sealed class ProbeRuntime : IDesktopRuntime, IDisposable
    {
        private readonly ManualResetEventSlim _exit = new();
        public void Run(RuntimeContext context) { context.OnReady?.Invoke(); if (!_exit.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); }
        public void Exit(int exitCode = 0) => _exit.Set();
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string label, string script) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
        public void Dispose() => _exit.Dispose();
    }
}
