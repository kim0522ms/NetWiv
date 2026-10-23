using System.Diagnostics;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.DeepLink;

internal static class MacOsOpenedProbe
{
    internal static int Run()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This probe requires macOS.");
        var events = System.Threading.Channels.Channel.CreateUnbounded<OpenedEvent>();
        var links = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = 0;
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .UseEmbeddedConfig(resourceName: "netwiv.opened.json").ExitOnLastWindowClose(false)
            .Plugin(new DeepLinkPlugin(["app"]))
            .Window("main", "NetWiv URL activation", "quiet.html")
            .OnOpened((_, value) =>
            {
                if (Environment.CurrentManagedThreadId != uiThread) throw new InvalidOperationException("Opened must arrive on the native event-loop thread.");
                events.Writer.TryWrite(value);
            })
            .Setup(app =>
            {
                uiThread = Environment.CurrentManagedThreadId;
                app.Handle.EventHandlerFailed += (_, error) => completed.TrySetException(error);
                app.Handle.Events.Listen("deep-link://new-url", value => links.Writer.TryWrite(value.Payload[0].GetString()!));
                _ = VerifyAsync(app.Handle);
            }).Build();
        using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL URL activation timed out"); Environment.Exit(124); },
            null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        app.Run();
        completed.Task.GetAwaiter().GetResult();
        Console.WriteLine("PASS macOS native URL activation while open and resident, typed delivery and deep-link current URL");
        return 0;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                // Setup precedes run_return. Wait for a native roundtrip so a fast
                // NativeAOT sender cannot target an app that AppKit has not launched yet.
                if (!await handle.GetWindow("main")!.IsVisibleAsync()) throw new InvalidOperationException("URL activation window is not visible.");
                for (var i = 0; i < 2; i++)
                {
                    if (i == 1) await handle.GetWindow("main")!.DestroyAsync();
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                    if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
                    start.ArgumentList.Add("--send-opened-event");
                    start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    using var sender = Process.Start(start) ?? throw new InvalidOperationException("Could not start URL activation sender.");
                    try
                    {
                        await sender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        if (sender.ExitCode != 0) throw new InvalidOperationException("Sending URL activation failed.");
                    }
                    finally { if (!sender.HasExited) sender.Kill(entireProcessTree: true); }
                    var opened = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                    if (opened.Urls.Count != 1 || opened.Urls[0].AbsoluteUri != "netwiv-test://opened/path") throw new InvalidOperationException("Native URL activation payload changed.");
                    if (await links.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)) != "netwiv-test://opened/path") throw new InvalidOperationException("Deep-link URL activation was not delivered.");
                    var current = await handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
                    if (current[0].GetString() != "netwiv-test://opened/path") throw new InvalidOperationException("Deep-link current URL was not updated.");
                }
                await new WindowBuilder(handle, "restored").Title("NetWiv activation restore").BuildAsync();
                completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { handle.Exit(completed.Task.IsFaulted ? 1 : 0); }
        }
    }
}
