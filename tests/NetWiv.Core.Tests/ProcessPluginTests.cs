using NetWiv;
using NetWiv.Plugin.Process;

internal static class ProcessPluginTests
{
    internal static async Task ExitAndPermissions()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new ProcessPlugin()).Build();
        await app.Handle.InvokeAsync("plugin:process|exit", JsonValue.Parse("""{"code":17}"""));
        Assert.Equal(17, runtime.ExitCode);

        var definitions = new List<PermissionDefinition>();
        var plugin = new ProcessPlugin();
        var context = new PluginContext(plugin.Name, new CommandRegistry(), definitions, JsonValue.Null);
        plugin.Initialize(context);
        context.Complete();
        var acl = new AclAuthorizer(definitions,
            [new Capability("main", ["process:allow-exit"], Windows: ["main"])],
            new HashSet<string> { "tauri://localhost" }, false);
        Assert.True(acl.Authorize(new("plugin:process|exit", "main", "main", new Uri("tauri://localhost"))).Allowed,
            "Exit permission did not grant exit.");
        Assert.True(!acl.Authorize(new("plugin:process|restart", "main", "main", new Uri("tauri://localhost"))).Allowed,
            "Exit permission granted restart.");
    }
}
