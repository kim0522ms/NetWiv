using NetWiv;
using NetWiv.Platform.Desktop;

internal static class ExitRequestSmoke
{
    internal static int Run()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .Window("main", "NetWiv cooperative exit", "quiet.html")
            .OnExitRequested(async (handle, request) =>
            {
                // Native operations must remain responsive during save/confirmation work.
                if (!await handle.GetWindow("main")!.IsVisibleAsync()) throw new InvalidOperationException("Exit request lost its window.");
                await Task.Delay(50, request.CancellationToken);
                if (Interlocked.Increment(ref requests) == 1) request.PreventDefault();
            })
            .Setup(app => { _ = VerifyAsync(app); }).Build();
        using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL cooperative exit timed out"); Environment.Exit(124); },
            null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        app.Run();
        completed.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Console.WriteLine("PASS cooperative exit waits for native async operations, honors veto and exits after approval");
        return 0;

        async Task VerifyAsync(App value)
        {
            try
            {
                if (await value.RequestExitAsync()) throw new InvalidOperationException("Vetoed exit was submitted.");
                if (!await value.Handle.GetWindow("main")!.IsVisibleAsync()) throw new InvalidOperationException("Vetoed exit stopped the native runtime.");
                if (!await value.RequestExitAsync()) throw new InvalidOperationException("Approved exit was not submitted.");
                completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); value.Handle.Exit(1); }
        }
    }
}
