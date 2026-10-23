using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class BundleSigningTests
{
    private const string Signature = "\nTEST-SIGNATURE:";

    internal static int Sign(string path)
    {
        var content = File.ReadAllText(path).Split(Signature)[0];
        File.WriteAllText(path, content + Signature + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        return 0;
    }

    private static void Verify(string path, bool signed)
    {
        var parts = File.ReadAllText(path).Split(Signature);
        if (!signed) { if (parts.Length != 1) throw new Exception("--no-sign signed a payload."); return; }
        if (parts.Length != 2 || parts[1] != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts[0]))))
            throw new Exception("Packager received an unsigned or modified-after-signing executable.");
    }

    internal static int Pack(string[] args)
    {
        var directory = Environment.CurrentDirectory;
        Verify(Path.Combine(directory, "payload", "Probe.exe"), Environment.GetEnvironmentVariable("NETWIV_BUNDLE_SIGNED") == "1");
        Verify(Path.Combine(directory, "payload", "helper.exe"), Environment.GetEnvironmentVariable("NETWIV_BUNDLE_SIGNED") == "1");
        string output;
        if (args.Contains("/V2")) output = Regex.Match(File.ReadAllText(args[^1]), "OutFile \"([^\"]+)\"").Groups[1].Value;
        else output = args[Array.IndexOf(args, "-out") + 1];
        if (Path.GetExtension(output) == ".msi")
        {
            var index = Array.IndexOf(args, "-loc");
            if (index < 0 || !File.ReadAllText(args[index + 1]).Contains("Codepage=\"65001\""))
                throw new Exception("MSI localization must preserve Unicode even with WixUIExtension.");
        }
        File.WriteAllText(output, "package");
        return 0;
    }

    internal static Task FinalPayload() => Bundle(false);
    internal static Task Installation() => Bundle(true);

    private static async Task Bundle(bool installationChecks)
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p1-sign-" + Guid.NewGuid().ToString("N"));
        var tools = Path.Combine(root, "tools"); Directory.CreateDirectory(tools);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldProbe = Environment.GetEnvironmentVariable("NETWIV_BUNDLE_PROBE");
        var oldSigned = Environment.GetEnvironmentVariable("NETWIV_BUNDLE_SIGNED");
        try
        {
            // Fake packaging tools execute the real bundler's generated commands, without a certificate or installer toolchain.
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
            var host = Path.Combine(AppContext.BaseDirectory, "NetWiv.Cli.Tests.exe");
            foreach (var name in new[] { "makensis.exe", "candle.exe", "light.exe" }) File.Copy(host, Path.Combine(tools, name));
            Environment.SetEnvironmentVariable("PATH", tools + Path.PathSeparator + oldPath);
            Environment.SetEnvironmentVariable("NETWIV_BUNDLE_PROBE", "1");
            var publish = Path.Combine(root, "publish"); Directory.CreateDirectory(publish);
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project />");
            var configPath = Path.Combine(root, "netwiv.conf.json");
            File.WriteAllText(configPath, new JsonObject
            {
                ["productName"] = "Probe", ["version"] = "1.0.0", ["identifier"] = "dev.netwiv.probe",
                ["build"] = new JsonObject { ["dotnetProject"] = "Probe.csproj" },
                ["bundle"] = new JsonObject { ["windows"] = new JsonObject { ["signCommand"] = new JsonObject
                { ["cmd"] = host, ["args"] = new JsonArray("--bundle-sign-probe", "%1") } } }
            }.ToJsonString());
            var config = ConfigLoader.Load(root, "windows", []);
            File.WriteAllText(Path.Combine(root, "helper-x86_64-pc-windows-msvc.exe"), "helper executable");
            config.Root["bundle"]!["externalBin"] = new JsonArray("helper");
            foreach (var noSign in new[] { false, true })
            {
                Environment.SetEnvironmentVariable("NETWIV_BUNDLE_SIGNED", noSign ? "0" : "1");
                File.WriteAllText(Path.Combine(publish, "Probe.exe"), "executable__NETWIV_BUNDLE_TYPE_VAR_UNK");
                await NativeBundler.BundleAsync(config, BuildTarget.Resolve("win-x64"), publish, "nsis,msi", noSign, TextWriter.Null, CancellationToken.None);
                Verify(Path.Combine(publish, "bundle", "nsis", "Probe_1.0.0_x64-setup.exe"), !noSign);
                Verify(Path.Combine(publish, "bundle", "msi", "Probe_1.0.0_x64_en-US.msi"), !noSign);
                if (File.ReadAllText(Path.Combine(root, "helper-x86_64-pc-windows-msvc.exe")) != "helper executable")
                    throw new Exception("Sidecar signing modified the configured source.");
                if (installationChecks)
                {
                    var nsis = File.ReadAllText(Path.Combine(publish, "bundle", "nsis", "installer.nsi"));
                    foreach (var required in new[] { "ReadRegStr", "EdgeUpdate", "0.0.0.0", "Function .onInit", "SetErrorLevel", "WriteRegStr HKCU", "UninstallString", "QuietUninstallString", "DeleteRegKey HKCU", "$SMPROGRAMS" })
                        if (!nsis.Contains(required)) throw new Exception("NSIS missing installation contract: " + required);
                    var xml = System.Xml.Linq.XDocument.Load(Path.Combine(publish, "bundle", "msi", "main.wxs"));
                    System.Xml.Linq.XNamespace wix = "http://schemas.microsoft.com/wix/2006/wi";
                    var searches = xml.Descendants(wix + "RegistrySearch").ToArray();
                    if (!searches.Any(item => (string?)item.Attribute("Root") == "HKLM") || !searches.Any(item => (string?)item.Attribute("Root") == "HKCU"))
                        throw new Exception("MSI did not search both runtime installation scopes.");
                    if (!xml.Descendants(wix + "Condition").Any(item => item.Value.Contains("Installed OR") && item.Value.Contains("0.0.0.0")))
                        throw new Exception("MSI must reject an absent runtime while allowing uninstall.");
                    if (!xml.Descendants(wix + "Shortcut").Any(item => (string?)item.Attribute("Target") == "[NETWIVTARGET]") || !xml.Descendants(wix + "RemoveFolder").Any(item => (string?)item.Attribute("Directory") == "ApplicationProgramsFolder"))
                        throw new Exception("MSI start-menu shortcut lifecycle is incomplete.");
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("NETWIV_BUNDLE_PROBE", oldProbe);
            Environment.SetEnvironmentVariable("NETWIV_BUNDLE_SIGNED", oldSigned);
            Directory.Delete(root, true);
        }
    }
}
