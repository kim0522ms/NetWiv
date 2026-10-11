using NetWiv;
using NetWiv.Runtime;

internal static class ManagerLookupTests
{
    internal static Task ParentChanges()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .Window("main", "Main", "index.html")
            .Window(new WindowOptions("bare", "Bare", "", 100, 100, default, []))
            .Build();
        var context = app.Handle.CreateRuntimeContext();
        var main = app.GetWebview("main")!;
        context.OnDocumentChanged!(new("main", 4, false));
        context.OnEvent(new("tauri://webview-created", null, JsonValue.Parse("""{"label":"bare","windowLabel":"bare"}""")));
        Check(app.GetWebviewWindow("bare") is not null);
        var events = 0;
        using var listener = app.Handle.Events.Listen("netwiv://webview-reparented", _ => events++);
        context.OnEvent(new("netwiv://webview-reparented", "main", JsonValue.Parse("""{"windowLabel":"bare"}""")));
        Check(main.Window.Label == "bare" && app.GetWebview("main")!.Window.Label == "bare");
        Check(app.GetWebviewWindow("main") is null && app.GetWebviewWindow("bare") is null && app.WebviewWindows.Count == 0);
        context.OnEvent(new("netwiv://webview-reparented", "main", JsonValue.Parse("""{"windowLabel":"main"}""")));
        Check(main.Window.Label == "main" && app.WebviewWindows.Count == 2 && events == 0);
        context.OnDocumentChanged(new("main", 4, true));
        context.OnEvent(new("netwiv://webview-reparented", "main", JsonValue.Parse("""{"windowLabel":"bare"}""")));
        Check(app.GetWebview("main") is null && app.WebviewWindows.Single().Label == "bare");
        return Task.CompletedTask;
    }

    internal static Task Lifecycle()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .Window("main", "Main", "index.html")
            .Window(new WindowOptions("bare", "Bare", "", 100, 100, default, []))
            .Window(new WindowOptions("host", "Host", "", 100, 100, default,
                [new WebviewOptions("bare", "index.html", 0, 0, 100, 100, default)]))
            .Build();
        Missing(app, "missing");
        Check(app.GetWindow("main") is Window and not WebviewWindow);
        Check(app.GetWebviewWindow("main") is not null);
        Check(ReferenceEquals(app.GetWebview("main"), app.GetWebviewWindow("main")!.Webview));
        Check(app.GetWindow("bare") is not null && app.GetWebview("bare")!.Window.Label == "host");
        Check(app.GetWebviewWindow("bare") is null && app.GetWebviewWindow("host") is null);
        var windows = app.Windows;
        var webviews = app.Webviews;
        var context = app.Handle.CreateRuntimeContext();
        context.OnDocumentChanged!(new("main", 1, false));
        Check(app.GetWebviewWindow("main") is not null);
        context.OnDocumentChanged(new("main", 2, false));
        Check(app.GetWebviewWindow("main") is not null);
        context.OnDocumentChanged(new("main", 2, true));
        Check(app.GetWindow("main") is not null && app.GetWebview("main") is null && app.GetWebviewWindow("main") is null);
        context.OnEvent(new("tauri://destroyed", "main", JsonValue.Null, new("Window", "main")));
        Missing(app, "main");
        context.OnEvent(new("tauri://destroyed", "bare", JsonValue.Null, new("Window", "bare")));
        // Destroying an unrelated same-label window must preserve the WebView.
        Check(app.GetWindow("bare") is null && app.GetWebview("bare")!.Window.Label == "host");
        context.OnDocumentChanged(new("bare", 1, true));
        Missing(app, "bare");
        Check(windows.Count == 3 && webviews.Count == 2 && app.Windows.Count == 1 && app.Webviews.Count == 0);
        return Task.CompletedTask;
    }

    private static void Missing(App app, string label) => Check(app.GetWindow(label) is null && app.GetWebview(label) is null &&
        app.GetWebviewWindow(label) is null && app.Handle.GetWindow(label) is null && app.Handle.GetWebview(label) is null && app.Handle.GetWebviewWindow(label) is null);
    private static void Check(bool value) { if (!value) throw new Exception("Manager lookup lifecycle mismatch."); }
}
