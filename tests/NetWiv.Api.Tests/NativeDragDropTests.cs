using System.Text.Json;
using NetWiv.Runtime;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed class NativeDragDropTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("child")]
    public async Task NativeDragTargetsTheReceivingViewAndParentWindowExactlyOnce(string source)
    {
        var runtime = new DragRuntime();
        using var app = new Builder().UseRuntime(runtime).Window("main", "Drag", "index.html")
            .Window("other", "Other", "index.html").Build();
        var window = app.Handle.GetWindow("main")!;
        var combined = app.Handle.GetWebviewWindow("main")!;
        var child = await new WebviewBuilder(app.Handle, window, "child").BuildAsync();
        var sibling = await new WebviewBuilder(app.Handle, window, "sibling").BuildAsync();
        var rootEvents = new List<DragDropEvent>();
        var childEvents = new List<DragDropEvent>();
        var windowEvents = new List<AppEvent<DragDropEvent>>();
        var webviewWindowEvents = new List<DragDropEvent>();
        var unrelated = 0;
        var any = 0;
        using var rootListener = app.Handle.GetWebview("main")!.OnDragDropEvent(value => rootEvents.Add(value.Payload));
        using var childListener = child.OnDragDropEvent(value => childEvents.Add(value.Payload));
        using var windowListener = window.OnDragDropEvent(windowEvents.Add);
        using var combinedListener = combined.OnDragDropEvent(value => webviewWindowEvents.Add(value.Payload));
        using var siblingListener = sibling.OnDragDropEvent(_ => unrelated++);
        using var otherListener = app.Handle.GetWindow("other")!.OnDragDropEvent(_ => unrelated++);
        using var globalListener = app.Handle.Events.Listen("tauri://drag-drop", _ => any++);

        // Register through the event command used by the frontend Window,
        // Webview and WebviewWindow classes; capture actual delivery scripts.
        foreach (var (kind, label, handler) in new[] {
            ("Webview", "main", 101), ("Webview", "child", 102),
            ("Window", "main", 103), ("WebviewWindow", "main", 104),
            ("Webview", "sibling", 105), ("Window", "other", 106), ("Any", "", 107) })
        {
            await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse(
                $$"""{"event":"tauri://drag-drop","target":{"kind":"{{kind}}","label":"{{label}}"},"handler":{{handler}}}"""));
        }
        app.Run();
        runtime.Scripts.Clear();
        var position = JsonValue.Parse("""{"position":{"x":80,"y":60}}""");
        var withPaths = JsonValue.Parse("""{"paths":["/fixture.txt"],"position":{"x":80,"y":60}}""");
        foreach (var (name, payload) in new[] { ("tauri://drag-enter", withPaths),
            ("tauri://drag-over", position), ("tauri://drag-drop", withPaths), ("tauri://drag-leave", JsonValue.Null) })
            runtime.Context!.OnEvent(new(name, source, payload, new("Webview", source)));

        Assert.Equal(source == "main" ? 4 : 0, rootEvents.Count);
        Assert.Equal(source == "child" ? 4 : 0, childEvents.Count);
        Assert.Equal(4, windowEvents.Count);
        Assert.All(windowEvents, value => Assert.Equal(source, value.Label));
        Assert.IsType<DragEnterEvent>(windowEvents[0].Payload);
        Assert.Equal(new PhysicalPosition(80, 60), Assert.IsType<DragOverEvent>(windowEvents[1].Payload).Position);
        Assert.Equal(new PhysicalPosition(80, 60), Assert.IsType<DragDroppedEvent>(windowEvents[2].Payload).Position);
        Assert.IsType<DragLeaveEvent>(windowEvents[3].Payload);
        Assert.Equal(4, webviewWindowEvents.Count);
        Assert.Equal(0, unrelated);
        Assert.Equal(1, any);
        foreach (var handler in new[] { 101, 102, 103, 104, 105, 106, 107 })
        {
            var expected = handler is 103 or 104 or 107 || handler == (source == "main" ? 101 : 102) ? 1 : 0;
            Assert.Equal(expected, runtime.Scripts.Count(script => script.Contains($"runCallback({handler},", StringComparison.Ordinal)));
        }
    }

    private sealed class DragRuntime : IDesktopRuntime
    {
        public RuntimeContext? Context;
        public List<string> Scripts { get; } = [];
        public void Run(RuntimeContext context) => Context = context;
        public void Dispatch(Action action) => action();
        public void EvaluateScript(string webviewLabel, string script) => Scripts.Add(script);
        public void Exit(int exitCode = 0) { }
        public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonValue.Null);
    }
}
