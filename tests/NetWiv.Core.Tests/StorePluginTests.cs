using NetWiv;
using NetWiv.Plugin.Store;

internal static class StorePluginTests
{
    internal static async Task Persistence()
    {
        var name = "netwiv-store-test-" + Guid.NewGuid().ToString("N") + ".json";
        using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new StorePlugin()).Build();
        var path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppData, name);
        try
        {
            var rid = (await app.Handle.InvokeAsync("plugin:store|load", JsonValue.Parse(
                "{\"path\":" + JsonValue.From(name).GetRawText() + ",\"options\":{\"autoSave\":false,\"defaults\":{\"answer\":1}}}"))).GetUInt32();
            var args = JsonValue.Parse($"{{\"rid\":{rid},\"key\":\"answer\"}}");
            Assert.Equal(1, (await app.Handle.InvokeAsync("plugin:store|get", args))[0].GetInt32());
            await app.Handle.InvokeAsync("plugin:store|set", JsonValue.Parse($"{{\"rid\":{rid},\"key\":\"answer\",\"value\":42}}"));
            Assert.True(!File.Exists(path), "autoSave:false persisted before save.");
            await app.Handle.InvokeAsync("plugin:store|save", JsonValue.Parse($"{{\"rid\":{rid}}}"));
            Assert.Contains("\"answer\":42", File.ReadAllText(path));
            var second = (await app.Handle.InvokeAsync("plugin:store|get_store", JsonValue.Parse("{\"path\":" + JsonValue.From(name).GetRawText() + "}"))).GetUInt32();
            Assert.Equal(42, (await app.Handle.InvokeAsync("plugin:store|get", JsonValue.Parse($"{{\"rid\":{second},\"key\":\"answer\"}}")))[0].GetInt32());
            File.WriteAllText(path, "{\"answer\":7}");
            await app.Handle.InvokeAsync("plugin:store|reload", JsonValue.Parse($"{{\"rid\":{rid},\"ignoreDefaults\":true}}"));
            Assert.Equal(7, (await app.Handle.InvokeAsync("plugin:store|get", args))[0].GetInt32());
            await app.Handle.InvokeAsync("plugin:store|reset", JsonValue.Parse($"{{\"rid\":{rid}}}"));
            Assert.Equal(1, (await app.Handle.InvokeAsync("plugin:store|get", args))[0].GetInt32());
            Assert.Equal(1, (await app.Handle.InvokeAsync("plugin:store|length", JsonValue.Parse($"{{\"rid\":{rid}}}"))).GetInt32());
            try
            {
                await app.Handle.InvokeAsync("plugin:store|load", JsonValue.Parse("""{"path":"../escape.json"}"""));
                throw new Exception("Store path traversal was accepted.");
            }
            catch (InvokeException) { }
        }
        finally { File.Delete(path); }
    }
}
