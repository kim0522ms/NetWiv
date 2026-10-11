using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class BuilderOptionsTests
{
    [Fact]
    public async Task WindowBuilderSerializesPinnedDesktopOptions()
    {
        using var app = CreateApp(out var runtime);

        await new WindowBuilder(app.Handle, "options")
            .Title("Options").Position(10.5, 20.5).InnerSize(900, 700)
            .MinInnerSize(300, 200).MaxInnerSize(1200, 900)
            .Centered().PreventOverflow(12, 34).Visible(false).Resizable(false)
            .Maximizable(false).Minimizable(false).Closable(false).Decorations(false)
            .Fullscreen().Focused(false).Focusable(false).Transparent().Maximized()
            .AlwaysOnTop().AlwaysOnBottom(false).ContentProtected().SkipTaskbar()
            .Shadow(false).VisibleOnAllWorkspaces().Theme(Theme.Dark)
            .TitleBarStyle(TitleBarStyle.Overlay)
            .WindowEffects(new Effects([Effect.Mica], EffectState.Active, 8, new RgbaColor(1, 2, 3, 4)))
            .BackgroundColor(new RgbaColor(5, 6, 7, 8))
            .BackgroundThrottling(BackgroundThrottlingPolicy.Throttle)
            .JavascriptDisabled().ScrollBarStyle(ScrollBarStyle.FluentOverlay)
            .ParentLabel("main")
            .BuildAsync();

        Assert.Equal("plugin:window|create", runtime.LastCommand);
        AssertJson(runtime.LastArguments,
            """{"options":{"label":"options","title":"Options","x":10.5,"y":20.5,"width":900,"height":700,"minWidth":300,"minHeight":200,"maxWidth":1200,"maxHeight":900,"center":true,"preventOverflow":{"width":12,"height":34},"visible":false,"resizable":false,"maximizable":false,"minimizable":false,"closable":false,"decorations":false,"fullscreen":true,"focus":false,"focusable":false,"transparent":true,"maximized":true,"alwaysOnTop":true,"alwaysOnBottom":false,"contentProtected":true,"skipTaskbar":true,"shadow":false,"visibleOnAllWorkspaces":true,"theme":"dark","titleBarStyle":"overlay","windowEffects":{"effects":["mica"],"state":"active","radius":8,"color":{"red":1,"green":2,"blue":3,"alpha":4}},"backgroundColor":{"red":5,"green":6,"blue":7,"alpha":8},"backgroundThrottling":"throttle","javascriptDisabled":true,"scrollBarStyle":"fluentOverlay","parent":"main"}}""");
    }

    [Fact]
    public async Task WebviewWindowBuilderMergesWindowAndWebviewOptionsOnce()
    {
        using var app = CreateApp(out var runtime);
        var identifier = new DataStoreIdentifier(Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray());

        await new WebviewWindowBuilder(app.Handle, "webview-window")
            .Url("page.html").MinInnerSize(320, 200).PreventOverflow(false)
            .Focusable(false).Maximized().AlwaysOnTop().ContentProtected()
            .Theme(Theme.Light).BackgroundColor(new RgbaColor(10, 20, 30))
            .DragDropEnabled(false).UserAgent("NetWiv-test").Incognito()
            .ProxyUrl("http://127.0.0.1:8080").ZoomHotkeysEnabled()
            .UseHttpsScheme().Devtools().BackgroundThrottling(BackgroundThrottlingPolicy.Suspend)
            .JavascriptDisabled().DataDirectory("profiles/test")
            .DataStoreIdentifier(identifier).ScrollBarStyle(ScrollBarStyle.FluentOverlay)
            .GeneralAutofillEnabled(false)
            .BuildAsync();

        Assert.Equal("plugin:webview|create_webview_window", runtime.LastCommand);
        AssertJson(runtime.LastArguments,
            """{"options":{"label":"webview-window","url":"page.html","minWidth":320,"minHeight":200,"preventOverflow":false,"maximized":true,"alwaysOnTop":true,"contentProtected":true,"focusable":false,"theme":"light","backgroundColor":{"red":10,"green":20,"blue":30,"alpha":255},"dragDropEnabled":false,"userAgent":"NetWiv-test","incognito":true,"proxyUrl":"http://127.0.0.1:8080","zoomHotkeysEnabled":true,"useHttpsScheme":true,"devtools":true,"backgroundThrottling":"suspend","javascriptDisabled":true,"dataDirectory":"profiles/test","dataStoreIdentifier":[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15],"scrollBarStyle":"fluentOverlay","generalAutofillEnabled":false}}""");
    }

    [Fact]
    public async Task ChildWebviewBuilderSerializesAllTargetWebviewOptions()
    {
        using var app = CreateApp(out var runtime);
        var window = app.Handle.GetWindow("main")!;

        var webview = await new WebviewBuilder(app.Handle, window, "child")
            .Url("child.html").Bounds(1, 2, 640, 480).Visible(false).AutoResize()
            .Transparent().Focused(false).DragDropEnabled(false).UserAgent("child-agent")
            .Incognito().ProxyUrl("socks5://127.0.0.1:1080").ZoomHotkeysEnabled(false)
            .UseHttpsScheme().Devtools().BackgroundColor(new RgbaColor(9, 8, 7, 6))
            .BackgroundThrottling(BackgroundThrottlingPolicy.Disabled).JavascriptDisabled()
            .DataDirectory("child-data").ScrollBarStyle(ScrollBarStyle.Default)
            .GeneralAutofillEnabled()
            .BuildAsync();

        Assert.Equal("plugin:webview|create_webview", runtime.LastCommand);
        Assert.Equal("main", webview.Window.Label);
        AssertJson(runtime.LastArguments,
            """{"windowLabel":"main","options":{"label":"child","url":"child.html","x":1,"y":2,"width":640,"height":480,"visible":false,"autoResize":true,"transparent":true,"focus":false,"dragDropEnabled":false,"userAgent":"child-agent","incognito":true,"proxyUrl":"socks5://127.0.0.1:1080","zoomHotkeysEnabled":false,"useHttpsScheme":true,"devtools":true,"backgroundColor":{"red":9,"green":8,"blue":7,"alpha":6},"backgroundThrottling":"disabled","javascriptDisabled":true,"dataDirectory":"child-data","scrollBarStyle":"default","generalAutofillEnabled":true}}""");
    }

    private static App CreateApp(out RecordingRuntime runtime)
    {
        runtime = new RecordingRuntime();
        return new Builder().UseRuntime(runtime)
            .Window("main", "Builder options", "tauri://localhost/index.html")
            .Build();
    }

    private static void AssertJson(JsonElement actual, string expected) =>
        Assert.True(JsonElement.DeepEquals(JsonValue.Parse(expected), actual), actual.GetRawText());

    private sealed class RecordingRuntime : IDesktopRuntime
    {
        public string? LastCommand { get; private set; }
        public JsonElement LastArguments { get; private set; }

        public void Run(RuntimeContext context) { }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
            InvokeAsync(command, args, new RuntimeCommandContext("native", new Uri("netwiv://native"), true), cancellationToken);
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastArguments = args.Clone();
            return Task.FromResult(JsonValue.Null);
        }
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
    }
}
