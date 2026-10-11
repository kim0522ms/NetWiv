using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace NetWiv.Build.Execution;

public static class ProcessRunner
{
    private static readonly ConditionalWeakTable<Process, SafeFileHandle> Jobs = new();
    public static async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(spec);
        var stdout = spec.CaptureOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        var stderr = spec.CaptureOutput ? process.StandardError.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
        int exitCode;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                await StopAsync(process);
            }
            throw;
        }
        finally { await ReleaseJobAsync(process); }
        return new ProcessResult(exitCode, await stdout, await stderr);
    }

    public static Process Start(ProcessSpec spec) => Start(spec, null);

    private static Process Start(ProcessSpec spec, string? windowsShellCommand)
    {
        var start = new ProcessStartInfo(spec.FileName)
        {
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = spec.CaptureOutput,
            RedirectStandardError = spec.CaptureOutput
        };
        if (windowsShellCommand is null)
            foreach (var argument in spec.Arguments) start.ArgumentList.Add(argument);
        else
            // cmd.exe parses a command string, not CRT argv quoting. ArgumentList
            // escapes embedded quotes with backslashes, which cmd treats literally.
            start.Arguments = "/d /s /c \"" + windowsShellCommand + "\"";
        foreach (var item in spec.Environment ?? new Dictionary<string, string?>())
        {
            if (item.Value is null) start.Environment.Remove(item.Key);
            else start.Environment[item.Key] = item.Value;
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start '{spec.FileName}'.");
        try
        {
            if (OperatingSystem.IsWindows() && !process.HasExited) Jobs.Add(process, WindowsProcessJob.Attach(process));
            return process;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public static async Task<int> WaitOwnedAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                await StopAsync(process);
            }
            throw;
        }
        finally
        {
            await ReleaseJobAsync(process);
            process.Dispose();
        }
    }

    public static async Task StopAsync(Process process)
    {
        // Closing the job also stops descendants whose intermediate parent has exited.
        var hadJob = await ReleaseJobAsync(process);
        if (!hadJob && !process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static async Task<bool> ReleaseJobAsync(Process process)
    {
        if (!Jobs.TryGetValue(process, out var job)) return false;
        Jobs.Remove(process);
        await WindowsProcessJob.StopAsync(job);
        return true;
    }

    public static Process StartShell(string command, string workingDirectory, IReadOnlyDictionary<string, string?> environment, bool captureOutput = false)
    {
        var windows = OperatingSystem.IsWindows();
        return Start(new ProcessSpec(
            windows ? "cmd.exe" : "/bin/sh",
            windows ? ["/d", "/s", "/c", command] : ["-c", command],
            workingDirectory,
            environment,
            captureOutput), windows ? command : null);
    }
}
