using NetWiv;
using NetWiv.Plugin.SingleInstance;
using System.Diagnostics;

internal static class SingleInstancePluginTests
{
    internal static async Task ForwardsSecondLaunch()
    {
        var identifier = "dev.netwiv.single-instance-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<(IReadOnlyList<string> Args, string Cwd)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new Builder().UseRuntime(new FakeRuntime())
            .Metadata(new AppMetadata("First", "1.0.0", identifier))
            .Plugin(new SingleInstancePlugin((_, args, cwd) => received.TrySetResult((args, cwd)))).Build();
        first.Run();
        var secondRuntime = new FakeRuntime();
        using var second = new Builder().UseRuntime(secondRuntime)
            .Metadata(new AppMetadata("Second", "1.0.0", identifier))
            .Plugin(new SingleInstancePlugin(arguments: ["second.exe", "--open", "selected.txt"])).Build();
        second.Run();
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, secondRuntime.ExitCode);
        Assert.Equal("second.exe", message.Args[0]);
        Assert.Equal("selected.txt", message.Args[2]);
        Assert.Equal(Environment.CurrentDirectory, message.Cwd);
    }

    internal static async Task ForwardsAcrossProcesses()
    {
        var identifier = "dev.netwiv.single-process-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new Builder().UseRuntime(new FakeRuntime())
            .Metadata(new AppMetadata("First", "1.0.0", identifier))
            .Plugin(new SingleInstancePlugin((_, args, _) => received.TrySetResult(args))).Build();
        first.Run();
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        var entry = Environment.GetCommandLineArgs()[0];
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            "dotnet", StringComparison.OrdinalIgnoreCase) && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(entry);
        start.ArgumentList.Add("--single-instance-child");
        start.ArgumentList.Add(identifier);
        using var child = Process.Start(start) ?? throw new Exception("Could not start the second app process.");
        IReadOnlyList<string> forwarded;
        try { forwarded = await received.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            throw new Exception($"Second process exited {child.ExitCode} without forwarding: {await child.StandardError.ReadToEndAsync()}");
        }
        Assert.True(forwarded.Contains("--single-instance-child"), "The process arguments were not forwarded.");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, child.ExitCode);
    }
}
