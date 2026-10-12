using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class WindowsInstallerTests
{
    internal static async Task Validation()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-installer-config-").FullName;
        try
        {
            foreach (var invalid in new[]
            {
                new JsonObject { ["shortcuts"] = new JsonObject { ["workingDirectory"] = string.Join("/", Enumerable.Repeat(new string('x', 100), 11)) } }.ToJsonString(),
                "{\"shortcuts\":{\"arguments\":[\"%PATH%\"]}}", "{\"shortcuts\":{\"arguments\":[\"50%\",\"25%\"]}}",
                "null", "[]", "true", "{\"scope\":null}", "{\"scope\":7}", "{\"scope\":\"both\"}",
                "{\"allowDirectorySelection\":\"yes\"}", "{\"unknown\":true}", "{\"shortcuts\":null}",
                "{\"shortcuts\":{\"other\":1}}", "{\"shortcuts\":{\"desktop\":1}}", "{\"shortcuts\":{\"startMenu\":null}}",
                "{\"shortcuts\":{\"arguments\":\"--test\"}}", "{\"shortcuts\":{\"arguments\":[null]}}", "{\"shortcuts\":{\"arguments\":[false]}}",
                "{\"shortcuts\":{\"arguments\":[\"a\\nline\"]}}", "{\"shortcuts\":{\"arguments\":[\"a\\u0000b\"]}}",
                "{\"shortcuts\":{\"workingDirectory\":\"../outside\"}}", "{\"shortcuts\":{\"workingDirectory\":\"C:\\\\outside\"}}",
                "{\"shortcuts\":{\"workingDirectory\":\"/absolute\"}}", "{\"shortcuts\":{\"workingDirectory\":\"a//b\"}}",
                "{\"shortcuts\":{\"name\":false}}", "{\"shortcuts\":{\"name\":\"\"}}", "{\"shortcuts\":{\"name\":\"NUL.txt\"}}",
                "{\"shortcuts\":{\"name\":\"COM1\"}}", "{\"shortcuts\":{\"name\":\"LPT²\"}}", "{\"shortcuts\":{\"name\":\"name.\"}}",
                "{\"shortcuts\":{\"name\":\"name \"}}", "{\"shortcuts\":{\"name\":\"a/b\"}}", "{\"shortcuts\":{\"name\":\"x\\t\"}}",
                new JsonObject { ["shortcuts"] = new JsonObject { ["arguments"] = new JsonArray(new string('x', 1000)) } }.ToJsonString(),
                new JsonObject { ["shortcuts"] = new JsonObject { ["name"] = new string('x', 201) } }.ToJsonString()
            })
            {
                File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"bundle\":{\"windows\":{\"installer\":" + invalid + "}}}");
                Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows"));
                // Both commands must reject config before finding a project, hooks, runner or packaging tools.
                foreach (var command in new[] { "build", "bundle" })
                {
                    var error = new StringWriter();
                    var result = await CliApp.RunAsync([command], TextWriter.Null, error, root);
                    Check(result != 0 && error.ToString().Contains("bundle.windows.installer"), error.ToString());
                }
            }
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"bundle\":{\"windows\":false}}");
            Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows"));
            foreach (var settings in new[] { "{}", "{\"scope\":\"currentUser\"}", "{\"scope\":\"perMachine\",\"shortcuts\":{\"workingDirectory\":\"data/files\",\"name\":\"작업 & 도구\",\"arguments\":[\"\",\"a\\\"b\",\"a\\\\\"]}}" })
            {
                var config = Config(root, settings);
                NativeBundler.ValidateConfiguration(config, BuildTarget.Resolve("win-x64"), null);
            }
            var resourceOnly = Config(root, "{}");
            resourceOnly.Root.Remove("productName");
            resourceOnly.Root["bundle"]!["targets"] = new JsonArray();
            NativeBundler.ValidateConfiguration(resourceOnly, BuildTarget.Resolve("win-x64"), null);
            var disabled = Config(root, "{\"shortcuts\":{\"desktop\":false,\"startMenu\":false,\"arguments\":[\"x\"]}}");
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(disabled, BuildTarget.Resolve("win-x64"), null));
            var msiDisabled = Config(root, "{\"shortcuts\":{\"startMenu\":false,\"name\":\"unused\"}}");
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(msiDisabled, BuildTarget.Resolve("win-x64"), "msi"));
            NativeBundler.ValidateConfiguration(msiDisabled, BuildTarget.Resolve("win-x64"), "nsis");
            NativeBundler.ValidateConfiguration(disabled, BuildTarget.Resolve("linux-x64"), "deb");
            Throws<ArgumentException>(() => NativeBundler.ValidateConfiguration(disabled, BuildTarget.Resolve("win-x64"), "deb"));
            var longName = Config(root, new JsonObject { ["shortcuts"] = new JsonObject { ["name"] = new string('a', 116) } }.ToJsonString());
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(longName, BuildTarget.Resolve("win-x64"), "msi"));
            NativeBundler.ValidateConfiguration(longName, BuildTarget.Resolve("win-x64"), "nsis");
            var mainName = Config(root, "{}");
            mainName.Root["mainBinaryName"] = "Valid";
            NativeBundler.ValidateConfiguration(mainName, BuildTarget.Resolve("win-x64"), "msi");
            mainName.Root["mainBinaryName"] = "../invalid";
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(mainName, BuildTarget.Resolve("win-x64"), "msi"));
            var badGuid = Config(root, "{}");
            badGuid.Root["bundle"]!["windows"]!["wix"] = new JsonObject { ["upgradeCode"] = "invalid" };
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(badGuid, BuildTarget.Resolve("win-x64"), "msi"));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static Task Rendering()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-installer-render-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Probe.exe"), "payload");
            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", "sample.txt"), "payload");
            File.WriteAllText(Path.Combine(root, "blocked"), "payload file");
            foreach (var directory in new[] { "blocked", "blocked/child" })
            {
                var conflict = Config(root, new JsonObject { ["shortcuts"] = new JsonObject { ["workingDirectory"] = directory } }.ToJsonString());
                Throws<NetWivConfigException>(() => NativeBundler.RenderNsis(conflict, root, "Probe.exe", "out.exe"));
                Throws<NetWivConfigException>(() => NativeBundler.RenderMsi(conflict, root, "Probe.exe"));
            }
            var defaults = Config(root, "{}");
            defaults.Root["bundle"]!["windows"]!.AsObject().Remove("installer");
            var legacyOptions = WindowsInstallerOptions.FromJson(defaults.Root);
            Check(!legacyOptions.PerMachine && !legacyOptions.AllowDirectorySelection && legacyOptions.Desktop is null && legacyOptions.StartMenu, "Legacy config defaults");
            var nsis = NativeBundler.RenderNsis(defaults, root, "Probe.exe", "out.exe");
            var msi = XDocument.Parse(NativeBundler.RenderMsi(defaults, root, "Probe.exe"));
            XNamespace w = "http://schemas.microsoft.com/wix/2006/wi";
            Check(nsis.Contains("InstallDir \"$LOCALAPPDATA\\Probe\"") && nsis.Contains("RequestExecutionLevel user"), "NSIS defaults");
            Check(!nsis.Contains("Page directory") && nsis.Contains("GetCommandLineW") && nsis.Contains("${GetOptions} $1 \"/D=\" $2"), "NSIS restore without changing UI default");
            Check(!nsis.Contains("RMDir /r") && nsis.Contains("Delete \"$INSTDIR\\data\\sample.txt\"") && nsis.Contains("RMDir \"$INSTDIR\\data\""), "Uninstall removes only owned files and empty directories");
            Check(nsis.Contains("Delete \"$0\\uninstall.exe\"") && nsis.Contains("RMDir \"$0\""), "Relocation removes the old in-place uninstaller after it exits");
            Check(nsis.Contains("FileScopedUninstall") && nsis.Contains("ExecWait") && nsis.Contains("_?=$0"), "Upgrade removes the previous owned payload without executing legacy recursive uninstallers");
            Check(nsis.Contains("IntCmp $0 1000 path_length_ok path_length_ok"), "Reject runtime path overflow before expansion");
            Check(nsis.Split("CreateShortcut").Length == 3, "NSIS both shortcuts by default");
            Check((string?)msi.Descendants(w + "Package").Single().Attribute("InstallScope") == "perUser", "MSI scope default");
            Check(msi.Descendants(w + "Shortcut").Count() == 1 && !msi.Descendants(w + "UIRef").Any(), "MSI default shortcuts/UI");
            Check((string?)msi.Descendants(w + "Product").Single().Attribute("Codepage") == "65001" && (string?)msi.Descendants(w + "Package").Single().Attribute("SummaryCodepage") == "1252", "WiX summary requires ANSI while the database preserves Unicode");
            var userUpgradeCode = (string?)msi.Descendants(w + "Product").Single().Attribute("UpgradeCode");
            foreach (var machine in new[] { false, true })
            foreach (var enabled in new[] { false, true })
            {
                var options = new JsonObject
                {
                    ["scope"] = machine ? "perMachine" : "currentUser", ["allowDirectorySelection"] = true,
                    ["shortcuts"] = new JsonObject { ["desktop"] = enabled, ["startMenu"] = enabled }
                };
                if (enabled)
                {
                    options["shortcuts"]!["name"] = "작업 & 도구";
                    options["shortcuts"]!["workingDirectory"] = "data/[work] $folder";
                    options["shortcuts"]!["arguments"] = new JsonArray("space value", "한글", "a\"b", "", "C:\\end\\", "$INSTDIR", "[INSTALLDIR]", "$(var.foo)", "!(wix.foo)", "$(loc.literal)", "!(literal)", "$(loc.x)", "!(loc.)", "${NSISDIR}", "50%", "$\\n", new string('x', 280));
                }
                var config = Config(root, options.ToJsonString());
                nsis = NativeBundler.RenderNsis(config, root, "Probe.exe", "out.exe");
                msi = XDocument.Parse(NativeBundler.RenderMsi(config, root, "Probe.exe"));
                var hive = machine ? "HKLM" : "HKCU";
                Check(nsis.Contains("RequestExecutionLevel " + (machine ? "admin" : "user")), "elevation");
                Check(nsis.Contains("ReadRegStr $0 " + hive + " \"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall") && nsis.Contains("DeleteRegKey " + hive), "NSIS scoped install/uninstall");
                Check(nsis.Split("SetShellVarContext " + (machine ? "all" : "current")).Length == 4, "NSIS context across phases");
                Check(nsis.Contains("Page directory"), "NSIS directory page");
                Check(nsis.Contains("ReadRegStr $0 HKCU \"Software\\Microsoft\\EdgeUpdate") != machine, "runtime prerequisite scope");
                Check(msi.Descendants(w + "RegistryValue").All(v => (string?)v.Attribute("Root") == hive), "MSI component hive");
                Check(msi.Descendants(w + "RegistrySearch").Any(v => (string?)v.Attribute("Id") == "WebView2User") != machine, "MSI prerequisite scope");
                Check(((string?)msi.Descendants(w + "Product").Single().Attribute("UpgradeCode") == userUpgradeCode) != machine, "MSI scope identity");
                Check(msi.Descendants(w + "SetProperty").Any(v => (string?)v.Attribute("Id") == "INSTALLDIR" && v.Value == "NOT INSTALLDIR AND EXISTINGINSTALLDIR"), "command-line path wins over upgrade path");
                Check(msi.Descendants(w + "UIRef").Any(), "MSI directory UI");
                Check(msi.Descendants(w + "Shortcut").Count() == (enabled ? 2 : 0), "MSI shortcut switches");
                Check(nsis.Split("CreateShortcut").Length == (enabled ? 3 : 1), "NSIS shortcut switches");
                if (enabled)
                {
                    Check(nsis.Contains("${U+24}${U+24}INSTDIR") && nsis.Contains("$\\\""), "NSIS literal arguments");
                    var shortcut = msi.Descendants(w + "Shortcut").First();
                    Check((string?)shortcut.Attribute("Name") == "작업 & 도구", "Unicode XML name");
                    Check((string?)shortcut.Attribute("Arguments") == "[NetWivShortcutArguments]", "MSI arguments avoid the 255-character table limit");
                    var arguments = (string)msi.Descendants(w + "Property").Single(p => (string?)p.Attribute("Id") == "NetWivShortcutArguments").Attribute("Value")!;
                    Check(arguments.Contains("[INSTALLDIR]") && arguments.Contains("$$(var.foo)") && arguments.Contains("!!(wix.foo)"), "MSI/WiX expression escaping");
                    Check(arguments.Contains("!$$(loc.literal)") && arguments.Contains("!(literal)"), "Literal WiX expressions");
                    Check(arguments.Contains("$$(loc.x)") && !arguments.Contains("!$$(loc.x)") && !arguments.Contains("!!(loc.)"), "Non-expressions keep their literal prefix");
                    Check(((string?)shortcut.Attribute("Advertise") == "yes") == machine, "Machine shortcuts use advertised file key paths");
                    Check((string?)shortcut.Attribute("WorkingDirectory") == "NETWIVWORKDIR", "MSI working directory property");
                    Check(msi.Descendants(w + "SetProperty").Any(p => (string?)p.Attribute("Id") == "NETWIVWORKDIR" && ((string?)p.Attribute("Value"))!.Contains(@"[\[]work[\]]")), "Literal directory brackets");
                    Check(msi.Descendants(w + "Directory").Any(d => (string?)d.Attribute("Name") == "[work] $$folder" && d.Descendants(w + "CreateFolder").Any()), "Create empty working directory");
                }
            }
            foreach (var (value, expected) in new[] { ("", "\"\""), ("a b", "\"a b\""), ("a\"b", "\"a\\\"b\""), ("a\\", "\"a\\\\\""), ("a\\\"b", "\"a\\\\\\\"b\"") })
                Check(WindowsInstallerOptions.QuoteArgument(value) == expected, "Win32 quoting: " + value);
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static ConfigDocument Config(string root, string settings)
    {
        File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"productName\":\"Probe\",\"identifier\":\"dev.netwiv.installer\",\"version\":\"1.0.0\",\"bundle\":{\"windows\":{\"installer\":" + settings + "}}}");
        return ConfigLoader.Load(root, "windows");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
