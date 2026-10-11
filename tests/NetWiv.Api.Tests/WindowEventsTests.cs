using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class WindowEventsTests
{
    [Fact]
    public async Task WindowConveniencesUsePinnedEventNamesAndTypedPayloads()
    {
        using var app = CreateApp();
        var window = await new WindowBuilder(app.Handle, "events").BuildAsync();
        AppEvent<PhysicalSize>? resized = null;
        AppEvent<PhysicalPosition>? moved = null;
        var focus = new List<bool>();
        AppEvent<ScaleFactorChanged>? scaled = null;
        AppEvent<Theme>? themed = null;

        using var resize = window.OnResized(value => resized = value);
        using var move = window.OnMoved(value => moved = value);
        using var focused = window.OnFocusChanged(value => focus.Add(value.Payload));
        using var scale = window.OnScaleChanged(value => scaled = value);
        using var theme = window.OnThemeChanged(value => themed = value);
        var target = new RuntimeEventTarget("Window", "events");

        app.Handle.Emit("tauri://resize", JsonValue.Parse("""{"width":1200,"height":800}"""), target);
        app.Handle.Emit("tauri://move", JsonValue.Parse("""{"x":-20,"y":30}"""), target);
        app.Handle.Emit("tauri://focus", JsonValue.Null, target);
        app.Handle.Emit("tauri://blur", JsonValue.Null, target);
        app.Handle.Emit("tauri://scale-change",
            JsonValue.Parse("""{"scaleFactor":1.5,"size":{"width":900,"height":600}}"""), target);
        app.Handle.Emit("tauri://theme-changed", JsonValue.Parse("\"dark\""), target);

        Assert.Equal(new PhysicalSize(1200, 800), resized!.Payload);
        Assert.Equal("tauri://resize", resized.Name);
        Assert.Equal("events", resized.Label);
        Assert.Equal(new PhysicalPosition(-20, 30), moved!.Payload);
        Assert.Equal(new[] { true, false }, focus);
        Assert.Equal(new ScaleFactorChanged(1.5, new PhysicalSize(900, 600)), scaled!.Payload);
        Assert.Equal(Theme.Dark, themed!.Payload);
    }

    [Fact]
    public async Task WindowAndWebviewDragDropNormalizeThePinnedUnionAndDisposeTogether()
    {
        using var app = CreateApp();
        var window = await new WindowBuilder(app.Handle, "drag-window").BuildAsync();
        var webview = app.Handle.GetWebview("main")!;
        var windowEvents = new List<DragDropEvent>();
        var webviewEvents = new List<DragDropEvent>();
        using var windowSubscription = window.OnDragDropEvent(value => windowEvents.Add(value.Payload));
        var webviewSubscription = webview.OnDragDropEvent(value => webviewEvents.Add(value.Payload));

        EmitDragEvents(app.Handle, new RuntimeEventTarget("Window", "drag-window"));
        EmitDragEvents(app.Handle, new RuntimeEventTarget("Webview", "main"));

        AssertDragSequence(windowEvents);
        AssertDragSequence(webviewEvents);
        webviewSubscription.Dispose();
        webviewSubscription.Dispose();
        EmitDragEvents(app.Handle, new RuntimeEventTarget("Webview", "main"));
        Assert.Equal(4, webviewEvents.Count);
    }

    private static void EmitDragEvents(AppHandle app, RuntimeEventTarget target)
    {
        app.Emit("tauri://drag-enter", JsonValue.Parse(
            """{"paths":["C:/one.txt","C:/two.txt"],"position":{"x":1,"y":2}}"""), target);
        app.Emit("tauri://drag-over", JsonValue.Parse("""{"position":{"x":3,"y":4}}"""), target);
        app.Emit("tauri://drag-drop", JsonValue.Parse(
            """{"paths":["C:/three.txt"],"position":{"x":5,"y":6}}"""), target);
        app.Emit("tauri://drag-leave", JsonValue.Null, target);
    }

    private static void AssertDragSequence(IReadOnlyList<DragDropEvent> events)
    {
        var enter = Assert.IsType<DragEnterEvent>(events[0]);
        Assert.Equal(new[] { "C:/one.txt", "C:/two.txt" }, enter.Paths);
        Assert.Equal(new PhysicalPosition(1, 2), enter.Position);
        Assert.Equal(new PhysicalPosition(3, 4), Assert.IsType<DragOverEvent>(events[1]).Position);
        var dropped = Assert.IsType<DragDroppedEvent>(events[2]);
        Assert.Equal(new[] { "C:/three.txt" }, dropped.Paths);
        Assert.Equal(new PhysicalPosition(5, 6), dropped.Position);
        Assert.IsType<DragLeaveEvent>(events[3]);
    }

    private static App CreateApp() => new Builder()
        .UseRuntime(new EventRuntime())
        .Window("main", "Event test", "tauri://localhost/index.html")
        .Build();

    private sealed class EventRuntime : IDesktopRuntime
    {
        public void Run(RuntimeContext context) { }
        public void Dispatch(Action action) => action();
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonValue.Null);
        public Task<JsonElement> InvokeAsync(
            string command,
            JsonElement args,
            RuntimeCommandContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(JsonValue.Null);
        public void EvaluateScript(string webviewLabel, string script) { }
        public void Exit(int exitCode = 0) { }
    }
}
