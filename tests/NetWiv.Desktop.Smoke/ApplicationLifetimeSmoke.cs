using NetWiv;
using NetWiv.Platform.Desktop;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ApplicationLifetimeSmoke
{
    internal static int Run(bool resident)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .ExitOnLastWindowClose(!resident)
            .Window("main", "NetWiv application lifetime", "quiet.html")
            .Setup(app => { _ = VerifyAsync(app.Handle); }).Build();
        using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL application lifetime timed out"); Environment.Exit(124); },
            null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        app.Run();
        completed.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Console.WriteLine(resident
            ? "PASS resident application closes and destroys its last window, recreates windows and exits explicitly"
            : "PASS default application exits after closing its last window");
        return 0;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                if (OperatingSystem.IsWindows() && !resident) await NativeCloseAsync(handle);
                else await handle.GetWindow("main")!.CloseAsync();
                if (handle.GetWindow("main") is not null) throw new InvalidOperationException("Closed window remains in the registry.");
                if (resident)
                {
                    // Dispatch and creation must still work after the final window has gone.
                    await Task.Delay(100);
                    await new WindowBuilder(handle, "main").Title("NetWiv restored window").BuildAsync();
                    if (OperatingSystem.IsWindows())
                    {
                        await NativeCloseAsync(handle);
                        await new WindowBuilder(handle, "main").Title("NetWiv restored after native close").BuildAsync();
                    }
                    await handle.GetWindow("main")!.DestroyAsync();
                    if (handle.Windows.Count != 0) throw new InvalidOperationException("Destroyed window remains in the registry.");
                    await new WindowBuilder(handle, "restored").Title("NetWiv restored again").BuildAsync();
                    if (handle.GetWindow("restored") is null) throw new InvalidOperationException("Resident application did not recreate a window.");
                }
                completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { if (resident || completed.Task.IsFaulted) handle.Exit(completed.Task.IsFaulted ? 1 : 0); }
        }
    }

    private static async Task NativeCloseAsync(AppHandle handle)
    {
        await Task.Delay(25);
        using var process = Process.GetCurrentProcess();
        var window = process.MainWindowHandle;
        if (window == 0 || !PostMessage(window, 0x0010, 0, 0)) // WM_CLOSE to this test process only.
            throw new InvalidOperationException("Could not close the test-owned native window.");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (handle.GetWindow("main") is not null)
        {
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("Native close did not remove the test window.");
            await Task.Delay(25);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
}
