using NetWiv;
using NetWiv.Platform.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("FAIL this probe requires a Linux Wayland desktop.");
            return 1;
        }

        var runtime = new DesktopRuntime();
        var result = 1;
        using var app = new Builder().UseRuntime(runtime).UseEmbeddedAssets()
            .Window("main", "NetWiv Wayland interaction probe", "index.html", 900, 820)
            .Permission(new("allow-wayland-probe", "wayland_ready", "wayland_finish"))
            .Capability(new("wayland-probe", [
                "core:menu:allow-new", "core:menu:allow-set-as-window-menu",
                "core:tray:allow-new", "core:tray:allow-get-by-id",
                "core:image:allow-new", "core:resources:allow-close",
                "core:event:allow-listen", "core:event:allow-unlisten",
                "core:window:allow-inner-size", "core:window:allow-is-maximized",
                "core:window:allow-start-dragging", "allow-wayland-probe"
            ], Windows: ["main"]))
            .RegisterCommand("wayland_ready", (_, _, _) =>
            {
                if (runtime.Backend != "wayland")
                    throw new InvokeException("This probe requires DesktopRuntime.Backend == wayland.");
                Console.WriteLine("READY Wayland backend verified; complete the interactive checks in the window.");
                return ValueTask.FromResult(JsonValue.Null);
            })
            .RegisterCommand("wayland_finish", (context, args, _) =>
            {
                result = runtime.Backend == "wayland"
                    && args.TryGetProperty("passed", out var passed) && passed.ValueKind == System.Text.Json.JsonValueKind.True ? 0 : 1;
                Console.WriteLine(result == 0
                    ? "PASS Wayland input/menu/tray assertions and explicit manual visual confirmations; probe resources closed."
                    : "FAIL Wayland interaction probe did not complete all checks.");
                context.App.Exit(result);
                return ValueTask.FromResult(JsonValue.Null);
            }).Build();

        // An abandoned or blocked interactive run must not look successful.
        // Process termination also releases this process's native window/tray resources.
        using var watchdog = new Timer(_ =>
        {
            Console.WriteLine("FAIL Wayland interaction probe timed out.");
            Environment.Exit(124);
        }, null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        try { app.Run(); }
        catch (Exception)
        {
            // Avoid printing potentially identifying native error paths or environment details.
            Console.WriteLine("FAIL Wayland runtime could not complete the probe.");
            return 1;
        }
        return result;
    }
}
