using System.Collections.Concurrent;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Linux;
using NetWiv.Runtime;

namespace NetWiv.Linux.Smoke;

internal static class Program
{
    private static readonly ConcurrentDictionary<string, byte> ReadyLabels =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> EvaluatedLabels =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> Failures = new();
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private static int _assetRequestCount;
    private static int _destroyedCount;
    private static int _closeRequestedCount;
    private static int _menuActivated;
    private static int _trayMenuActivated;
    private static int _verificationStarted;

    private static int Main()
    {
        LinuxDesktopRuntime runtime = new();
        int configuredHoldMilliseconds = GetHoldMilliseconds();
        using CancellationTokenSource watchdog = new(
            TimeSpan.FromMilliseconds(30_000 + configuredHoldMilliseconds));
        watchdog.Token.Register(() =>
        {
            Failures.Enqueue("Timed out waiting for both WebKitGTK windows.");
            TryExit(runtime, 124);
        });

        RuntimeContext context = new(
            [
                new WindowOptions(
                    "main",
                    "NetWiv Linux main",
                    "tauri://localhost/main",
                    640,
                    420,
                    EmptyObject,
                    [
                        new WebviewOptions("left", "left", 0, 0, 320, 420, EmptyObject),
                        new WebviewOptions("right", "right", 320, 0, 320, 420, EmptyObject),
                    ]),
                new WindowOptions("secondary", "NetWiv Linux secondary", "tauri://localhost/secondary", 480, 320, EmptyObject),
            ],
            "globalThis.__netwivDocumentStart = document.readyState === 'loading';" +
            "globalThis.__TAURI_INTERNALS__={ipc:message=>globalThis.__NETWIV_IPC__.postMessage(message)," +
            "runCallback:(id,data)=>globalThis.__NETWIV_IPC__.postMessage(JSON.stringify({kind:'menu'," +
            "label:globalThis.__NETWIV_IPC__.webviewLabel,id,data}))};",
            message => OnMessage(runtime, message),
            OnEvent,
            ResolveAsset,
            ResolveResource: static rid => rid == 0x80000001
                ? new ImageData([255, 64, 32, 255], 1, 1)
                : null,
            HasEventListeners: static (eventName, label) =>
                eventName == "tauri://close-requested" && label == "close-intercept");

        try
        {
            runtime.Run(context);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }

        if (_assetRequestCount != 3)
        {
            Failures.Enqueue($"Expected 3 custom asset requests, received {_assetRequestCount}.");
        }

        if (ReadyLabels.Count != 3 || EvaluatedLabels.Count != 3)
        {
            Failures.Enqueue(
                $"Expected IPC from 3 ready and 3 evaluated webviews; got {ReadyLabels.Count} and {EvaluatedLabels.Count}.");
        }

        if (_destroyedCount != 5)
        {
            Failures.Enqueue($"Expected cleanup events for 5 windows, received {_destroyedCount}.");
        }

        if (_closeRequestedCount != 1)
        {
            Failures.Enqueue($"Expected one intercepted close request, received {_closeRequestedCount}.");
        }

        if (Environment.GetEnvironmentVariable("NETWIV_SMOKE_EXPECT_MENU_CLICK") == "1" && _menuActivated == 0)
        {
            Failures.Enqueue("Expected the native GTK menu activation channel to fire.");
        }
        if (Environment.GetEnvironmentVariable("NETWIV_SMOKE_EXPECT_TRAY_CLICK") == "1" && _trayMenuActivated == 0)
        {
            Failures.Enqueue("Expected the native Ayatana tray menu channel to fire.");
        }

        if (!Failures.IsEmpty)
        {
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine($"FAIL: {failure}");
            }

            return 1;
        }

        if (!VerifyRepeatedRun(runtime))
        {
            return 1;
        }

