using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class WindowsNotificationSmoke
{
    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This probe requires Windows.");
        var result = 1;
        var runtime = new DesktopRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Metadata(new("NetWiv Plugin Smoke", "1.0.0", "dev.netwiv.plugin-smoke"))
            .UseEmbeddedConfig(resourceName: "netwiv.no-windows.json")
            .Setup(value => { _ = Probe(value.Handle); }).Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;

        async Task Probe(AppHandle handle)
        {
            try
            {
                var response = await runtime.InvokeAsync("plugin:notification|notify", JsonValue.Parse("""
                    {"identifier":"dev.netwiv.plugin-smoke","options":{"title":"NetWiv notification smoke","body":"Synthetic desktop notification"}}
                    """));
                if (response.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Notification completion was not null.");
                Console.WriteLine("PASS Windows native notification OS call accepted; banner visibility is not asserted");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + error.Message); }
            finally { handle.Exit(result); }
        }
    }
}
