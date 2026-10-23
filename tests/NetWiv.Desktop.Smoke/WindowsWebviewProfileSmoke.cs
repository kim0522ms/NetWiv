using NetWiv;
using NetWiv.Platform.Desktop;

internal static class WindowsWebviewProfileSmoke
{
    internal static int Run(string identifier, string expected, string next, string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This probe requires Windows.");
        var result = 1;
        var options = directory switch
        {
            "default" => default,
            "null" => JsonValue.Parse("""{"dataDirectory":null}"""),
            _ => JsonValue.Parse($$"""{"dataDirectory":{{JsonValue.From(directory).GetRawText()}}}""")
        };
        var builder = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets()
            .Window("main", "NetWiv browser profile probe", "webview-profile.html", extraConfig: options)
            .RegisterCommand("profile_config", (_, _, _) => ValueTask.FromResult(JsonValue.Parse(
                $$"""{"expected":{{JsonValue.From(expected).GetRawText()}},"next":{{JsonValue.From(next).GetRawText()}}}""")))
            .RegisterCommand("profile_result", (context, args, _) =>
            {
                if (args.TryGetProperty("error", out var error)) Console.Error.WriteLine("FAIL " + error.GetString());
                else { result = 0; Console.WriteLine("PASS browser profile persistence assertion"); }
                context.App.Exit(result);
                return ValueTask.FromResult(JsonValue.Null);
            });
        if (identifier != "unconfigured") builder.Metadata(new("Profile probe", "1.0", identifier));
        using var app = builder.Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;
    }
}
