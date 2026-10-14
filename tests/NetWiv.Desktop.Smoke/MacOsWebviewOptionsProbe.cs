using System.Runtime.InteropServices;
using NetWiv;
using NetWiv.Platform.Desktop;

// Functional assertions against live AppKit/WebKit objects, never configuration echoes.
internal static partial class MacOsWebviewOptionsProbe
{
    private sealed record Options(string Name, string Json, bool FirstMouse, bool LinkPreview, nint? Scheduling);
    private static readonly Options[] Cases = [
        new("default", "{}", false, true, null),
        new("disabled", """{"acceptFirstMouse":false,"allowLinkPreview":false,"backgroundThrottling":"disabled"}""", false, false, 2),
        new("suspend", """{"acceptFirstMouse":true,"allowLinkPreview":true,"backgroundThrottling":"suspend"}""", true, true, 0),
        new("throttle", """{"acceptFirstMouse":true,"allowLinkPreview":false,"backgroundThrottling":"throttle"}""", true, false, 1)
    ];

    internal static int Run(bool input = false)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This probe requires macOS.");
        var result = 1;
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .Window("default", "NetWiv WebView default", "webview-options.html", 480, 320)
            .Window("first-mouse-off", "NetWiv first mouse OFF", "webview-options.html", 480, 320,
                JsonValue.Parse(Cases[1].Json))
            .Window("first-mouse-on", "NetWiv first mouse ON", "webview-options.html", 480, 320,
                JsonValue.Parse(Cases[2].Json))
            .Setup(value => _ = VerifyAsync(value.Handle)).Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromMinutes(3), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                await CheckAsync(handle, "NetWiv WebView default", Cases[0]);
                await CheckAsync(handle, "NetWiv first mouse OFF", Cases[1]);
                await CheckAsync(handle, "NetWiv first mouse ON", Cases[2]);
                foreach (var options in Cases)
                {
                    foreach (var child in new[] { false, true })
                    {
                        var label = "options-" + options.Name + (child ? "-child" : "-root");
                        var title = "NetWiv " + label;
                        try
                        {
                            if (child)
                                await new WindowBuilder(handle, label).Title(title).Visible(false).BuildAsync();
                            var extra = options.Json[1..^1];
                            var arguments = JsonValue.Parse($$"""{"options":{"label":"{{label}}","title":"{{title}}","url":"quiet.html","visible":false,"width":400,"height":280{{(extra.Length == 0 ? "" : "," + extra)}}}{{(child ? ",\"windowLabel\":\"" + label + "\"" : "")}}}""");
                            await handle.InvokeAsync(child ? "plugin:webview|create_webview" : "plugin:webview|create_webview_window", arguments);
                            await CheckAsync(handle, title, options);
                        }
                        finally
                        {
                            if (await handle.GetWindowAsync(label) is { } window) await window.DestroyAsync();
                        }
                    }
                }
                Console.WriteLine("PASS macOS WebView live first-mouse/link-preview defaults and on/off states, root and child creation");
                Console.WriteLine(OperatingSystem.IsMacOSVersionAtLeast(14)
                    ? "PASS macOS WebView live disabled/suspend/throttle scheduling policies"
                    : "SKIP background scheduling policy: macOS 14+ required; older-system creation checked");
                if (input)
                {
                    await handle.GetWindow("default")!.HideAsync();
                    await handle.GetWindow("first-mouse-off")!.SetPositionAsync(new LogicalPosition(80, 140));
                    await handle.GetWindow("first-mouse-on")!.SetPositionAsync(new LogicalPosition(580, 140));
                    Console.WriteLine("INPUT check: activate ON, click OFF button once (0), twice (1); activate OFF, click ON once (1)");
                    Console.WriteLine("INPUT link-preview gesture must be checked separately; automatic PASS lines cover native state only");
                    await Task.Delay(TimeSpan.FromSeconds(120));
                }
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally { handle.Exit(result); }
        }
    }

    private static Task CheckAsync(AppHandle app, string title, Options options)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Dispatch(() =>
        {
            try
            {
                var application = Send(GetClass("NSApplication"), Selector("sharedApplication"));
                var windows = Send(application, Selector("windows"));
                nint view = 0;
                for (nint index = 0; index < Send(windows, Selector("count")); index++)
                {
                    var window = SendArgument(windows, Selector("objectAtIndex:"), index);
                    var nativeTitle = Send(Send(window, Selector("title")), Selector("UTF8String"));
                    if (Marshal.PtrToStringUTF8(nativeTitle) == title)
                        view = FindWebview(Send(window, Selector("contentView")));
                }
                Require(view != 0, "Test window has no WKWebView");
                Require(SendBoolArgument(view, Selector("acceptsFirstMouse:"), 0) != 0 == options.FirstMouse,
                    "WKWebView first-mouse response differs from requested option");
                Require(SendBool(view, Selector("allowsLinkPreview")) != 0 == options.LinkPreview,
                    "WKWebView link-preview state differs from requested option");
                if (OperatingSystem.IsMacOSVersionAtLeast(14) && options.Scheduling is { } expected)
                {
                    var preferences = Send(Send(view, Selector("configuration")), Selector("preferences"));
                    Require(Send(preferences, Selector("inactiveSchedulingPolicy")) == expected,
                        "WKPreferences inactive scheduling policy differs from requested option");
                }
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static nint FindWebview(nint view)
    {
        if (SendBoolArgument(view, Selector("isKindOfClass:"), GetClass("WKWebView")) != 0) return view;
        var children = Send(view, Selector("subviews"));
        for (nint index = 0; index < Send(children, Selector("count")); index++)
        {
            var found = FindWebview(SendArgument(children, Selector("objectAtIndex:"), index));
            if (found != 0) return found;
        }
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendArgument(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SendBool(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SendBoolArgument(nint receiver, nint selector, nint argument);
}