        Console.WriteLine("PASS: Linux NativeAOT GTK3/WebKitGTK gate completed.");
        return 0;
    }

    private static bool VerifyRepeatedRun(LinuxDesktopRuntime runtime)
    {
        int assets = 0;
        int messages = 0;
        int destroyed = 0;
        RuntimeContext repeatedContext = new(
            [new WindowOptions("repeat", "NetWiv repeat", "repeat", 320, 200, EmptyObject)],
            "globalThis.__TAURI_INTERNALS__={ipc:message=>globalThis.__NETWIV_IPC__.postMessage(message)};",
            message =>
            {
                if (Interlocked.Increment(ref messages) == 1)
                {
                    _ = Task.Run(async () =>
                    {
                        await runtime.InvokeAsync(
                            "plugin:window|close",
                            EmptyObject,
                            new RuntimeCommandContext("repeat", message.Source, true)).ConfigureAwait(false);
                    });
                }
            },
            runtimeEvent =>
            {
                if (runtimeEvent.EventName == "window-destroyed") Interlocked.Increment(ref destroyed);
            },
            request =>
            {
                Interlocked.Increment(ref assets);
                return new AssetResponse(
                    200,
                    "text/html; charset=utf-8",
                    "<script>addEventListener('DOMContentLoaded',()=>__TAURI_INTERNALS__.ipc('{}'))</script>"u8.ToArray());
            });

        try
        {
            runtime.Run(repeatedContext);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: repeated runtime run threw: {exception}");
            return false;
        }

        if (assets == 1 && messages == 1 && destroyed == 1)
        {
            return true;
        }

        Console.Error.WriteLine(
            $"FAIL: repeated runtime run counts were assets={assets}, messages={messages}, destroyed={destroyed}.");
        return false;
    }

    private static AssetResponse? ResolveAsset(AssetRequest request)
    {
        Interlocked.Increment(ref _assetRequestCount);
        string label = request.Uri.AbsolutePath.Trim('/');
        if (label is not ("left" or "right" or "secondary"))
        {
            return null;
        }

        string html = "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<script>document.addEventListener('DOMContentLoaded',()=>{" +
            "globalThis.__TAURI_INTERNALS__.ipc(JSON.stringify({kind:'ready'," +
            $"label:'{label}',docStart:globalThis.__netwivDocumentStart," +
            "width:innerWidth,height:innerHeight}));});</script></head><body>NetWiv smoke</body></html>";
        return new AssetResponse(200, "text/html; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(html));
    }

    private static void OnMessage(LinuxDesktopRuntime runtime, RuntimeMessage message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message.Json);
            JsonElement root = document.RootElement;
            string kind = root.GetProperty("kind").GetString()!;
            string reportedLabel = root.GetProperty("label").GetString()!;
            if (reportedLabel != message.WebviewLabel)
            {
                Failures.Enqueue($"Native label '{message.WebviewLabel}' disagrees with page '{reportedLabel}'.");
            }

            if (message.Source.Scheme != "tauri" || message.Source.Host != "localhost")
            {
                Failures.Enqueue($"Unexpected IPC source '{message.Source}'.");
            }

            if (kind == "ready")
            {
                if (!root.GetProperty("docStart").GetBoolean())
                {
                    Failures.Enqueue($"Document-start script ran late in '{message.WebviewLabel}'.");
                }

                if (root.GetProperty("width").GetInt32() <= 0 || root.GetProperty("height").GetInt32() <= 0)
                {
                    Failures.Enqueue($"Webview '{message.WebviewLabel}' reported an invalid viewport.");
                }

                ReadyLabels.TryAdd(message.WebviewLabel, 0);
                runtime.EvaluateScript(
                    message.WebviewLabel,
                    $"globalThis.__TAURI_INTERNALS__.ipc(JSON.stringify({{kind:'evaluated',label:'{message.WebviewLabel}'}}));");
            }
            else if (kind == "evaluated")
            {
                EvaluatedLabels.TryAdd(message.WebviewLabel, 0);
            }
            else if (kind == "menu")
            {
                string? itemId = root.GetProperty("data").GetProperty("message").GetString();
                if (itemId == "smoke-item") Interlocked.Exchange(ref _menuActivated, 1);
                if (itemId == "tray-action") Interlocked.Exchange(ref _trayMenuActivated, 1);
            }

            if (ReadyLabels.Count == 3 && EvaluatedLabels.Count == 3 &&
                Interlocked.Exchange(ref _verificationStarted, 1) == 0)
            {
                _ = Task.Run(() => VerifyCommandsAndCloseAsync(runtime));
            }
        }
        catch (Exception exception)
        {
            Failures.Enqueue($"IPC validation failed: {exception.Message}");
            TryExit(runtime, 1);
        }
    }

    private static async Task VerifyCommandsAndCloseAsync(LinuxDesktopRuntime runtime)
    {
        try
        {
            foreach (string label in new[] { "left", "right", "secondary" })
            {
                RuntimeCommandContext caller = new(label, new Uri($"tauri://localhost/{label}"), true);
                JsonElement visible = await runtime.InvokeAsync(
                    "plugin:window|is_visible", EmptyObject, caller).ConfigureAwait(false);
                if (!visible.GetBoolean())
                {
                    Failures.Enqueue($"Window '{label}' was not visible.");
                }

                JsonElement size = await runtime.InvokeAsync(
                    "plugin:window|inner_size", EmptyObject, caller).ConfigureAwait(false);
                if (size.GetProperty("width").GetInt32() <= 0 || size.GetProperty("height").GetInt32() <= 0)
                {
                    Failures.Enqueue($"Window '{label}' returned an invalid native size.");
                }

                using JsonDocument titleArgs = JsonDocument.Parse($"{{\"value\":\"verified {label}\"}}");
                await runtime.InvokeAsync(
                    "plugin:window|set_title", titleArgs.RootElement, caller).ConfigureAwait(false);
            }

            RuntimeCommandContext resourceCaller = new(
                "left", new Uri("tauri://localhost/left"), true);
            using JsonDocument webviewWindowArgs = JsonDocument.Parse(
                "{\"options\":{\"label\":\"dynamic-window\",\"url\":\"about:blank\",\"visible\":false,\"width\":360,\"height\":240}}");
            await runtime.InvokeAsync(
                "plugin:webview|create_webview_window", webviewWindowArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument dynamicWindowCloseArgs = JsonDocument.Parse("{\"label\":\"dynamic-window\"}");
            await runtime.InvokeAsync(
                "plugin:window|close", dynamicWindowCloseArgs.RootElement, resourceCaller).ConfigureAwait(false);
            JsonElement mainStillVisible = await runtime.InvokeAsync(
                "plugin:window|is_visible", EmptyObject, resourceCaller).ConfigureAwait(false);
            if (!mainStillVisible.GetBoolean())
            {
                Failures.Enqueue("Closing a dynamic WebviewWindow incorrectly closed its caller window.");
            }

            using JsonDocument interceptArgs = JsonDocument.Parse(
                "{\"options\":{\"label\":\"close-intercept\",\"url\":\"about:blank\",\"visible\":false}}");
            await runtime.InvokeAsync(
                "plugin:webview|create_webview_window", interceptArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument interceptLabelArgs = JsonDocument.Parse("{\"label\":\"close-intercept\"}");
            await runtime.InvokeAsync(
                "plugin:window|close", interceptLabelArgs.RootElement, resourceCaller).ConfigureAwait(false);
            JsonElement windowsAfterIntercept = await runtime.InvokeAsync(
                "plugin:window|get_all_windows", EmptyObject, resourceCaller).ConfigureAwait(false);
            if (!windowsAfterIntercept.EnumerateArray().Any(item => item.GetString() == "close-intercept"))
            {
                Failures.Enqueue("A prevented close request destroyed its GTK window.");
            }
            await runtime.InvokeAsync(
                "plugin:window|destroy", interceptLabelArgs.RootElement, resourceCaller).ConfigureAwait(false);

            using JsonDocument hostArgs = JsonDocument.Parse(
                "{\"options\":{\"label\":\"dynamic-host\",\"title\":\"Dynamic host\",\"visible\":true,\"width\":500,\"height\":360}}");
            await runtime.InvokeAsync(
                "plugin:window|create", hostArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument childArgs = JsonDocument.Parse(
                "{\"windowLabel\":\"dynamic-host\",\"options\":{\"label\":\"dynamic-child\",\"url\":\"about:blank\",\"x\":10,\"y\":10,\"width\":120,\"height\":90}}");
            await runtime.InvokeAsync(
                "plugin:webview|create_webview", childArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument autoResizeArgs = JsonDocument.Parse(
                "{\"label\":\"dynamic-child\",\"value\":true}");
            await runtime.InvokeAsync(
                "plugin:webview|set_webview_auto_resize", autoResizeArgs.RootElement, resourceCaller).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            using JsonDocument childLabelArgs = JsonDocument.Parse("{\"label\":\"dynamic-child\"}");
            JsonElement autoSize = await runtime.InvokeAsync(
                "plugin:webview|webview_size", childLabelArgs.RootElement, resourceCaller).ConfigureAwait(false);
            if (autoSize.GetProperty("width").GetInt32() <= 120 || autoSize.GetProperty("height").GetInt32() <= 90)
            {
                Failures.Enqueue("Dynamic webview auto-resize did not follow its GTK container allocation.");
            }

            using JsonDocument reparentArgs = JsonDocument.Parse(
                "{\"label\":\"dynamic-child\",\"window\":\"main\"}");
            await runtime.InvokeAsync(
                "plugin:webview|reparent", reparentArgs.RootElement, resourceCaller).ConfigureAwait(false);
            JsonElement allWebviews = await runtime.InvokeAsync(
                "plugin:webview|get_all_webviews", EmptyObject, resourceCaller).ConfigureAwait(false);
            bool reparented = allWebviews.EnumerateArray().Any(item =>
                item.GetProperty("label").GetString() == "dynamic-child" &&
                item.GetProperty("windowLabel").GetString() == "main");
            if (!reparented)
            {
                Failures.Enqueue("Dynamic webview reparenting did not update native parent routing.");
            }
            await runtime.InvokeAsync(
                "plugin:webview|webview_close", childLabelArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument hostCloseArgs = JsonDocument.Parse("{\"label\":\"dynamic-host\"}");
            await runtime.InvokeAsync(
                "plugin:window|destroy", hostCloseArgs.RootElement, resourceCaller).ConfigureAwait(false);

            using JsonDocument menuArgs = JsonDocument.Parse(
                "{\"kind\":\"Menu\",\"options\":{\"id\":\"smoke-menu\",\"items\":[{\"id\":\"conformance-item\",\"text\":\"Conformance\"}]}}");
            JsonElement menu = await runtime.InvokeAsync(
                "plugin:menu|new", menuArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument menuItemsArgs = JsonDocument.Parse(
                $"{{\"rid\":{menu[0].GetInt32()},\"kind\":\"Menu\"}}");
            JsonElement inlineItems = await runtime.InvokeAsync(
                "plugin:menu|items", menuItemsArgs.RootElement, resourceCaller).ConfigureAwait(false);
            if (inlineItems.GetArrayLength() != 1 || inlineItems[0][1].GetString() != "conformance-item")
            {
                Failures.Enqueue("Inline GTK menu construction did not retain its item resource.");
            }
            using JsonDocument itemArgs = JsonDocument.Parse(
                "{\"kind\":\"MenuItem\",\"options\":{\"id\":\"smoke-item\",\"text\":\"Smoke\"},\"handler\":\"__CHANNEL__:77\"}");
            JsonElement item = await runtime.InvokeAsync(
                "plugin:menu|new", itemArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument appendArgs = JsonDocument.Parse(
                $"{{\"rid\":{menu[0].GetInt32()},\"kind\":\"Menu\",\"items\":[[{item[0].GetInt32()},\"MenuItem\"]]}}");
            await runtime.InvokeAsync(
                "plugin:menu|append", appendArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument checkArgs = JsonDocument.Parse(
                "{\"kind\":\"Check\",\"options\":{\"id\":\"smoke-check\",\"text\":\"Checked\",\"checked\":false}}");
            JsonElement check = await runtime.InvokeAsync(
                "plugin:menu|new", checkArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument setCheckArgs = JsonDocument.Parse(
                $"{{\"rid\":{check[0].GetInt32()},\"kind\":\"Check\",\"checked\":true}}");
            await runtime.InvokeAsync(
                "plugin:menu|set_checked", setCheckArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument getCheckArgs = JsonDocument.Parse(
                $"{{\"rid\":{check[0].GetInt32()},\"kind\":\"Check\"}}");
            JsonElement isChecked = await runtime.InvokeAsync(
                "plugin:menu|is_checked", getCheckArgs.RootElement, resourceCaller).ConfigureAwait(false);
            if (!isChecked.GetBoolean())
            {
                Failures.Enqueue("GTK check menu item did not preserve checked state.");
            }
            using JsonDocument appendCheckArgs = JsonDocument.Parse(
                $"{{\"rid\":{menu[0].GetInt32()},\"kind\":\"Menu\",\"items\":[[{check[0].GetInt32()},\"Check\"]]}}");
            await runtime.InvokeAsync(
                "plugin:menu|append", appendCheckArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument iconArgs = JsonDocument.Parse(
                "{\"kind\":\"Icon\",\"options\":{\"id\":\"smoke-icon\",\"text\":\"Icon\",\"icon\":2147483649}}");
            JsonElement iconItem = await runtime.InvokeAsync(
                "plugin:menu|new", iconArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument appendIconArgs = JsonDocument.Parse(
                $"{{\"rid\":{menu[0].GetInt32()},\"kind\":\"Menu\",\"items\":[[{iconItem[0].GetInt32()},\"Icon\"]]}}");
            await runtime.InvokeAsync(
                "plugin:menu|append", appendIconArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument windowMenuArgs = JsonDocument.Parse(
                $"{{\"rid\":{menu[0].GetInt32()},\"window\":\"main\"}}");
            await runtime.InvokeAsync(
                "plugin:menu|set_as_window_menu", windowMenuArgs.RootElement, resourceCaller).ConfigureAwait(false);

            using JsonDocument trayMenuArgs = JsonDocument.Parse(
                "{\"kind\":\"Submenu\",\"options\":{\"id\":\"tray-menu\",\"text\":\"Tray\",\"items\":[{\"id\":\"tray-action\",\"text\":\"Tray Action\",\"handler\":\"__CHANNEL__:78\"}]}}");
            JsonElement trayMenu = await runtime.InvokeAsync(
                "plugin:menu|new", trayMenuArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument trayArgs = JsonDocument.Parse(
                $"{{\"options\":{{\"id\":\"smoke-tray\",\"title\":\"NetWiv\",\"icon\":2147483649,\"menu\":[{trayMenu[0].GetInt32()},\"Submenu\"]}},\"handler\":0}}");
            JsonElement tray = await runtime.InvokeAsync(
                "plugin:tray|new", trayArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument trayTitleArgs = JsonDocument.Parse(
                $"{{\"rid\":{tray[0].GetInt32()},\"title\":\"NetWiv smoke\"}}");
            await runtime.InvokeAsync(
                "plugin:tray|set_title", trayTitleArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument trayVisibleArgs = JsonDocument.Parse(
                $"{{\"rid\":{tray[0].GetInt32()},\"visible\":true}}");
            await runtime.InvokeAsync(
                "plugin:tray|set_visible", trayVisibleArgs.RootElement, resourceCaller).ConfigureAwait(false);
            using JsonDocument trayLookupArgs = JsonDocument.Parse("{\"id\":\"smoke-tray\"}");
            JsonElement foundTray = await runtime.InvokeAsync(
                "plugin:tray|get_by_id", trayLookupArgs.RootElement, resourceCaller).ConfigureAwait(false);
            if (foundTray.GetInt32() != tray[0].GetInt32())
            {
                Failures.Enqueue("Ayatana tray resource lookup returned a different rid.");
            }

            int holdMilliseconds = GetHoldMilliseconds();
            if (holdMilliseconds > 0)
            {
                Console.WriteLine($"HOLD: keeping verified windows open for {holdMilliseconds} ms.");
                await Task.Delay(holdMilliseconds).ConfigureAwait(false);
            }

            await runtime.InvokeAsync(
                "plugin:tray|remove_by_id", trayLookupArgs.RootElement, resourceCaller).ConfigureAwait(false);

            await runtime.InvokeAsync(
                "plugin:window|close",
                EmptyObject,
                new RuntimeCommandContext(
                    "secondary", new Uri("tauri://localhost/secondary"), true)).ConfigureAwait(false);
            await runtime.InvokeAsync(
                "plugin:window|close",
                EmptyObject,
                new RuntimeCommandContext(
                    "left", new Uri("tauri://localhost/left"), true)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Failures.Enqueue($"Native command validation failed: {exception.Message}");
            TryExit(runtime, 1);
        }
    }

    private static void OnEvent(RuntimeEvent runtimeEvent)
    {
        if (runtimeEvent.EventName == "window-destroyed")
        {
            Interlocked.Increment(ref _destroyedCount);
        }
        else if (runtimeEvent.EventName == "tauri://close-requested" && runtimeEvent.Label == "close-intercept")
        {
            Interlocked.Increment(ref _closeRequestedCount);
        }
    }

    private static void TryExit(LinuxDesktopRuntime runtime, int exitCode)
    {
        try
        {
            runtime.Exit(exitCode);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static int GetHoldMilliseconds()
    {
        string? value = Environment.GetEnvironmentVariable("NETWIV_SMOKE_HOLD_MS");
        return int.TryParse(value, out int milliseconds)
            ? Math.Clamp(milliseconds, 0, 120_000)
            : 0;
    }
}
