using NetWiv;
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Fs;
using NetWiv.Plugin.PersistedScope;

internal static class PersistedScopePluginTests
{
    internal static async Task RestoresDialogSelection()
    {
        var file = Path.Combine(Path.GetTempPath(), "netwiv-persisted-scope-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "selected");
        string? state = null;
        try
        {
            var runtime = new FakeRuntime { InvokeResult = JsonValue.From(file) };
            using (var first = Create(runtime))
            {
                state = await first.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppData, ".persisted-scope.netwiv.json");
                if (File.Exists(state)) File.Delete(state);
                first.Run();
                await first.Handle.InvokeAsync("plugin:dialog|open", JsonValue.Parse("""{"options":{}}"""));
                Assert.True(File.Exists(state), "Selected path was not persisted.");
            }
            using (var reopened = Create(new FakeRuntime()))
            {
                reopened.Run();
                Assert.True(reopened.AssetProtocolScope().IsAllowed(file), "Saved selection was not restored to the asset scope.");
            }
        }
        finally
        {
            if (state is not null && File.Exists(state)) File.Delete(state);
            File.Delete(file);
        }
    }

    private static App Create(FakeRuntime runtime) => new Builder().UseRuntime(runtime)
        .Plugin(new FsPlugin()).Plugin(new DialogPlugin()).Plugin(new PersistedScopePlugin())
        .UseEmbeddedConfig(assembly: typeof(PersistedScopePluginTests).Assembly, resourceName: "asset-config.json").Build();
}
