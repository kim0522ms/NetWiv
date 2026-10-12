using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class WindowsLifecycleProbe
{
    // Send only to this test process: never restart Explorer or broadcast to
    // other applications. Concurrent TaskbarCreated delivery reproduces TAO's
    // window-state lock being re-entered through ITaskbarList.AddTab.
    internal static async Task VerifyTaskbarRecreationAsync()
    {
        using var process = Process.GetCurrentProcess();
        nint window = process.MainWindowHandle;
        if (window == 0) throw new InvalidOperationException("Taskbar regression requires a visible native window.");
        uint message = RegisterWindowMessage("TaskbarCreated");
        if (message == 0) throw new InvalidOperationException("Could not register TaskbarCreated.");
        using var start = new Barrier(2);
        void Deliver()
        {
            start.SignalAndWait();
            for (int i = 0; i < 200; i++)
                if (SendMessageTimeout(window, message, 0, 0, 2, 2000, out _) == 0)
                    throw new InvalidOperationException("Native window stopped responding during TaskbarCreated.");
        }
        await Task.WhenAll(
            Task.Factory.StartNew(Deliver, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default),
            Task.Factory.StartNew(Deliver, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        if (SendMessageTimeout(window, 0, 0, 0, 2, 2000, out _) == 0)
            throw new InvalidOperationException("Native window stopped responding after TaskbarCreated.");
        Console.WriteLine("PASS concurrent TaskbarCreated delivery and native window responsiveness");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam,
        uint flags, uint timeout, out nuint result);
}
