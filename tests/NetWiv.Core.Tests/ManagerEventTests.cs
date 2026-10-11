// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
// Ports manager::test::{emit, emit_to} from the pinned Tauri source.
using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class ManagerEventTests
{
    internal static Task Emit()
    {
        using var app = Create();
        var window = app.GetWindow("main-window")!;
        var webview = app.GetWebview("main-webview")!;
        var combined = app.GetWebviewWindow("main-webview-window")!;
        var received = new List<string>();
        Action<AppEvent> Listener(string name) => value =>
        { Assert.Equal("global-payload", value.Payload.GetString()); received.Add(name); };
        using var appListener = app.Listen("event", Listener("app"));
        using var appAny = app.ListenAny("event", Listener("app-any"));
        using var windowListener = window.Listen("event", Listener("window"));
        using var windowAny = window.ListenAny("event", Listener("window-any"));
        using var webviewListener = webview.Listen("event", Listener("webview"));
        using var webviewAny = webview.ListenAny("event", Listener("webview-any"));
        using var combinedListener = combined.Listen("event", Listener("combined"));
        using var combinedAny = combined.ListenAny("event", Listener("combined-any"));
        foreach (Action<string, JsonElement> emit in new Action<string, JsonElement>[] { app.Emit, window.Emit, webview.Emit, combined.Emit })
        {
            emit("event", JsonValue.From("global-payload"));
            Check(received, "app", "app-any", "window", "window-any", "webview", "webview-any", "combined", "combined-any");
        }
        return Task.CompletedTask;
    }

    internal static async Task EmitTo()
    {
        using var app = Create();
        var window = app.GetWindow("main-window")!;
        var webview = app.GetWebview("main-webview")!;
        var combined = app.GetWebviewWindow("main-webview-window")!;
        var received = new List<string>();
        Action<AppEvent> Listener(string name) => value =>
        { Assert.Equal("global-payload", value.Payload.GetString()); received.Add(name); };
        using var appListener = app.Listen("event", Listener("app"));
        using var windowListener = window.Listen("event", Listener("window"));
        using var webviewListener = webview.Listen("event", Listener("webview"));
        using var combinedListener = combined.Listen("event", Listener("combined"));
        Action<RuntimeEventTarget, string, JsonElement>[] emitters = [app.EmitTo, window.EmitTo, webview.EmitTo, combined.EmitTo];
        Action<string, string, JsonElement>[] labelEmitters = [app.EmitTo, window.EmitTo, webview.EmitTo, combined.EmitTo];
        for (var index = 0; index < emitters.Length; index++)
        {
            var payload = JsonValue.From("global-payload");
            emitters[index](RuntimeEventTarget.App, "event", payload); Check(received, "app");
            labelEmitters[index](window.Label, "event", payload); Check(received, "window");
            labelEmitters[index](webview.Label, "event", payload); Check(received, "webview");
            labelEmitters[index](combined.Label, "event", payload); Check(received, "combined");
            var other = await new WebviewWindowBuilder(app.Handle, "other-" + index).BuildAsync();
            using var otherListener = other.Listen("event", Listener("other"));
            labelEmitters[index](other.Label, "event", payload); Check(received, "other");
        }
    }

    internal static Task OnceAndUnlisten()
    {
        using var app = Create();
        var window = app.GetWindow("main-window")!;
        var webview = app.GetWebview("main-webview")!;
        var combined = app.GetWebviewWindow("main-webview-window")!;
        var received = new List<string>();
        Func<string, Action<AppEvent>, IDisposable>[] anyOnce = [app.OnceAny, app.Handle.OnceAny, window.OnceAny, webview.OnceAny, combined.OnceAny];
        foreach (var once in anyOnce)
        {
            using var subscription = once("once", _ => { received.Add("once"); app.EmitTo("unrelated", "once", JsonValue.Null); });
            app.EmitTo("unrelated", "once", JsonValue.Null);
            app.EmitTo(RuntimeEventTarget.App, "once", JsonValue.Null);
            Check(received, "once");
        }
        using var localOnce = app.Once("local-once", _ => received.Add("app-once"));
        app.EmitTo(window.Label, "local-once", JsonValue.Null); Check(received);
        app.EmitTo(RuntimeEventTarget.App, "local-once", JsonValue.Null);
        app.Emit("local-once", JsonValue.Null); Check(received, "app-once");

        Action<long>[] unlisten = [app.Unlisten, app.Handle.Unlisten, window.Unlisten, webview.Unlisten, combined.Unlisten];
        foreach (var remove in unlisten)
        {
            using var listener = app.Listen("self-remove", value => { received.Add("called"); remove(value.Id); });
            app.Emit("self-remove", JsonValue.Null); app.Emit("self-remove", JsonValue.Null);
            remove(long.MaxValue); Check(received, "called");
        }
        return Task.CompletedTask;
    }

    private static App Create() => new Builder().UseRuntime(new FakeRuntime())
        .Window(new WindowOptions("main-window", "Window", "tauri://localhost", 800, 600, JsonValue.EmptyObject,
            [new WebviewOptions("main-webview", "tauri://localhost", 0, 0, 800, 600, JsonValue.EmptyObject)]))
        .Window("main-webview-window", "Combined", "tauri://localhost").Build();

    private static void Check(List<string> received, params string[] expected)
    {
        Assert.True(received.Count == expected.Length && expected.All(received.Contains),
            "Unexpected event recipients: " + string.Join(',', received) + "; expected: " + string.Join(',', expected));
        received.Clear();
    }
}
