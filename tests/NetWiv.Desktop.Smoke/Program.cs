using System.Collections.Concurrent;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.Stronghold;
using NetWiv.Plugin.GlobalShortcut;
using NetWiv.Runtime;

internal static class Program
{
    private static readonly ConcurrentDictionary<string, byte> Ready = new();
    private static int _scenarioStarted;
    private static int _failed;
    private static int _readyCount;
    private static int _emptyVerified;
    private static int _sensitiveCalls;
    private static ulong _pendingDocument;
    private static readonly TaskCompletionSource<JsonElement> PendingReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource ReloadComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Channel? _pendingChannel;
    private static readonly int GuiHoldMs = int.TryParse(Environment.GetEnvironmentVariable("NETWIV_SMOKE_HOLD_MS"), out var duration)
        ? Math.Clamp(duration, 0, 600000) : 0;
    private static readonly TaskCompletionSource GuiComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly ImageData SmokeIcon = CreateIcon();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--window-state-smoke", var stateRoot, var phase]) return WindowStateSmoke.Run(stateRoot, phase == "restore");
        if (args is ["--expect-startup-error", var expected]) return CheckStartupError(expected);
        if (args is ["--no-window-smoke"]) return CheckNoWindows();
        if (args.Length != 0) throw new ArgumentException("Unknown smoke-test arguments.");
        using var probe = new OriginProbe();
        using var asset = new SmokeAsset();
        var runtime = new DesktopRuntime();
        var builder = new Builder().UseRuntime(runtime).DefaultWindowIcon(SmokeIcon)
            .Plugin(new StrongholdPlugin())
            .Plugin(new GlobalShortcutPlugin())
            .Plugin(new GeneratedCommandPlugin())
            .Capability(new("generated-commands", ["generated-external:allow-status", "generated-local:allow-status", "generated-local:allow-blocked", "generated-local:deny-blocked"], Windows: ["main", "secondary"]))
            .Capability(new("runtime-identity", ["core:app:allow-tauri-version"], Windows: ["main", "secondary"]))
            .OnMenuEvent((_, value) => Console.WriteLine($"MENU {value.Id}"))
            .OnTrayIconEvent((_, value) => Console.WriteLine($"TRAY {value.Type} {value.Id}"))
            .Setup(app => app.Handle.Dispatch(() =>
            {
                Interlocked.Increment(ref _readyCount);
                Console.WriteLine("PASS application setup on native UI thread");
                Console.WriteLine($"BACKEND {runtime.Backend}");
            }))
            .UseEmbeddedAssets()
            .UseEmbeddedConfig()
            .Window(new WindowOptions("main", "NetWiv desktop — main + child", "index.html", 860, 540, default,
                [new("main", "index.html", 0, 0, 430, 500, default), new("child", "index.html", 430, 0, 430, 500, default)]))
            .Window("secondary", "NetWiv desktop — secondary", "index.html", 440, 420, JsonValue.Parse("{\"useHttpsScheme\":true}"))
            .RegisterCommand("asset_path", (_, _, _) => Value(JsonValue.From(asset.Path)))
            .RegisterCommand("probe_url", (_, _, _) => Value(JsonValue.From(probe.Url)))
            .RegisterCommand("finish_gui", (_, _, _) => { GuiComplete.TrySetResult(); return Value(JsonValue.Null); })
            .RegisterCommand("pending_reload", (context, args, _) =>
            {
                _pendingDocument = context.DocumentId;
                _pendingChannel = Channel.FromJson(context, args.GetProperty("channel"));
                return new ValueTask<JsonElement>(PendingReply.Task);
            })
            .RegisterCommand("reload_barrier", (_, _, _) =>
            {
                Require(_pendingDocument != 0, "native document generation is missing");
                return Value(JsonValue.Null);
            })
            .RegisterCommand("release_reload", (context, _, _) =>
            {
                Require(context.DocumentId != 0 && context.DocumentId != _pendingDocument, "reload reused native document generation");
                _pendingChannel!.Send(JsonValue.From("old document"));
                _pendingChannel.Dispose();
                PendingReply.SetResult(JsonValue.From("old document"));
                return Value(JsonValue.Null);
            })
            .RegisterCommand("reload_complete", (_, args, _) =>
            {
                Require(!args.GetProperty("stale").GetBoolean(), "old callback reached the replacement document");
                ReloadComplete.TrySetResult();
                return Value(JsonValue.Null);
            })
            .RegisterCommand("sensitive", (_, _, _) =>
            {
                Interlocked.Increment(ref _sensitiveCalls);
                return Value(JsonValue.From("sensitive"));
            })
            .RegisterCommand("echo", (_, args, _) => Value(args.GetProperty("input").Clone()))
            .RegisterCommand("greet", (_, args, _) => Value(JsonValue.From($"Hello {args.GetProperty("name").GetString()} from C#")))
            .RegisterCommand("stream", (context, args, _) =>
            {
                using var channel = Channel.FromJson(context, args.GetProperty("channel"));
                for (int value = 0; value < 3; value++) channel.Send(JsonValue.From(value));
                return Value(JsonValue.Null);
            })
            .RegisterCommand("failed", (context, args, _) =>
            {
                Fail(args.GetProperty("error").GetString()!);
                context.App.Exit(1);
                return Value(JsonValue.Null);
            })
            .RegisterCommand("ready", (context, args, cancellationToken) =>
            {
                string label = args.GetProperty("label").GetString()!;
                if (label != context.WebviewLabel) throw new InvokeException("Native IPC label does not match the document.");
                Ready[label] = 0;
                Console.WriteLine($"PASS guest {label} including scoped file asset");
                if (Ready.Count == 3 && Interlocked.Exchange(ref _scenarioStarted, 1) == 0)
                    _ = RunScenario(context.App, probe);
                return Value(JsonValue.Null);
            });
        using var app = builder.Build();
        using var watchdog = new Timer(_ =>
        {
            Fail("Native integration timed out");
            try { app.Handle.Exit(124); } catch (Exception error) { Console.Error.WriteLine(error.Message); }
        }, null, TimeSpan.FromMilliseconds(180000 + GuiHoldMs), Timeout.InfiniteTimeSpan);
        try { app.Run(); }
        catch (Exception error) { Fail(error.ToString()); }
        return _failed == 0 && _scenarioStarted == 1 && _readyCount == 1 ? 0 : 1;
    }

    private static async Task RunScenario(AppHandle app, OriginProbe probe)
    {
        try
        {
            var title = await app.GetWindow("main")!.TitleAsync();
            Require(title == "NetWiv desktop — main + child", "native window title");
            await app.InvokeAsync("plugin:window|create", JsonValue.Parse("{\"options\":{\"label\":\"identity-title\",\"visible\":false}}"));
            var identityWindow = (await app.GetWindowAsync("identity-title"))!;
            Require(await identityWindow.TitleAsync() == "NetWiv App", "dynamic default window title");
            await identityWindow.DestroyAsync();
            Console.WriteLine("PASS NetWiv default window title on native runtime");
            var image = await Image.NewAsync(app, SmokeIcon.Rgba, SmokeIcon.Width, SmokeIcon.Height);
            var item = await CheckMenuItem.NewAsync(app, new("Verified", Id: "verified", Checked: true));
            var iconItem = await IconMenuItem.NewAsync(app, new("Icon item", Id: "icon-item", Icon: image));
            var about = await PredefinedMenuItem.NewAsync(app, new(PredefinedMenuItemType.About, "About NetWiv",
                new AboutMetadata(Name: "NetWiv smoke", Version: "0.1.0", Authors: ["NetWiv contributors"], Icon: image)));
            var submenu = await Submenu.NewAsync(app, new("NetWiv", Id: "smoke-submenu", Items: [item, iconItem, about]));
            var menu = await Menu.NewAsync(app, new("smoke-menu", [submenu]));
            await menu.SetAsWindowMenuAsync(app.GetWindow("main")!);
            Require((await menu.ItemsAsync()).Count == 1, "native menu item count");
            await item.SetCheckedAsync(false);
            Require(!await item.IsCheckedAsync(), "native menu mutation");
            Require((await submenu.ItemsAsync()).OfType<IconMenuItem>().Single().Id == iconItem.Id, "native icon item materialization");
            await iconItem.SetIconAsync(null);
            await iconItem.SetIconAsync(image);
            Require(await about.TextAsync() == "About NetWiv", "native About item text");
            var tray = await TrayIcon.NewAsync(app, new(Id: "netwiv-desktop-smoke", Menu: menu, Icon: image, Tooltip: "NetWiv desktop integration"));
            await tray.SetVisibleAsync(false); await tray.SetVisibleAsync(true);
            Require(await TrayIcon.GetByIdAsync(app, tray.Id) is not null, "native tray lookup");
            var views = await app.InvokeAsync("plugin:webview|get_all_webviews", JsonValue.EmptyObject);
            Require(views.GetArrayLength() == 3, "three native webviews");
            Console.WriteLine("PASS native windows, menu mutation, icon/About SDK, image and tray");
            await VerifyStrongholdAndShortcutBridge(app);
            await VerifyDesktopApi(app, image);
            for (int iteration = 0; iteration < 10; iteration++)
            {
                var temporary = await new WindowBuilder(app, "temporary").Visible(false).BuildAsync();
                Require((await app.InvokeAsync("plugin:webview|get_all_webviews", JsonValue.EmptyObject)).GetArrayLength() == 3,
                    "bare window must not create an implicit webview");
                var temporaryView = await new WebviewBuilder(app, temporary, "temporary").Url("quiet.html").AutoResize().BuildAsync();
                await temporaryView.CloseAsync();
                await temporary.DestroyAsync();
                Require(app.Webviews.All(view => view.Label != "temporary"), "closed webview remains in Core registry");
                Require(app.WebviewWindows.All(window => window.Label != "temporary"), "destroyed window remains in Core registry");
            }
            Console.WriteLine("PASS repeated bare window and child webview creation and cleanup");
            var reload = await new WebviewBuilder(app, app.GetWindow("main")!, "reload").Url("reload.html").BuildAsync();
            await ReloadComplete.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await reload.CloseAsync();
            Console.WriteLine("PASS reload invalidates pending command and Channel callbacks");
            if (GuiHoldMs > 0)
            {
                Console.WriteLine("READY integrated GUI inspection");
                await Task.WhenAny(Task.Delay(GuiHoldMs), GuiComplete.Task);
                Console.WriteLine($"GUI menu checked: {await item.IsCheckedAsync()}");
            }
            app.GetWebview("secondary")!.EvaluateScript($"location.href={JsonValue.From(probe.Url + "/top").GetRawText()};");
            Require(await probe.RemoteDenied.Task.WaitAsync(TimeSpan.FromSeconds(10)), "remote document default command denial");
            Require(_sensitiveCalls == 0, "remote frame or document invoked a local command");
            Console.WriteLine("PASS remote iframe isolation and navigation origin denial");
            await TrayIcon.RemoveByIdAsync(app, tray.Id);
            await image.CloseAsync(); await menu.CloseAsync(); await submenu.CloseAsync(); await item.CloseAsync();
            await iconItem.CloseAsync(); await about.CloseAsync();
            Console.WriteLine("PASS resource cleanup");
            app.Exit();
        }
        catch (Exception error) { Fail(error.ToString()); app.Exit(1); }
    }

    private static async Task VerifyStrongholdAndShortcutBridge(AppHandle app)
    {
        var shortcut = await app.InvokeAsync("plugin:global-shortcut|is_registered",
            JsonValue.Parse("""{"shortcut":"Ctrl+Alt+Shift+F19"}"""));
        Require(!shortcut.GetBoolean(), "unregistered native shortcut was reported as registered");
        var directory = Directory.CreateTempSubdirectory("netwiv-stronghold-smoke-");
        try
        {
            var path = Path.Combine(directory.FullName, "test.hold");
            var encoded = JsonValue.From(path).GetRawText();
            var initialize = JsonValue.Parse($$"""{"snapshotPath":{{encoded}},"password":"test-secret"}""");
            var client = JsonValue.Parse($$"""{"snapshotPath":{{encoded}},"client":"app"}""");
            var record = JsonValue.Parse($$"""{"snapshotPath":{{encoded}},"client":"app","key":"token","value":[2,4,6]}""");
            await app.InvokeAsync("plugin:stronghold|initialize", initialize);
            await app.InvokeAsync("plugin:stronghold|create_client", client);
            await app.InvokeAsync("plugin:stronghold|save_store_record", record);
            await app.InvokeAsync("plugin:stronghold|save", initialize);
            await app.InvokeAsync("plugin:stronghold|destroy", initialize);
            await app.InvokeAsync("plugin:stronghold|initialize", initialize);
            await app.InvokeAsync("plugin:stronghold|load_client", client);
            var result = await app.InvokeAsync("plugin:stronghold|get_store_record", record);
            Require(result.GetArrayLength() == 3 && result[0].GetInt32() == 2 && result[2].GetInt32() == 6,
                "native Stronghold snapshot did not roundtrip through the C ABI");
            await app.InvokeAsync("plugin:stronghold|destroy", initialize);
            Console.WriteLine("PASS Stronghold snapshot across C# IPC and native ABI; shortcut query");
        }
        finally { directory.Delete(true); }
    }

    private static async Task VerifyDesktopApi(AppHandle app, Image image)
    {
        var main = app.GetWindow("main")!;
        if (OperatingSystem.IsMacOS())
        {
            await main.SetTitleBarStyleAsync(TitleBarStyle.Overlay);
            await main.SetTitleBarStyleAsync(TitleBarStyle.Visible);
            await main.SetShadowAsync(false); await main.SetShadowAsync(true);
            await main.SetBadgeCountAsync(3); await main.SetBadgeLabelAsync(null);
            await main.SetEffectsAsync(new Effects([Effect.Sidebar], EffectState.Active, 4));
            await main.ClearEffectsAsync();
            await AppApi.SetDockVisibilityAsync(app, false); await AppApi.SetDockVisibilityAsync(app, true);
            await AppApi.HideAsync(app); await AppApi.ShowAsync(app);
            Require(await PathApi.AppLogDirectoryAsync(app) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", app.Metadata.Identifier), "macOS log directory");
            try { await main.SetSkipTaskbarAsync(true); throw new Exception("Unsupported per-window Dock operation succeeded."); }
            catch (InvokeException) { }
            try { await AppApi.FetchDataStoreIdentifiersAsync(app); throw new Exception("Unimplemented data store enumeration succeeded."); }
            catch (InvokeException) { }
            Console.WriteLine("PASS macOS title bar, effects, Dock, app visibility, paths and unsupported-operation errors");
        }
        Require((await app.GetAllWindowsAsync()).Count == 2, "live window list");
        Require((await app.GetAllWebviewsAsync()).Single(view => view.Label == "child").Window.Label == "main", "live child parent");
        Require(await app.GetWindowAsync("missing") is null, "missing native lookup must be null");
        var child = app.GetWebview("child")!;
        var childCopy = (await app.GetWebviewAsync("child"))!;
        var destination = await new WindowBuilder(app, "reparent-target").Visible(false).BuildAsync();
        await child.ReparentAsync(destination.Label);
        Require(child.Window.Label == destination.Label && childCopy.Window.Label == destination.Label, "retained SDK handles kept the old parent");
        Require(app.GetWebviewWindow("main") is not null, "single same-label WebView was not recognized as a combined window");
        await child.ReparentAsync("main");
        Require(child.Window.Label == "main" && childCopy.Window.Label == "main" && app.GetWebviewWindow("main") is null, "returning child did not update parent and combined-window membership");
        await destination.DestroyAsync();
        Console.WriteLine("PASS native reparent updates retained SDK handles and combined-window membership");
        var monitors = await main.AvailableMonitorsAsync();
        Require(monitors.Count > 0 && monitors.All(monitor => monitor.Size.Width > 0 && monitor.ScaleFactor > 0), "native monitor geometry");
        _ = await main.CurrentMonitorAsync();
        _ = await main.PrimaryMonitorAsync();
        _ = await main.MonitorFromPointAsync(0, 0);
        var cursor = await main.CursorPositionAsync();
        Require(double.IsFinite(cursor.X) && double.IsFinite(cursor.Y), "native cursor coordinates");
        await main.SetIconAsync(image);
        await app.GetWebviewWindow("secondary")!.SetBackgroundColorAsync(new RgbaColor(12, 24, 36));
        await app.GetWebviewWindow("secondary")!.SetBackgroundColorAsync(null);
        var window = await new WindowBuilder(app, "managed-close").Visible(false).BuildAsync();
        var prevented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (window.OnCloseRequested(request => { request.PreventDefault(); prevented.TrySetResult(); }))
        {
            await window.CloseAsync();
            await prevented.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(await app.GetWindowAsync(window.Label) is not null, "C# PreventDefault did not retain native window");
        }
        var destroyed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ReportFailure(AppEvent value, Exception error) { if (value.Label == window.Label) destroyed.TrySetException(error); }
        app.EventHandlerFailed += ReportFailure;
        try
        {
            using var onDestroyed = window.Once("tauri://destroyed", _ => destroyed.TrySetResult());
            using var onClose = window.OnCloseRequested(_ => Task.CompletedTask);
            await window.CloseAsync();
            await destroyed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(await app.GetWindowAsync(window.Label) is null, "C# close handler did not destroy native window");
        }
        finally { app.EventHandlerFailed -= ReportFailure; }
        Console.WriteLine("PASS C# native queries, icons, background and async close lifecycle");
    }

    private static int CheckNoWindows()
    {
        using var app = new Builder().UseRuntime(new DesktopRuntime())
            .UseEmbeddedConfig(assembly: typeof(Program).Assembly, resourceName: "netwiv.no-windows.json")
            .Setup(value => _ = CheckEmptyRuntime(value.Handle)).Build();
        using var watchdog = new Timer(_ =>
        {
            Fail("windowless startup timed out");
            try { app.Handle.Exit(124); } catch (Exception error) { Console.Error.WriteLine(error.Message); }
        }, null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
        try { app.Run(); }
        catch (Exception error) { Fail(error.ToString()); }
        return _failed == 0 && Volatile.Read(ref _emptyVerified) == 1 ? 0 : 1;
    }

    private static async Task CheckEmptyRuntime(AppHandle app)
    {
        try
        {
            Require((await app.GetAllWindowsAsync()).Count == 0, "empty config created a native window");
            Require((await app.GetAllWebviewsAsync()).Count == 0, "empty config created a native WebView");
            var image = await Image.NewAsync(app, SmokeIcon.Rgba, SmokeIcon.Width, SmokeIcon.Height);
            var menu = await Menu.NewAsync(app, new("windowless-menu", []));
            var tray = await TrayIcon.NewAsync(app, new(Id: "netwiv-windowless", Menu: menu, Icon: image));
            Require(await TrayIcon.GetByIdAsync(app, tray.Id) is not null, "windowless tray lookup failed");
            await tray.SetVisibleAsync(false);
            await TrayIcon.RemoveByIdAsync(app, tray.Id);
            await menu.CloseAsync();
            await image.CloseAsync();
            Require((await app.GetAllWindowsAsync()).Count == 0, "tray-only app created an implicit window");
            Interlocked.Exchange(ref _emptyVerified, 1);
            Console.WriteLine("PASS windowless native startup, menu/tray resources, task dispatch and shutdown");
            app.Exit();
        }
        catch (Exception error) { Fail(error.ToString()); app.Exit(1); }
    }

    private static int CheckStartupError(string expected)
    {
        using var app = new Builder().UseRuntime(new DesktopRuntime()).Build();
        try { app.Run(); }
        catch (InvalidOperationException exception) when (exception.Message.Contains(expected, StringComparison.Ordinal))
        {
            Console.WriteLine($"PASS startup diagnostic: {exception.Message}");
            return 0;
        }
        Console.Error.WriteLine("FAIL expected startup failure was not reported.");
        return 1;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static ImageData CreateIcon()
    {
        var rgba = new byte[16 * 16 * 4];
        for (int index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = 10; rgba[index + 1] = 150; rgba[index + 2] = 90; rgba[index + 3] = 255;
        }
        return new(rgba, 16, 16);
    }
    private static void Fail(string message) { Interlocked.Exchange(ref _failed, 1); Console.Error.WriteLine($"FAIL {message}"); }
    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
}
