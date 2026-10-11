using NetWiv;
using NetWiv.Plugin.Autostart;

internal static class AutostartPluginTests
{
    internal static async Task Registration()
    {
        var name = "netwiv-autostart-test-" + Guid.NewGuid().ToString("N");
        var root = Directory.CreateTempSubdirectory("netwiv-autostart-").FullName;
        var original = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (OperatingSystem.IsLinux()) Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", root);
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime())
                .Plugin(new AutostartPlugin(name, "--from-autostart")).Build();
            app.Run();
            if (OperatingSystem.IsMacOS())
            {
                try { await app.Handle.InvokeAsync("plugin:autostart|is_enabled", JsonValue.EmptyObject); }
                catch (PlatformNotSupportedException) { return; }
                throw new Exception("Unimplemented macOS autostart registration must fail explicitly.");
            }
            Assert.True(!(await app.Handle.InvokeAsync("plugin:autostart|is_enabled", JsonValue.EmptyObject)).GetBoolean(),
                "A fresh autostart entry was already enabled.");
            if (!OperatingSystem.IsLinux()) return;
            await app.Handle.InvokeAsync("plugin:autostart|enable", JsonValue.EmptyObject);
            var entry = Path.Combine(root, "autostart", name + ".desktop");
            Assert.True(File.Exists(entry), "Autostart entry was not created.");
            Assert.Contains("--from-autostart", File.ReadAllText(entry));
            Assert.True((await app.Handle.InvokeAsync("plugin:autostart|is_enabled", JsonValue.EmptyObject)).GetBoolean(),
                "Autostart entry was not detected.");
            await app.Handle.InvokeAsync("plugin:autostart|disable", JsonValue.EmptyObject);
            Assert.True(!File.Exists(entry), "Autostart entry was not removed.");
        }
        finally
        {
            if (OperatingSystem.IsLinux()) Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", original);
            Directory.Delete(root, recursive: true);
        }
    }
}
