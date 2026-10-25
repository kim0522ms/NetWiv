using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

// Interactive functional fixture: use an actual OS file-manager drag. Injected
// DOM/native events do not establish native drag delivery or coordinate accuracy.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var html = args.Contains("--html", StringComparer.Ordinal);
        var zoom = args.Contains("--zoom", StringComparer.Ordinal) ? 1.5 : 1.0;
        var result = 1;
        var passed = new HashSet<string>(StringComparer.Ordinal);
        var nativeDrops = new Dictionary<string, int>(StringComparer.Ordinal);
        var subscriptions = new List<IDisposable>();
        var options = JsonValue.Parse(html ? "{\"dragDropEnabled\":false}" : "{}");
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .Window("main", "NetWiv drag root", "drag.html", 560, 420, options)
            .Window(new WindowOptions("child-host", "NetWiv drag child", "drag.html", 640, 480, default,
                [new("child", "drag.html", 80, 60, 480, 360, options)]))
            .Permission(new("drag-fixture", "drag_config", "drag_result"))
            .Capability(new("drag-fixture", ["drag-fixture", "core:event:allow-listen", "core:event:allow-unlisten",
                "core:webview:allow-webview-size", "core:webview:allow-set-webview-zoom"], Windows: ["main", "child-host"]))
            .Setup(app =>
            {
                foreach (var window in app.Windows)
                    subscriptions.Add(window.OnDragDropEvent(value =>
                    {
                        if (value.Payload is not DragDroppedEvent || value.Label is not { } source) return;
                        lock (nativeDrops) nativeDrops[source] = nativeDrops.GetValueOrDefault(source) + 1;
                    }));
            })
            .RegisterCommand("drag_config", (_, _, _) => ValueTask.FromResult(JsonValue.Parse(
                "{\"html\":" + (html ? "true" : "false") + ",\"zoom\":" + zoom.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")))
            .RegisterCommand("drag_result", (context, value, _) =>
            {
                var label = context.WebviewLabel;
                var stage = value.GetProperty("stage").GetString();
                var parentReceived = html;
                lock (nativeDrops) parentReceived |= nativeDrops.GetValueOrDefault(label) == 1;
                var frontendPassed = value.GetProperty("passed").GetBoolean();
                if (!frontendPassed || !parentReceived)
                {
                    Console.Error.WriteLine($"FAIL drag {label}: " + (!frontendPassed ? stage : "parent Window native drop delivery"));
                    context.App.Exit(1);
                }
                else
                {
                    lock (passed)
                    {
                        passed.Add(label);
                        Console.WriteLine($"PASS drag {label}: {stage}, zoom {zoom}");
                        if (passed.Count == 2) { result = 0; context.App.Exit(0); }
                    }
                }
                return ValueTask.FromResult(JsonValue.Null);
            }).Build();
        using var timeout = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        app.Run();
        foreach (var subscription in subscriptions) subscription.Dispose();
        return result;
    }
}
