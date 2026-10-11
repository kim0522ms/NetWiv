using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Notification;

internal static class NotificationPluginTests
{
    internal static async Task GuestContract()
    {
        JsonElement native = default;
        var runtime = new FakeRuntime
        {
            InvokeHandler = (_, args) => { native = args.Clone(); return JsonValue.Null; }
        };
        using var app = new Builder().UseRuntime(runtime)
            .Metadata(new AppMetadata("Notice App", "1.0.0", "dev.netwiv.notice"))
            .Plugin(new NotificationPlugin()).Build();
        app.Run();
        Assert.Contains("window.Notification", runtime.Context!.InitializationScript);
        Assert.True((await app.Handle.InvokeAsync("plugin:notification|is_permission_granted", JsonValue.EmptyObject)).GetBoolean(),
            "Desktop notification permission was not granted.");
        Assert.Equal("granted", (await app.Handle.InvokeAsync("plugin:notification|request_permission", JsonValue.EmptyObject)).GetString());
        await app.Handle.InvokeAsync("plugin:notification|notify", JsonValue.Parse("""{"options":{"body":"hello"}}"""));
        Assert.Equal("plugin:notification|notify", runtime.LastCommand);
        Assert.Equal("dev.netwiv.notice", native.GetProperty("identifier").GetString());
        Assert.Equal("Notice App", native.GetProperty("options").GetProperty("title").GetString());
        Assert.Equal("hello", native.GetProperty("options").GetProperty("body").GetString());
        try
        {
            await app.Handle.InvokeAsync("plugin:notification|notify", JsonValue.Parse("""{"options":"bad"}"""));
            throw new Exception("Invalid notification options were accepted.");
        }
        catch (InvokeException) { }
    }
}
