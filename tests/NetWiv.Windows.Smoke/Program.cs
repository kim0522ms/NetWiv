using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Windows;
using NetWiv.Runtime;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("NetWiv.Windows.Smoke requires Windows.");
            return 2;
        }

var runtime = new WindowsDesktopRuntime();
var pageReady = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
var roundTrips = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
bool childRequested = false;
bool resizeRequested = false;
bool completionStarted = false;
bool nativeResourcesVerified = false;
bool menuActionReceived = false;
bool trayActionReceived = false;
bool menuChannelReceived = false;
bool trayChannelReceived = false;
int holdMilliseconds = int.TryParse(Environment.GetEnvironmentVariable("NETWIV_SMOKE_HOLD_MS"), out int requestedHold)
    ? Math.Clamp(requestedHold, 0, 120_000)
    : 0;

JsonElement Parse(string json)
{
    using JsonDocument document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

void VerifyNativeResources(RuntimeCommandContext commandContext)
{
    JsonElement Invoke(string command, string json) => runtime.InvokeAsync(
        command, Parse(json), commandContext).GetAwaiter().GetResult();

    uint checkRid = Invoke("plugin:menu|new", """{"kind":"Check","options":{"id":"check","text":"Unchecked","checked":false},"handler":"__CHANNEL__:7010"}""")[0].GetUInt32();
    _ = Invoke("plugin:menu|set_checked", $"{{\"rid\":{checkRid},\"checked\":true}}");
    if (!Invoke("plugin:menu|is_checked", $"{{\"rid\":{checkRid},\"kind\":\"Check\"}}").GetBoolean())
        throw new InvalidOperationException("Check menu item mutation was not retained.");
    _ = Invoke("plugin:menu|set_text", $"{{\"rid\":{checkRid},\"kind\":\"Check\",\"text\":\"Checked\"}}");
    _ = Invoke("plugin:menu|set_enabled", $"{{\"rid\":{checkRid},\"kind\":\"Check\",\"enabled\":false}}");
    if (Invoke("plugin:menu|text", $"{{\"rid\":{checkRid},\"kind\":\"Check\"}}").GetString() != "Checked"
        || Invoke("plugin:menu|is_enabled", $"{{\"rid\":{checkRid},\"kind\":\"Check\"}}").GetBoolean())
        throw new InvalidOperationException("Menu text/enabled mutations were not retained.");
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{checkRid}}}");

    uint iconRid = Invoke("plugin:menu|new", """{"kind":"Icon","options":{"id":"icon","text":"Icon","icon":2147483648},"handler":"__CHANNEL__:7011"}""")[0].GetUInt32();
    _ = Invoke("plugin:menu|set_icon", $"{{\"rid\":{iconRid},\"kind\":\"Icon\",\"icon\":null}}");
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{iconRid}}}");

    uint submenuRid = Invoke("plugin:menu|new", """{"kind":"Submenu","options":{"id":"nested","text":"Nested","items":[{"id":"nested-child","text":"Child"}]},"handler":"__CHANNEL__:7012"}""")[0].GetUInt32();
    JsonElement nestedItems = Invoke("plugin:menu|items", $"{{\"rid\":{submenuRid},\"kind\":\"Submenu\"}}");
    if (nestedItems.GetArrayLength() != 1) throw new InvalidOperationException("Nested submenu lost its child.");
    uint nestedAlias = nestedItems[0][0].GetUInt32();
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{submenuRid}}}");
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{nestedAlias}}}");

    uint sharedRid = Invoke("plugin:menu|new", """{"kind":"MenuItem","options":{"id":"shared","text":"Shared"},"handler":"__CHANNEL__:7013"}""")[0].GetUInt32();
    uint firstMenu = Invoke("plugin:menu|new", $"{{\"kind\":\"Menu\",\"options\":{{\"id\":\"first\",\"items\":[[{sharedRid},\"MenuItem\"]]}},\"handler\":\"__CHANNEL__:7014\"}}")[0].GetUInt32();
    uint secondMenu = Invoke("plugin:menu|new", $"{{\"kind\":\"Menu\",\"options\":{{\"id\":\"second\",\"items\":[[{sharedRid},\"MenuItem\"]]}},\"handler\":\"__CHANNEL__:7015\"}}")[0].GetUInt32();
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{sharedRid}}}");
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{firstMenu}}}");
    JsonElement retained = Invoke("plugin:menu|items", $"{{\"rid\":{secondMenu},\"kind\":\"Menu\"}}");
    if (retained.GetArrayLength() != 1 || retained[0][1].GetString() != "shared")
        throw new InvalidOperationException("An item reused by two native menus was disposed too early.");
    uint retainedAlias = retained[0][0].GetUInt32();
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{secondMenu}}}");
    _ = Invoke("plugin:resources|close", $"{{\"rid\":{retainedAlias}}}");

    JsonElement menu = runtime.InvokeAsync(
        "plugin:menu|new",
        Parse("""{"kind":"Menu","options":{"id":"smoke-menu","items":[{"id":"smoke-item","text":"Smoke Action","handler":"__CHANNEL__:7002"}]},"handler":"__CHANNEL__:7001"}"""),
        commandContext).GetAwaiter().GetResult();
    uint menuRid = menu[0].GetUInt32();
    JsonElement items = runtime.InvokeAsync(
        "plugin:menu|items",
        Parse($"{{\"rid\":{menuRid},\"kind\":\"Menu\"}}"),
        commandContext).GetAwaiter().GetResult();
    if (items.GetArrayLength() != 1 || items[0][1].GetString() != "smoke-item")
    {
        throw new InvalidOperationException("Native menu did not retain its item.");
    }
    runtime.InvokeAsync(
        "plugin:menu|set_as_window_menu",
        Parse($"{{\"rid\":{menuRid},\"kind\":\"Menu\",\"window\":\"main\"}}"),
        commandContext).GetAwaiter().GetResult();

    nint mainWindow = NativeTestMethods.FindWindow(null, "NetWiv Windows Gate 0 - Main");
    nint nativeMenu = NativeTestMethods.GetMenu(mainWindow);
    uint commandId = NativeTestMethods.GetMenuItemId(nativeMenu, 0);
    if (mainWindow == 0 || nativeMenu == 0 || commandId == uint.MaxValue)
    {
        throw new InvalidOperationException("Native Win32 menu handles were not discoverable.");
    }
    _ = NativeTestMethods.SendMessage(mainWindow, 0x0111, commandId, 0);

    JsonElement tray = runtime.InvokeAsync(
        "plugin:tray|new",
        Parse("""{"options":{"id":"smoke-tray","icon":2147483648,"tooltip":"NetWiv native tray","visible":true},"handler":"__CHANNEL__:7003"}"""),
        commandContext).GetAwaiter().GetResult();
    uint trayRid = tray[0].GetUInt32();
    _ = NativeTestMethods.SendMessage(mainWindow, 0x8002, trayRid, 0x0202);
    JsonElement foundTray = runtime.InvokeAsync(
        "plugin:tray|get_by_id",
        Parse("""{"id":"smoke-tray"}"""),
        commandContext).GetAwaiter().GetResult();
    if (foundTray.ValueKind != JsonValueKind.Number || foundTray.GetUInt32() == 0)
    {
        throw new InvalidOperationException("Native tray lookup did not return a resource handle.");
    }
    runtime.InvokeAsync(
        "plugin:tray|remove_by_id",
        Parse("""{"id":"smoke-tray"}"""),
        commandContext).GetAwaiter().GetResult();
    runtime.InvokeAsync(
        "plugin:resources|close",
        Parse($"{{\"rid\":{menuRid}}}"),
        commandContext).GetAwaiter().GetResult();
    nativeResourcesVerified = true;
}

