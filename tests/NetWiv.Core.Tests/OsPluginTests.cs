using NetWiv;
using NetWiv.Plugin.Os;

internal static class OsPluginTests
{
    internal static async Task GuestContract()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new OsPlugin()).Build();
        app.Run();
        var script = runtime.Context!.InitializationScript;
        Assert.Contains("window.__TAURI_OS_PLUGIN_INTERNALS__=", script);
        Assert.Contains(OperatingSystem.IsWindows() ? "\"platform\":\"windows\"" : "\"platform\":\"linux\"", script);
        Assert.Equal(System.Globalization.CultureInfo.CurrentCulture.Name,
            (await app.Handle.InvokeAsync("plugin:os|locale", JsonValue.EmptyObject)).GetString());
        Assert.Equal(Environment.MachineName,
            (await app.Handle.InvokeAsync("plugin:os|hostname", JsonValue.EmptyObject)).GetString());
    }
}
