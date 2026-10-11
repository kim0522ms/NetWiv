using System.Diagnostics;
using NetWiv.Build.Execution;

internal static class ProcessOwnershipTests
{
    internal static async Task ExitCodes()
    {
        foreach (var capture in new[] { false, true })
        foreach (var expected in new[] { 0, 17 })
        {
            var result = await ProcessRunner.RunAsync(new ProcessSpec(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                OperatingSystem.IsWindows() ? ["/d", "/c", "exit " + expected] : ["-c", "exit " + expected],
                Environment.CurrentDirectory, CaptureOutput: capture));
            if (result.ExitCode != expected) throw new Exception($"Expected exit {expected}, received {result.ExitCode}, capture={capture}.");
        }
        if (!OperatingSystem.IsWindows()) return; // Windows Job Object lifetime regression.
        var report = Path.Combine(Path.GetTempPath(), "netwiv-process-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var result = await ProcessRunner.RunAsync(new ProcessSpec(Environment.ProcessPath!,
                ["--owned-process-probe", "parent", report], Environment.CurrentDirectory));
            if (result.ExitCode != 0) throw new Exception("The successful probe parent lost its exit code.");
            var pid = int.Parse(await File.ReadAllTextAsync(report));
            try
            {
                using var child = Process.GetProcessById(pid);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException) { }
        }
        finally { File.Delete(report); }
    }

    internal static async Task CancelledBeforeStart()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        try { await ProcessRunner.RunAsync(new ProcessSpec("does-not-exist-netwiv-probe", [], Environment.CurrentDirectory), stop.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("The cancelled runner attempted to launch a process.");
    }

    internal static async Task<int> Probe(string phase, string report)
    {
        if (phase == "child")
        {
            await File.WriteAllTextAsync(report, Environment.ProcessId.ToString());
            await Task.Delay(Timeout.Infinite);
        }
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in new[] { "--owned-process-probe", "child", report }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(report) || new FileInfo(report).Length == 0) await Task.Delay(10, stop.Token);
        return 0;
    }
}
