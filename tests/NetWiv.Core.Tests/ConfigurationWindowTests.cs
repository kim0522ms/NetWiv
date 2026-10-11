using NetWiv;

internal static class ConfigurationWindowTests
{
    internal static Task DefaultsAndDeferred()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig(
            assembly: typeof(ConfigurationWindowTests).Assembly, resourceName: "window-defaults.json").Build();
        app.Run();
        Assert.Equal(2, runtime.Context!.Windows.Count);
        var first = runtime.Context.Windows[0];
        Assert.Equal("main", first.Label);
        Assert.Equal("Tauri App", first.Title);
        Assert.Equal("index.html", first.Url);
        Assert.Equal(800d, first.Width);
        Assert.Equal(600d, first.Height);
        Assert.True(app.GetWindow("deferred") is null, "create:false made a startup window");
        var second = runtime.Context.Windows[1];
        Assert.Equal("named", second.Label);
        Assert.Equal("Chosen", second.Title);
        Assert.Equal(320d, second.Width);
        Assert.Equal(240d, second.Height);
        return Task.CompletedTask;
    }

    internal static Task EmptyAndProgrammatic()
    {
        foreach (var resource in new[] { "no-windows.json", "plugins-config.json" })
        {
            var runtime = new FakeRuntime();
            var builder = new Builder().UseRuntime(runtime).Window("before", "Before", "index.html")
                .UseEmbeddedConfig(assembly: typeof(ConfigurationWindowTests).Assembly, resourceName: resource);
            using (var app = builder.Build())
            {
                app.Run();
                // An explicit list replaces prior programmatic windows. An
                // omitted list does not discard explicitly requested windows.
                var expected = resource == "no-windows.json" ? 0 : 1;
                Assert.Equal(expected, runtime.Context!.Windows.Count);
                Assert.Equal(expected, app.Windows.Count);
                Assert.Equal(expected, app.Webviews.Count);
            }
            var emptyRuntime = new FakeRuntime();
            using (var empty = new Builder().UseRuntime(emptyRuntime).UseEmbeddedConfig(
                assembly: typeof(ConfigurationWindowTests).Assembly, resourceName: resource).Build())
            {
                empty.Run();
                Assert.Equal(0, emptyRuntime.Context!.Windows.Count);
            }
            // Configuration sets the initial list; subsequent explicit windows
            // remain available for applications that construct their own UI.
            var nextRuntime = new FakeRuntime();
            using var withWindow = new Builder().UseRuntime(nextRuntime).UseEmbeddedConfig(
                assembly: typeof(ConfigurationWindowTests).Assembly, resourceName: resource)
                .Window("after", "After", "index.html").Build();
            withWindow.Run();
            Assert.Equal(1, nextRuntime.Context!.Windows.Count);
            Assert.Equal("after", nextRuntime.Context.Windows[0].Label);
        }
        return Task.CompletedTask;
    }
}
