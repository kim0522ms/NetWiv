using System.Text;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class WindowsInstallerIdentityTests
{
    internal static async Task Validation()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-installer-identity-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Probe.exe"), "compile-only payload");
            foreach (var invalid in new[] { "null", "\"\"", "\" \"", "\"\\t\"", "false", "1", "{}", "[]",
                "\"a\\\\b\"", "\"a/b\"", "\"a:b\"", "\"a b\"", "\"a\\n\"", "\"a\\u0000b\"", "\"앱\"" })
            {
                var config = Config(root);
                config.Root["identifier"] = JsonNode.Parse(invalid);
                Reject(config, root);
            }
            var missing = Config(root);
            missing.Root.Remove("identifier");
            Reject(missing, root);
            if (OperatingSystem.IsWindows())
            {
                foreach (var identifier in new[] { "", ",\"identifier\":null" })
                {
                    File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"productName\":\"Probe\",\"bundle\":{\"active\":true}" + identifier + "}");
                    foreach (var command in new[] { "build", "bundle" })
                    {
                        var error = new StringWriter();
                        var result = await CliApp.RunAsync([command], TextWriter.Null, error, root);
                        Check(result != 0 && error.ToString().Contains("Windows installers require a nonempty 'identifier'", StringComparison.Ordinal), "Installer identity must fail before project, hooks, or tools: " + error);
                    }
                }
            }
            // Missing identifiers remain valid for configurations that do not produce Windows installers.
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"productName\":\"Probe\",\"bundle\":{\"targets\":[]}}");
            var withoutInstaller = ConfigLoader.Load(root, "windows");
            NativeBundler.ValidateConfiguration(withoutInstaller, BuildTarget.Resolve("win-x64"), null);
            NativeBundler.ValidateConfiguration(withoutInstaller, BuildTarget.Resolve("linux-x64"), "deb");
            foreach (var identifier in new[] { "com.netwiv.app", "dev.netwiv.identity-1" })
            {
                var config = Config(root);
                config.Root["identifier"] = identifier;
                foreach (var format in new[] { "nsis", "msi" })
                    NativeBundler.ValidateConfiguration(config, BuildTarget.Resolve("win-x64"), format);
                var script = NativeBundler.RenderNsis(config, root, "Probe.exe", "out.exe");
                Check(script.Contains(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + identifier + "\""), "NSIS must use the complete application registry key.");
                Check(!script.Contains("DeleteRegKey HKCU \"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\\""), "NSIS must never remove the uninstall root.");
                _ = NativeBundler.RenderMsi(config, root, "Probe.exe");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Compilation()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP installer identity compilation: Windows only"); return; }
        var nsis = Environment.GetEnvironmentVariable("NETWIV_NSIS_PATH");
        var wix = Environment.GetEnvironmentVariable("NETWIV_WIX_DIRECTORY");
        if (nsis is null || wix is null) { Console.WriteLine("SKIP installer identity compilation: NSIS and WiX tool paths are required"); return; }
        var root = Directory.CreateTempSubdirectory("netwiv-installer-identity-compile-").FullName;
        try
        {
            var payload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            File.WriteAllText(Path.Combine(payload, "Probe.exe"), "compile-only payload; never executed");
            foreach (var scope in new[] { "currentUser", "perMachine" })
            {
                var config = Config(root);
                config.Root["bundle"] = new JsonObject { ["windows"] = new JsonObject { ["installer"] = new JsonObject { ["scope"] = scope } } };
                var script = Path.Combine(root, scope + ".nsi");
                var exe = Path.Combine(root, scope + ".exe");
                File.WriteAllText(script, NativeBundler.RenderNsis(config, payload, "Probe.exe", exe), new UTF8Encoding(true));
                await Compile(nsis, ["/V2", script], exe);
                var source = Path.Combine(root, scope + ".wxs");
                var obj = Path.Combine(root, scope + ".wixobj");
                var msi = Path.Combine(root, scope + ".msi");
                File.WriteAllText(source, NativeBundler.RenderMsi(config, payload, "Probe.exe"));
                await Compile(Path.Combine(wix, "candle.exe"), ["-nologo", "-arch", "x64", "-out", obj, source], obj);
                await Compile(Path.Combine(wix, "light.exe"), ["-nologo", "-out", msi, obj], msi);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task Compile(string tool, string[] args, string output)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec(tool, args, Path.GetDirectoryName(output)!, CaptureOutput: true));
        Check(result.ExitCode == 0 && File.Exists(output), "Installer identity compilation failed: " + result.StandardOutput + result.StandardError);
    }

    private static ConfigDocument Config(string root) => new("netwiv.conf.json", root, root, new JsonObject
    {
        ["identifier"] = "com.netwiv.app", ["productName"] = "Probe", ["version"] = "1.0.0"
    }, []);

    private static void Reject(ConfigDocument config, string payload)
    {
        foreach (var format in new[] { "nsis", "msi" })
            Throws(() => NativeBundler.ValidateConfiguration(config, BuildTarget.Resolve("win-x64"), format));
        Throws(() => NativeBundler.RenderNsis(config, payload, "Probe.exe", "out.exe"));
        Throws(() => NativeBundler.RenderMsi(config, payload, "Probe.exe"));
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (NetWivConfigException exception) when (exception.Message.Contains("identifier", StringComparison.Ordinal)) { return; }
        throw new Exception("Windows installers accepted an invalid identifier.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
