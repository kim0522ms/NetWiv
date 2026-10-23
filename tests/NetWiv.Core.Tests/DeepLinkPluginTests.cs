using NetWiv;
using NetWiv.Plugin.DeepLink;
using NetWiv.Plugin.SingleInstance;
using System.Text;

internal static class DeepLinkPluginTests
{
    internal static async Task NativeOpenedUrls()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .UseEmbeddedConfig(resourceName: "plugins-deep-link.json")
            .Plugin(new DeepLinkPlugin(["app"])).Build();
        app.Run();
        var calls = 0;
        using var listener = app.Handle.Events.Listen("deep-link://new-url", info =>
        {
            Assert.Equal(2, info.Payload.GetArrayLength());
            Assert.Equal("netwiv-test://first/path", info.Payload[0].GetString());
            Assert.Equal("netwiv-test://second/path", info.Payload[1].GetString());
            calls++;
        });
        var payload = JsonValue.Parse("""{"urls":["file:///test/selected.txt","netwiv-test://first/path","https://unconfigured.example/","netwiv-test://second/path"]}""");
        app.Handle.Emit("netwiv://opened", payload);
        Assert.Equal(0, calls);
        runtime.Context!.OnEvent(new("netwiv://opened", null, payload, NetWiv.Runtime.RuntimeEventTarget.App));
        Assert.Equal(1, calls);
        var current = await app.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
        Assert.Equal(2, current.GetArrayLength());
        Assert.Equal("netwiv-test://second/path", current[1].GetString());
        runtime.Context.OnEvent(new("netwiv://opened", null, JsonValue.Parse("""{"urls":["https://unconfigured.example/"]}""")));
        Assert.Equal(1, calls);
        current = await app.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
        Assert.Equal(2, current.GetArrayLength());
    }

    internal static async Task CurrentAndEvent()
    {
        var plugin = new DeepLinkPlugin(["app", "netwiv-test://initial/path"]);
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-deep-link.json")
            .Plugin(plugin).Build();
        app.Run();
        var current = await app.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
        Assert.Equal("netwiv-test://initial/path", current[0].GetString());
        var count = 0;
        using var listener = app.Handle.Events.Listen("deep-link://new-url", eventInfo =>
        {
            Assert.Equal("netwiv-test://next/path", eventInfo.Payload[0].GetString());
            count++;
        });
        plugin.HandleLaunchArguments(["app", "netwiv-test://next/path"]);
        Assert.Equal(1, count);
        plugin.HandleLaunchArguments(["app", "https://unconfigured.example/"]);
        plugin.HandleLaunchArguments(["app", "netwiv-test://ignored", "extra"]);
        Assert.Equal(1, count);
        current = await app.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
        Assert.Equal("netwiv-test://next/path", current[0].GetString());
        try
        {
            await app.Handle.InvokeAsync("plugin:deep-link|is_registered", JsonValue.Parse("""{"protocol":"../../bad"}"""));
            throw new Exception("An invalid protocol was accepted.");
        }
        catch (InvokeException) { }
    }

    internal static async Task Registration()
    {
        var protocol = "netwivtest" + Guid.NewGuid().ToString("N");
        var args = JsonValue.Parse("{\"protocol\":\"" + protocol + "\"}");
        var directory = Directory.CreateTempSubdirectory("netwiv-deep-link-").FullName;
        var originalData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        if (OperatingSystem.IsLinux())
        {
            var script = "#!/bin/sh\nexit 0\n";
            foreach (var tool in new[] { "xdg-mime", "update-desktop-database" })
            {
                var path = Path.Combine(directory, tool);
                File.WriteAllText(path, script, new UTF8Encoding(false));
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", directory);
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
        }
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new DeepLinkPlugin()).Build();
            app.Run();
            if (OperatingSystem.IsMacOS())
            {
                try { await app.Handle.InvokeAsync("plugin:deep-link|register", args); }
                catch (PlatformNotSupportedException) { return; }
                throw new Exception("Unimplemented macOS deep-link registration must fail explicitly.");
            }
            await app.Handle.InvokeAsync("plugin:deep-link|register", args);
            if (OperatingSystem.IsWindows())
            {
                Assert.True((await app.Handle.InvokeAsync("plugin:deep-link|is_registered", args)).GetBoolean(),
                    "The Windows protocol registration was not visible through HKCR.");
            }
            else if (OperatingSystem.IsLinux())
            {
                var file = Path.Combine(directory, "applications", Path.GetFileName(Environment.ProcessPath) + "-handler.desktop");
                Assert.Contains("x-scheme-handler/" + protocol, File.ReadAllText(file));
            }
            await app.Handle.InvokeAsync("plugin:deep-link|unregister", args);
            if (OperatingSystem.IsWindows())
                Assert.True(!(await app.Handle.InvokeAsync("plugin:deep-link|is_registered", args)).GetBoolean(),
                    "The Windows protocol registration was not removed.");
            else if (OperatingSystem.IsLinux())
            {
                var file = Path.Combine(directory, "applications", Path.GetFileName(Environment.ProcessPath) + "-handler.desktop");
                Assert.True(!File.ReadAllText(file).Contains("x-scheme-handler/" + protocol, StringComparison.Ordinal),
                    "The Linux desktop entry still declares the removed scheme.");
            }
        }
        finally
        {
            if (OperatingSystem.IsLinux())
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalData);
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static async Task SecondInstanceOpensUrl()
    {
        var identifier = "dev.netwiv.deep-link-" + Guid.NewGuid().ToString("N");
        using var first = new Builder().UseRuntime(new FakeRuntime())
            .UseEmbeddedConfig(resourceName: "plugins-deep-link.json")
            .Metadata(new AppMetadata("First", "1.0.0", identifier))
            .Plugin(new SingleInstancePlugin()).Plugin(new DeepLinkPlugin(["app"])).Build();
        first.Run();
        var opened = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = first.Handle.Events.Listen("deep-link://new-url", info =>
            opened.TrySetResult(info.Payload[0].GetString() ?? ""));
        using var second = new Builder().UseRuntime(new FakeRuntime())
            .Metadata(new AppMetadata("Second", "1.0.0", identifier))
            .Plugin(new SingleInstancePlugin(arguments: ["app", "netwiv-test://opened/second"])).Build();
        second.Run();
        Assert.Equal("netwiv-test://opened/second", await opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var current = await first.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject);
        Assert.Equal("netwiv-test://opened/second", current[0].GetString());
    }
}
