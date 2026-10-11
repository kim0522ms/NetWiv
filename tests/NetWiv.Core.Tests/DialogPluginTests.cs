using NetWiv;
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Fs;

internal static class DialogPluginTests
{
    internal static async Task SelectedFileScope()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-dialog-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "selected");
        try
        {
            var runtime = new FakeRuntime { InvokeResult = JsonValue.From(path) };
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin()).Plugin(new DialogPlugin())
                .UseEmbeddedConfig(assembly: typeof(DialogPluginTests).Assembly, resourceName: "asset-config.json").Build();
            app.Run();
            Assert.True(!app.AssetProtocolScope().IsAllowed(path), "Selection was already in the asset scope.");
            var selected = await app.Handle.InvokeAsync("plugin:dialog|open", JsonValue.Parse("""{"options":{}}"""));
            Assert.Equal(path, selected.GetString());
            Assert.Equal("plugin:dialog|open", runtime.LastCommand);
            Assert.True(app.AssetProtocolScope().IsAllowed(path), "Selected path was not granted to the asset scope.");
        }
        finally { File.Delete(path); }
    }

    internal static Task PermissionAliases()
    {
        var definitions = new List<PermissionDefinition>();
        var plugin = new DialogPlugin();
        var context = new PluginContext(plugin.Name, new CommandRegistry(), definitions, JsonValue.Null);
        plugin.Initialize(context);
        context.Complete();
        var capabilities = new[] { new Capability("main", ["dialog:allow-ask"], Windows: ["main"]) };
        var acl = new AclAuthorizer(definitions, capabilities, new HashSet<string> { "tauri://localhost" }, false);
        Assert.True(acl.Authorize(new("plugin:dialog|message", "main", "main", new Uri("tauri://localhost"))).Allowed,
            "Legacy ask permission did not authorize the message wire command.");
        Assert.True(!acl.Authorize(new("plugin:dialog|open", "main", "main", new Uri("tauri://localhost"))).Allowed,
            "Legacy ask permission granted file selection.");
        return Task.CompletedTask;
    }
}
