using NetWiv;
using NetWiv.Platform.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This smoke requires a macOS desktop.");
        var result = 1;
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .Window("main", "NetWiv menu and tray probe", "index.html", 850, 740)
            .Window("secondary", "NetWiv menu role secondary", "secondary.html", 360, 180)
            .Permission(new("allow-probe", "menu_tray_ready", "menu_tray_finish"))
            .Capability(new("menu-tray-probe", ["core:menu:default", "core:tray:default", "core:image:default", "core:resources:default", "core:window:default", "core:window:allow-set-title", "allow-probe"], Windows: ["main"]))
            .RegisterCommand("menu_tray_ready", (_, _, _) =>
            {
                Console.WriteLine("READY official guest menu/tray setup; awaiting native interaction and manual presentation checks");
                return ValueTask.FromResult(JsonValue.Null);
            })
            .RegisterCommand("menu_tray_finish", (context, args, _) =>
            {
                result = args.TryGetProperty("error", out var error) ? 1 : 0;
                Console.WriteLine(result == 0
                    ? "PASS official guest menu/tray input, cleanup and manually confirmed macOS roles/appearance"
                    : "FAIL menu/tray probe: " + error.GetString());
                context.App.Exit(result);
                return ValueTask.FromResult(JsonValue.Null);
            }).Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;
    }
}