AssetResponse? ResolveAsset(AssetRequest request)
{
    string label = request.Uri.AbsolutePath.Trim('/').Replace(".html", string.Empty, StringComparison.Ordinal);
    if (label is not ("main" or "secondary" or "child"))
    {
        return null;
    }

    string encodedLabel = JsonEncodedText.Encode(label).ToString();
    string html = $$"""
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <style>
            html, body { height: 100%; margin: 0; font: 16px system-ui; background: #101521; color: #e8edf7; }
            body { display: grid; place-items: center; }
            main { padding: 24px; border: 1px solid #52627a; border-radius: 12px; background: #182033; }
            strong { color: #69dcad; }
          </style>
        </head>
        <body>
          <main>NetWiv NativeAOT WebView2: <strong>{{label}}</strong><div id="status">loading</div></main>
          <script>
            const label = "{{encodedLabel}}";
            const documentStart = globalThis.__netwivDocumentStart === 'installed';
            document.querySelector('#status').textContent = documentStart ? 'document-start OK' : 'document-start FAILED';
            chrome.webview.addEventListener('message', event => {
              if (event.data?.kind === 'native-ping') {
                chrome.webview.postMessage({ kind: 'native-roundtrip', label, documentStart });
              }
            });
            chrome.webview.postMessage({ kind: 'page-ready', label, documentStart });
          </script>
        </body>
        </html>
        """;
    return new AssetResponse(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
}

void OnMessage(RuntimeMessage message)
{
    using JsonDocument document = JsonDocument.Parse(message.Json);
    JsonElement root = document.RootElement;
    string kind = root.GetProperty("kind").GetString() ?? string.Empty;
    string label = root.GetProperty("label").GetString() ?? string.Empty;
    bool documentStart = root.GetProperty("documentStart").GetBoolean();
    Console.WriteLine($"MESSAGE kind={kind} label={label} window={message.WindowLabel} source={message.Source} documentStart={documentStart}");
    if (!documentStart)
    {
        throw new InvalidOperationException($"Document-start script was late in webview '{label}'.");
    }

    if (message.WebviewLabel != label)
    {
        throw new InvalidOperationException($"Native webview label '{message.WebviewLabel}' did not match page '{label}'.");
    }

    var commandContext = new RuntimeCommandContext(message.WebviewLabel, message.Source, true);
    if (kind == "native-channel")
    {
        uint callbackId = root.GetProperty("callbackId").GetUInt32();
        JsonElement payload = root.GetProperty("payload");
        if (payload.TryGetProperty("message", out JsonElement eventPayload))
        {
            if (callbackId == 7002 && eventPayload.GetString() == "smoke-item") menuChannelReceived = true;
            if (callbackId == 7003
                && eventPayload.GetProperty("type").GetString() == "Click"
                && eventPayload.GetProperty("button").GetString() == "Left"
                && eventPayload.GetProperty("buttonState").GetString() == "Up"
                && eventPayload.GetProperty("position").TryGetProperty("x", out _)
                && eventPayload.GetProperty("rect").TryGetProperty("size", out _)) trayChannelReceived = true;
        }
    }
    else if (kind == "page-ready")
    {
        pageReady[label] = true;
        if (label == "main" && !childRequested)
        {
            childRequested = true;
            runtime.InvokeAsync(
                "plugin:webview|create_webview",
                Parse("""{"windowLabel":"main","label":"child","url":"child.html","x":32,"y":250,"width":430,"height":240}"""),
                commandContext).GetAwaiter().GetResult();
        }

        if (label == "main" && !resizeRequested)
        {
            resizeRequested = true;
            runtime.InvokeAsync(
                "window.setSize",
                Parse("""{"width":960,"height":720}"""),
                commandContext).GetAwaiter().GetResult();
        }

        runtime.InvokeAsync(
            "webview.postMessage",
            Parse("""{"message":{"kind":"native-ping"}}"""),
            commandContext).GetAwaiter().GetResult();
        if (label == "child" && !nativeResourcesVerified)
        {
            VerifyNativeResources(commandContext);
        }
    }
    else if (kind == "native-roundtrip")
    {
        roundTrips[label] = true;
    }

    if (pageReady.Count == 3 && roundTrips.Count == 3
        && nativeResourcesVerified && menuActionReceived && trayActionReceived
        && menuChannelReceived && trayChannelReceived
        && !completionStarted)
    {
        completionStarted = true;
        Console.WriteLine("GATE0 PASS: 2 top-level windows, child multi-webview, document-start, local asset, bidirectional IPC, resize/DPI-aware bounds, cleanup.");
        if (holdMilliseconds == 0)
        {
            runtime.Exit(0);
        }
        else
        {
            Console.WriteLine($"HOLD: keeping verified windows open for {holdMilliseconds} ms.");
            _ = Task.Run(async () =>
            {
                await Task.Delay(holdMilliseconds);
                runtime.Exit(0);
            });
        }
    }
}

void OnEvent(RuntimeEvent runtimeEvent)
{
    Console.WriteLine($"EVENT name={runtimeEvent.EventName} label={runtimeEvent.Label}");
    if (runtimeEvent.EventName == "menu-action" && runtimeEvent.Label == "smoke-item")
    {
        menuActionReceived = true;
    }
    if (runtimeEvent.EventName == "tray-action" && runtimeEvent.Label == "smoke-tray")
    {
        trayActionReceived = true;
    }
}

var windows = new[]
{
    new WindowOptions("main", "NetWiv Windows Gate 0 - Main", "main.html", 820, 640, default),
    new WindowOptions("secondary", "NetWiv Windows Gate 0 - Secondary", "secondary.html", 620, 440, default),
};
var context = new RuntimeContext(
    windows,
    """
    globalThis.__netwivDocumentStart = 'installed';
    globalThis.__TAURI_INTERNALS__ = {
      runCallback(id, payload) {
        chrome.webview.postMessage({
          kind: 'native-channel',
          label: globalThis.__NETWIV_IPC__.webviewLabel,
          documentStart: globalThis.__netwivDocumentStart === 'installed',
          callbackId: id,
          payload
        });
      }
    };
    """,
    OnMessage,
    OnEvent,
    ResolveAsset,
    rid => rid == 0x80000000
        ? new ImageData([255, 0, 0, 255], 1, 1)
        : null);

_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(holdMilliseconds));
    Console.Error.WriteLine($"GATE0 TIMEOUT: pages={string.Join(',', pageReady.Keys)} roundtrips={string.Join(',', roundTrips.Keys)}");
    runtime.Exit(3);
});

try
{
    runtime.Run(context);
    return Environment.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
    }
}

internal static partial class NativeTestMethods
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? className, string windowName);

    [LibraryImport("user32.dll")]
    internal static partial nint GetMenu(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuItemID")]
    internal static partial uint GetMenuItemId(nint menu, int position);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
