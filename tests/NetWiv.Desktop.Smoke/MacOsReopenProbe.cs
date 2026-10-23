using System.Diagnostics;
using System.Runtime.InteropServices;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class MacOsReopenProbe
{
    internal static int Run()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This probe requires macOS.");
        var result = 1;
        var events = System.Threading.Channels.Channel.CreateUnbounded<bool>();
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreOnReopen = false;
        var uiThread = 0;
        var runtime = new DesktopRuntime();
        using var app = new Builder().UseRuntime(runtime).UseEmbeddedAssets()
            .Window("main", "NetWiv reopen probe", "quiet.html")
            .OnReopen(async (handle, value) =>
            {
                if (Environment.CurrentManagedThreadId != uiThread)
                {
                    events.Writer.TryComplete(new InvalidOperationException("Reopen must arrive on the native event-loop thread."));
                    return;
                }
                events.Writer.TryWrite(value.HasVisibleWindows);
                if (!restoreOnReopen) return;
                var window = handle.GetWindow("main")!;
                await window.ShowAsync();
                await window.UnminimizeAsync();
                await window.SetFocusAsync();
                restored.TrySetResult();
            })
            .Setup(app =>
            {
                uiThread = Environment.CurrentManagedThreadId;
                Console.WriteLine("Reopen probe: native runtime ready");
                app.Handle.EventHandlerFailed += (_, error) => restored.TrySetException(error);
                app.GetWindow("main")!.OnCloseRequested(async value =>
                {
                    value.PreventDefault();
                    await app.GetWindow("main")!.HideAsync();
                    hidden.TrySetResult();
                });
                _ = VerifyAsync(app.Handle);
            }).Build();
        using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL reopen probe timed out"); Environment.Exit(124); }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        Console.WriteLine("Reopen probe: starting native runtime");
        app.Run();
        return result;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                var window = handle.GetWindow("main")!;
                await window.CloseAsync();
                await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Console.WriteLine("Reopen probe: close prevented and window hidden");
                if (await window.IsVisibleAsync()) throw new InvalidOperationException("Close prevention did not hide the window.");
                for (int i = 0; i < 3; i++)
                {
                    await SendReopenAsync();
                    Console.WriteLine("Reopen probe: AppleEvent submitted");
                    if (await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
                        throw new InvalidOperationException("Hidden window was reported as visible.");
                    if (await window.IsVisibleAsync()) throw new InvalidOperationException("Reopen restored a window without app policy.");
                }
                restoreOnReopen = true;
                await SendReopenAsync();
                if (await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Hidden reopen payload changed.");
                await restored.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (!await window.IsVisibleAsync()) throw new InvalidOperationException("App reopen handler failed to restore the window.");
                restoreOnReopen = false;
                await SendReopenAsync();
                if (!await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Visible window was reported as hidden.");
                await window.MinimizeAsync();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (!await window.IsMinimizedAsync())
                {
                    if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("Window did not minimize.");
                    await Task.Delay(25);
                }
                await SendReopenAsync();
                if (!await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("AppKit must count minimized windows as visible.");
                await window.UnminimizeAsync();
                Console.WriteLine("PASS macOS reopen AppleEvent delivery, repeated hidden reopen, close prevention, app-owned restore and visible/minimized payloads");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + error.Message); }
            finally { handle.Exit(result); }
        }
    }

    private static async Task SendReopenAsync()
    {
        // A separate sender makes AppKit dispatch on its main thread, as Dock/Finder do.
        // Same-process AESendMessage can instead invoke AppKit inline on the calling thread.
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet")
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        start.ArgumentList.Add("--send-reopen-event");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var sender = Process.Start(start) ?? throw new InvalidOperationException("Could not start reopen sender.");
        try
        {
            await sender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (sender.ExitCode != 0) throw new InvalidOperationException("Sending reopen AppleEvent failed.");
        }
        finally { if (!sender.HasExited) sender.Kill(entireProcessTree: true); }
    }

    internal static int SendReopen(int processId)
        => SendActivation(processId, opened: false);

    internal static int SendOpened(int processId)
        => SendActivation(processId, opened: true);

    private static int SendActivation(int processId, bool opened)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This probe requires macOS.");
        var foundation = NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
        try
        {
            var pool = Send(Send(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
            try
            {
                var type = GetClass("NSAppleEventDescriptor");
                var target = ProcessDescriptor(type, Selector("descriptorWithProcessIdentifier:"), processId);
                var descriptor = CreateEvent(type, Selector("appleEventWithEventClass:eventID:targetDescriptor:returnID:transactionID:"),
                    opened ? 0x4755524cu : 0x61657674u, opened ? 0x4755524cu : 0x72617070u, target, -1, 0); // 'GURL'/'GURL' or 'aevt'/'rapp'
                if (opened)
                {
                    var url = SendString(GetClass("NSString"), Selector("stringWithUTF8String:"), "netwiv-test://opened/path");
                    var value = SendArgument(type, Selector("descriptorWithString:"), url);
                    SetParameter(descriptor, Selector("setParamDescriptor:forKeyword:"), value, 0x2d2d2d2d); // '----'
                }
                SendEvent(descriptor, Selector("sendEventWithOptions:timeout:error:"), 1, 10, out var error);
                if (error != 0) Console.Error.WriteLine($"AppleEvent send failed with error code {(long)Send(error, Selector("code"))}.");
                return error == 0 ? 0 : 1;
            }
            finally { Send(pool, Selector("drain")); }
        }
        finally { NativeLibrary.Free(foundation); }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern nint GetClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern nint Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendArgument(nint receiver, nint selector, nint argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetParameter(nint receiver, nint selector, nint descriptor, uint keyword);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ProcessDescriptor(nint receiver, nint selector, int processId);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint CreateEvent(nint receiver, nint selector, uint eventClass, uint eventId, nint target, short returnId, int transactionId);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendEvent(nint receiver, nint selector, nuint options, double timeout, out nint error);
}
