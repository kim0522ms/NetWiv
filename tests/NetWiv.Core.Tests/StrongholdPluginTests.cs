using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Stronghold;

internal static class StrongholdPluginTests
{
    internal static async Task GuestContract()
    {
        JsonElement forwarded = default;
        var runtime = new FakeRuntime
        {
            InvokeHandler = (_, args) => { forwarded = args.Clone(); return JsonValue.Parse("[1,2,3]"); }
        };
        using var app = new Builder().UseRuntime(runtime)
            .Plugin(new StrongholdPlugin("test.salt")).Build();
        app.Run();
        var output = await app.Handle.InvokeAsync("plugin:stronghold|initialize",
            JsonValue.Parse("""{"snapshotPath":"test.hold","password":"secret"}"""));
        Assert.Equal("plugin:stronghold|initialize", runtime.LastCommand);
        Assert.Equal("test.salt", forwarded.GetProperty("saltPath").GetString());
        Assert.Equal("secret", forwarded.GetProperty("password").GetString());
        Assert.Equal(3, output.GetArrayLength());
        await app.Handle.InvokeAsync("plugin:stronghold|save_store_record",
            JsonValue.Parse("""{"snapshotPath":"test.hold","client":"app","key":"token","value":[1,2]}"""));
        Assert.Equal("plugin:stronghold|save_store_record", runtime.LastCommand);
        Assert.Equal("token", forwarded.GetProperty("key").GetString());
        try
        {
            await app.Handle.InvokeAsync("plugin:stronghold|save", JsonValue.EmptyObject);
            throw new Exception("Missing snapshotPath was accepted.");
        }
        catch (InvokeException) { }
    }
}
