using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class WindowApiTests
{
    [Fact]
    public void ApplicationDisposalReleasesListenersAndRejectsNewSubscriptions()
    {
        using var app = CreateApp(out _);
        int callbacks = 0;
        using var listener = app.Handle.GetWindow("main")!.Listen("cleanup", _ => callbacks++);
        app.Handle.Emit("cleanup", JsonValue.Null);
        Assert.Equal(1, callbacks);
        app.Dispose();
        Assert.Throws<ObjectDisposedException>(() => app.Handle.Emit("cleanup", JsonValue.Null));
        Assert.Throws<ObjectDisposedException>(() => app.Handle.Events.Listen("cleanup", _ => callbacks++));
        Assert.Equal(1, callbacks);
        listener.Dispose();
    }

    [Fact]
    public async Task ExplicitResourceCloseRejectsReuseWhileDisposeIsIdempotent()
    {
        using var app = CreateApp(out _);
        var image = await Image.NewAsync(app.Handle, new byte[] { 1, 2, 3, 4 }, 1, 1);
        await image.CloseAsync();
        var error = await Assert.ThrowsAsync<InvokeException>(async () => await image.CloseAsync());
        Assert.Equal($"The resource id {image.ResourceId} is invalid.", error.Message);
        await image.DisposeAsync();
        await image.DisposeAsync();
    }

    [Fact]
    public async Task LiveLookupsUseNativeStateAndKeepWebviewParents()
    {
        using var app = CreateApp(out var runtime);
        runtime.Responses["plugin:window|get_all_windows"] = JsonValue.Parse("""["bare","remote"]""");
        runtime.Responses["plugin:webview|get_all_webviews"] = JsonValue.Parse("""[{"window_label":"bare","label":"child"}]""");
        Assert.Null(await app.Handle.GetWindowAsync("main"));
        Assert.Equal(new[] { "bare", "remote" }, (await app.Handle.GetAllWindowsAsync()).Select(window => window.Label));
        Assert.Equal("bare", (await app.Handle.GetWebviewWindowAsync("bare"))!.Label);
        var child = await app.Handle.GetWebviewAsync("child");
        Assert.Equal("bare", child!.Window.Label);
        Assert.Null(await app.Handle.GetWebviewAsync("missing"));
        runtime.Responses["plugin:window|is_focused"] = JsonValue.From(false);
        Assert.Null(await app.Handle.GetFocusedWindowAsync());
        runtime.Responses["plugin:window|is_focused"] = JsonValue.From(true);
        Assert.Equal("bare", (await app.Handle.GetFocusedWindowAsync())!.Label);
        // Querying native state must not synthesize lifecycle entries.
        Assert.Single(app.Handle.Windows);
        Assert.Equal("main", app.Handle.Windows.Single().Label);
    }

    [Fact]
    public async Task MonitorQueriesPreserveNullWorkAreaAndFractionalCursorCoordinates()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWindow("main")!;
        Assert.Null(await window.CurrentMonitorAsync());
        var monitor = JsonValue.Parse("""{"name":null,"size":{"width":1920,"height":1080},"position":{"x":-1920,"y":0},"workArea":{"position":{"x":-1920,"y":20},"size":{"width":1920,"height":1060}},"scaleFactor":1.25}""");
        runtime.Responses["plugin:window|primary_monitor"] = monitor;
        var primary = await window.PrimaryMonitorAsync();
        Assert.Equal(new PhysicalPosition(-1920, 20), primary!.WorkAreaPosition);
        Assert.Equal(1.25, primary.ScaleFactor);
        runtime.Responses["plugin:window|available_monitors"] = JsonValue.Parse("[" + monitor.GetRawText() + "]");
        Assert.Equal(primary, Assert.Single(await window.AvailableMonitorsAsync()));
        await window.MonitorFromPointAsync(-0.5, 0.75);
        AssertCall(runtime, "plugin:window|monitor_from_point", "main", """{"x":-0.5,"y":0.75}""");
        runtime.Responses["plugin:window|cursor_position"] = JsonValue.Parse("""{"x":-0.5,"y":1.25}""");
        Assert.Equal(new PhysicalPosition<double>(-0.5, 1.25), await window.CursorPositionAsync());
    }

    [Fact]
    public async Task WindowIconsSupportResourcePathBytesAndOverlayRemoval()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWindow("main")!;
        var image = await Image.NewAsync(app.Handle, new byte[] { 1, 2, 3, 255 }, 1, 1);
        await window.SetIconAsync(image);
        AssertCall(runtime, "plugin:window|set_icon", "main", $$"""{"label":"main","value":{{image.ResourceId}}}""");
        await window.SetIconAsync("icon.png");
        AssertCall(runtime, "plugin:window|set_icon", "main", """{"label":"main","value":"icon.png"}""");
        await window.SetIconAsync(new byte[] { 1, 2, 3 });
        AssertCall(runtime, "plugin:window|set_icon", "main", """{"label":"main","value":[1,2,3]}""");
        await window.SetOverlayIconAsync(image);
        AssertCall(runtime, "plugin:window|set_overlay_icon", "main", $$"""{"label":"main","value":{{image.ResourceId}}}""");
        await window.SetOverlayIconAsync();
        Assert.Equal(JsonValueKind.Null, runtime.LastArguments.GetProperty("value").ValueKind);
        await window.ClearEffectsAsync();
        AssertCall(runtime, "plugin:window|set_effects", "main", """{"label":"main","value":null}""");
    }

    [Fact]
    public async Task CombinedBackgroundSetsBothLayersAndStopsAfterFirstFailure()
    {
        using var app = CreateApp(out var runtime);
        var combined = app.Handle.GetWebviewWindow("main")!;
        await combined.SetBackgroundColorAsync(new RgbaColor(12, 34, 56, 78));
        Assert.Equal(new[] { "plugin:window|set_background_color", "plugin:webview|set_webview_background_color" }, runtime.Calls);
        AssertCall(runtime, "plugin:webview|set_webview_background_color", "main", """{"label":"main","value":{"red":12,"green":34,"blue":56,"alpha":78}}""");
        runtime.Calls.Clear();
        await combined.SetBackgroundColorAsync(null);
        Assert.Equal(2, runtime.Calls.Count);
        Assert.Equal(JsonValueKind.Null, runtime.LastArguments.GetProperty("value").ValueKind);
        runtime.Calls.Clear();
        runtime.Failure = new InvalidOperationException("window failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => combined.SetBackgroundColorAsync(null));
        Assert.Equal(new[] { "plugin:window|set_background_color" }, runtime.Calls);
    }

    [Fact]
    public async Task EffectsAndTitleBarPreservePinnedOptionValues()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWindow("main")!;
        await window.SetEffectsAsync(new Effects([Effect.Mica, Effect.FullScreenUI], EffectState.FollowsWindowActiveState, 3.5, new RgbaColor(1,2,3)));
        AssertCall(runtime, "plugin:window|set_effects", "main", """{"label":"main","value":{"effects":["mica","fullScreenUI"],"state":"followsWindowActiveState","radius":3.5,"color":{"red":1,"green":2,"blue":3,"alpha":255}}}""");
        await window.SetEffectsAsync(null);
        Assert.Equal(JsonValueKind.Null, runtime.LastArguments.GetProperty("value").ValueKind);
        await window.SetTitleBarStyleAsync(TitleBarStyle.Transparent);
        AssertCall(runtime, "plugin:window|set_title_bar_style", "main", """{"label":"main","value":"transparent"}""");
    }

    [Fact]
    public async Task ManagedEventTargetsMatchWindowWebviewAndWebviewWindowSeparately()
    {
        using var app = CreateApp(out _);
        var calls = new List<string>();
        using var window = app.Handle.GetWindow("main")!.Listen("typed", _ => calls.Add("Window"));
        using var webview = app.Handle.GetWebview("main")!.Listen("typed", _ => calls.Add("Webview"));
        using var combined = app.Handle.GetWebviewWindow("main")!.Listen("typed", _ => calls.Add("WebviewWindow"));
        app.Handle.Emit("typed", JsonValue.Null, new RuntimeEventTarget("Window", "main"));
        Assert.Equal(new[] { "Window" }, calls);
        calls.Clear();
        app.Handle.Emit("typed", JsonValue.Null, new RuntimeEventTarget("Webview", "main"));
        Assert.Equal(new[] { "Webview" }, calls);
        calls.Clear();
        app.Handle.Emit("typed", JsonValue.Null, new RuntimeEventTarget("WebviewWindow", "main"));
        Assert.Equal(new[] { "WebviewWindow" }, calls);
        calls.Clear();
        await app.Handle.GetWindow("main")!.EmitAsync("typed", JsonValue.Null);
        Assert.Equal(new[] { "Webview", "WebviewWindow", "Window" }, calls.Order().ToArray());
        int count = 0;
        using var once = app.Handle.GetWindow("main")!.Once("once", _ => Interlocked.Increment(ref count));
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => app.Handle.Emit("once", JsonValue.Null))));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task BuildersPreserveUnicodeLabelsAndKeepBareWindowsOutOfWebviewWindows()
    {
        using var app = CreateApp(out _);
        foreach (var label in new[] { "도구:事件", "Ⅷ²", "\u0345", "" })
        {
            var window = await new WindowBuilder(app.Handle, label).BuildAsync();
            Assert.IsType<Window>(window);
            Assert.Equal(label, app.Handle.GetWindow(label)!.Label);
            Assert.Null(app.Handle.GetWebviewWindow(label));
        }
        Assert.Single(app.Handle.WebviewWindows);
        Assert.Throws<ArgumentException>(() => new WindowBuilder(app.Handle, "invalid label!"));
        Assert.Throws<ArgumentException>(() => new WindowBuilder(app.Handle, "\u0301"));
    }

    [Fact]
    public async Task WindowMethodsUsePinnedTauriCommandAndPayloadShape()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWebviewWindow("main")!;

        await window.SetTitleAsync("NetWiv");
        AssertCall(runtime, "plugin:window|set_title", "main", """{"label":"main","value":"NetWiv"}""");

        await window.SetSizeAsync(new LogicalSize(640.5, 480));
        AssertCall(runtime, "plugin:window|set_size", "main",
            """{"label":"main","value":{"Logical":{"width":640.5,"height":480}}}""");

        await window.SetPositionAsync(new PhysicalPosition(-20, 30));
        AssertCall(runtime, "plugin:window|set_position", "main",
            """{"label":"main","value":{"Physical":{"x":-20,"y":30}}}""");
    }

    [Fact]
    public async Task WindowQueriesMapPrimitiveAndDpiResponses()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWebviewWindow("main")!;
        runtime.Responses["plugin:window|scale_factor"] = JsonValue.Parse("1.5");
        runtime.Responses["plugin:window|inner_size"] = JsonValue.Parse("{\"width\":1200,\"height\":800}");
        runtime.Responses["plugin:window|outer_position"] = JsonValue.Parse("{\"x\":-10,\"y\":40}");
        runtime.Responses["plugin:window|is_visible"] = JsonValue.Parse("true");
        runtime.Responses["plugin:window|theme"] = JsonValue.Parse("\"dark\"");

        Assert.Equal(1.5, await window.ScaleFactorAsync());
        Assert.Equal(new PhysicalSize(1200, 800), await window.InnerSizeAsync());
        Assert.Equal(new PhysicalPosition(-10, 40), await window.OuterPositionAsync());
        Assert.True(await window.IsVisibleAsync());
        Assert.Equal(Theme.Dark, await window.ThemeAsync());
    }

    [Fact]
    public async Task OptionalConstraintsAndEnumsSerializeLikeTauriApi()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWebviewWindow("main")!;

        await window.SetSizeConstraintsAsync(new WindowSizeConstraints(MinWidth: 300, MaxHeight: 900));
        AssertCall(runtime, "plugin:window|set_size_constraints", "main",
            """{"label":"main","value":{"minWidth":{"Logical":300},"minHeight":null,"maxWidth":null,"maxHeight":{"Logical":900}}}""");

        await window.RequestUserAttentionAsync(UserAttentionType.Critical);
        AssertCall(runtime, "plugin:window|request_user_attention", "main",
            """{"label":"main","value":{"type":"Critical"}}""");

        await window.SetCursorIconAsync(CursorIcon.NwseResize);
        AssertCall(runtime, "plugin:window|set_cursor_icon", "main",
            """{"label":"main","value":"nwseResize"}""");
    }

    [Fact]
    public async Task WebviewMethodsUseWebviewCommandsAndTargetContext()
    {
        using var app = CreateApp(out var runtime);
        var webview = app.Handle.GetWebview("main")!;

        await webview.SetZoomAsync(1.25);
        AssertCall(runtime, "plugin:webview|set_webview_zoom", "main",
            """{"label":"main","value":1.25}""");

        await webview.ReparentAsync("secondary");
        AssertCall(runtime, "plugin:webview|reparent", "main",
            """{"label":"main","window":"secondary"}""");

        await webview.ClearAllBrowsingDataAsync();
        AssertCall(runtime, "plugin:webview|clear_all_browsing_data", "main", "{}");
    }

    [Fact]
    public async Task UnsupportedBackendOperationRemainsAnExplicitFailure()
    {
        using var app = CreateApp(out var runtime);
        runtime.Failure = new NotSupportedException("not implemented by backend");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            app.Handle.GetWebviewWindow("main")!.SetShadowAsync(true));

        Assert.Equal("not implemented by backend", exception.Message);
    }

    [Fact]
    public void DpiConversionsMatchRustRoundingAndValidateScale()
    {
        Assert.Equal(new PhysicalSize(2, 3), new LogicalSize(1, 2).ToPhysical(1.5));
        Assert.Equal(new PhysicalPosition(-2, 2), new LogicalPosition(-1, 1).ToPhysical(1.5));
        Assert.Equal(new LogicalSize(100, 50), new PhysicalSize(200, 100).ToLogical(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalSize(1, 1).ToLogical(0));
    }

    [Fact]
    public async Task AppAndPathApisUseCoreCommandsWithoutReflection()
    {
        using var app = CreateApp(out _);

        Assert.Equal("NetWiv", await AppApi.GetNameAsync(app.Handle));
        Assert.Equal("1.0.0", await AppApi.GetVersionAsync(app.Handle));
        Assert.Equal("com.netwiv.app", await AppApi.GetIdentifierAsync(app.Handle));
        // Intentional difference: NetWiv does not embed the framework, so the version is unavailable (docs/api-status.md).
        var tauriVersion = await Assert.ThrowsAsync<InvokeException>(() => AppApi.GetTauriVersionAsync(app.Handle));
        Assert.Equal("NetWiv does not embed the TAURI framework; its version is unavailable.", tauriVersion.Message);
        Assert.Equal(Path.Combine("folder", "child"), await PathApi.JoinAsync(
            app.Handle, ["folder", "child"]));
        Assert.Equal("txt", await PathApi.ExtensionNameAsync(app.Handle, "file.txt"));
        Assert.Equal(Path.DirectorySeparatorChar.ToString(), PathApi.Separator);
        Assert.Equal(Path.PathSeparator.ToString(), PathApi.Delimiter);
    }

    [Fact]
    public async Task DesktopAppCommandsDelegateToRuntime()
    {
        using var app = CreateApp(out var runtime);
        runtime.Responses["plugin:app|fetch_data_store_identifiers"] = JsonValue.Parse("[]");
        var identifier = new DataStoreIdentifier(Enumerable.Range(0, 15).Select(static value => (byte)value).Append((byte)255).ToArray());
        Assert.Equal(Enumerable.Range(0, 15).Select(static value => (byte)value).Append((byte)255), identifier.ToArray());
        Assert.Empty(await AppApi.FetchDataStoreIdentifiersAsync(app.Handle));
        Assert.Null(await AppApi.GetBundleTypeAsync(app.Handle));
        Assert.True(await AppApi.SupportsMultipleWindowsAsync(app.Handle));
        await AppApi.RemoveDataStoreAsync(app.Handle, identifier);
        await AppApi.ShowAsync(app.Handle); await AppApi.HideAsync(app.Handle);
        await AppApi.SetDockVisibilityAsync(app.Handle, true);
        Assert.Contains("plugin:app|app_show", runtime.Calls);
        Assert.Contains("plugin:app|app_hide", runtime.Calls);
        Assert.Equal("plugin:app|set_dock_visibility", runtime.LastCommand);
        runtime.Failure = new InvokeException("unsupported data store");
        await Assert.ThrowsAsync<InvokeException>(() => AppApi.FetchDataStoreIdentifiersAsync(app.Handle));
    }

    [Fact]
    public async Task ImageApiOwnsAndClosesCoreResource()
    {
        using var app = CreateApp(out _);
        var rgba = new byte[] { 10, 20, 30, 255 };
        var image = await Image.NewAsync(app.Handle, rgba, 1, 1);

        Assert.Equal(new PhysicalSize(1, 1), await image.SizeAsync());
        Assert.Equal(rgba, await image.RgbaAsync());

        await image.CloseAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => image.SizeAsync());
    }

    [Fact]
    public async Task DefaultWindowIconAllocatesIndependentResources()
    {
        var data = new ImageData([10, 20, 30, 255], 1, 1);
        using var app = new Builder().UseRuntime(new RecordingRuntime()).DefaultWindowIcon(data).Build();
        var first = await AppApi.DefaultWindowIconAsync(app.Handle);
        var second = await AppApi.DefaultWindowIconAsync(app.Handle);
        Assert.NotNull(first); Assert.NotNull(second);
        Assert.NotEqual(first.ResourceId, second.ResourceId);
        await first.CloseAsync();
        Assert.Equal(new byte[] {10, 20, 30, 255}, (await second.RgbaAsync()).ToArray());
        await second.CloseAsync();
    }

    [Fact]
    public async Task MenuAndTrayApisUseNativeResourcePairsAndPinnedPayloads()
    {
        using var app = CreateApp(out var runtime);
        runtime.Responses["plugin:menu|new"] = JsonValue.Parse("[11,\"file\"]");

        var menu = await Menu.NewAsync(app.Handle, new MenuOptions("file"));
        Assert.Equal((uint)11, menu.ResourceId);
        AssertCall(runtime, "plugin:menu|new", "native",
            """{"kind":"Menu","options":{"id":"file"},"handler":null}""");

        runtime.Responses["plugin:tray|new"] = JsonValue.Parse("[12,\"main-tray\"]");
        var tray = await TrayIcon.NewAsync(app.Handle, new TrayIconOptions(
            Id: "main-tray", Menu: menu, Tooltip: "NetWiv", Visible: true));
        Assert.Equal("main-tray", tray.Id);
        AssertCall(runtime, "plugin:tray|new", "native",
            """{"options":{"id":"main-tray","menu":[11,"Menu"],"tooltip":"NetWiv","visible":true},"handler":null}""");

        await tray.SetTooltipAsync("ready");
        AssertCall(runtime, "plugin:tray|set_tooltip", "native",
            """{"rid":12,"tooltip":"ready"}""");
    }

    [Fact]
    public async Task DynamicBuildersRegisterCreatedWindowsAndWebviews()
    {
        using var app = CreateApp(out var runtime);

        var window = await new WindowBuilder(app.Handle, "tools")
            .Title("Tools")
            .Position(10, 20)
            .InnerSize(500, 300)
            .Visible(false)
            .BuildAsync();
        Assert.Same(window, app.Handle.GetWindow("tools"));
        Assert.Null(app.Handle.GetWebviewWindow("tools"));
        AssertCall(runtime, "plugin:window|create", "native",
            """{"options":{"label":"tools","title":"Tools","x":10,"y":20,"width":500,"height":300,"visible":false}}""");

        var webviewWindow = await new WebviewWindowBuilder(app.Handle, "secondary")
            .Url("tauri://localhost/secondary.html")
            .Title("Secondary")
            .InnerSize(640, 480)
            .Devtools(true)
            .BuildAsync();
        Assert.Same(webviewWindow, app.Handle.GetWebviewWindow("secondary")!);
        Assert.Same(webviewWindow.Webview, app.Handle.GetWebview("secondary")!);
        AssertCall(runtime, "plugin:webview|create_webview_window", "native",
            """{"options":{"label":"secondary","url":"tauri://localhost/secondary.html","title":"Secondary","width":640,"height":480,"devtools":true}}""");

        var child = await new WebviewBuilder(app.Handle, window, "child")
            .Url("tauri://localhost/child.html")
            .Bounds(5, 6, 320, 200)
            .BuildAsync();
        Assert.Same(child, app.Handle.GetWebview("child"));
        AssertCall(runtime, "plugin:webview|create_webview", "native",
            """{"windowLabel":"tools","options":{"label":"child","url":"tauri://localhost/child.html","x":5,"y":6,"width":320,"height":200}}""");
    }

    private static App CreateApp(out RecordingRuntime runtime)
    {
        runtime = new RecordingRuntime();
        return new Builder()
            .UseRuntime(runtime)
            .Window("main", "API test", "tauri://localhost/index.html")
            .Build();
    }

    private static void AssertCall(
        RecordingRuntime runtime,
        string command,
        string webviewLabel,
        string expectedJson)
    {
        Assert.Equal(command, runtime.LastCommand);
        Assert.Equal(webviewLabel, runtime.LastContext?.WebviewLabel);
        Assert.Equal(webviewLabel, runtime.LastContext?.WindowLabel);
        Assert.True(JsonElement.DeepEquals(JsonValue.Parse(expectedJson), runtime.LastArguments));
    }

    private sealed class RecordingRuntime : IDesktopRuntime
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, JsonElement> Responses { get; } = new(StringComparer.Ordinal);
        public string? LastCommand { get; private set; }
        public JsonElement LastArguments { get; private set; }
        public RuntimeCommandContext? LastContext { get; private set; }
        public Exception? Failure { get; set; }

        public void Run(RuntimeContext context) { }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(
            string command,
            JsonElement args,
            CancellationToken cancellationToken = default) =>
            InvokeAsync(command, args, new RuntimeCommandContext("native", new Uri("netwiv://native"), true), cancellationToken);

        public Task<JsonElement> InvokeAsync(
            string command,
            JsonElement args,
            RuntimeCommandContext context,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            Calls.Add(command);
            LastArguments = args.Clone();
            LastContext = context;
            if (Failure is not null) return Task.FromException<JsonElement>(Failure);
            return Task.FromResult(Responses.TryGetValue(command, out var response)
                ? response.Clone()
                : JsonValue.Null);
        }

        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
    }
}
