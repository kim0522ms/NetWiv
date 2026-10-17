using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class MacAppStoreTests
{
    private const string Entitlements = """
        <plist version="1.0"><dict>
        <key>com.apple.security.app-sandbox</key><true/>
        <key>com.apple.security.network.client</key><true/>
        </dict></plist>
        """;
    private const string Profile = """
        <plist version="1.0"><dict>
        <key>ExpirationDate</key><date>2040-01-01T00:00:00Z</date>
        <key>Entitlements</key><dict>
        <key>com.apple.application-identifier</key><string>PREFIX.dev.netwiv.store</string>
        <key>com.apple.developer.team-identifier</key><string>TEAM</string>
        <key>com.apple.security.get-task-allow</key><false/>
        </dict></dict></plist>
        """;

    internal static async Task Configuration()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-store-config-").FullName;
        var variables = new[] { "APPLE_APP_STORE_SIGNING_IDENTITY", "APPLE_APP_STORE_INSTALLER_IDENTITY", "APPLE_APP_STORE_PROVISIONING_PROFILE", "APPLE_SIGNING_IDENTITY" };
        var previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, null);
            var config = CreateConfig(root);
            var target = BuildTarget.Resolve("osx-arm64");
            Check(NativeBundler.ParseFormats(config, target, null).SequenceEqual(new[] { "app-store" }));
            Check(NativeBundler.ParseFormats(config, target, "app,app-store").Count == 2);
            Throws<ArgumentException>(() => NativeBundler.ParseFormats(config, BuildTarget.Resolve("linux-x64"), "app-store"));
            config.Root["bundle"]!["targets"] = "all";
            Check(NativeBundler.ParseFormats(config, target, null).SequenceEqual(new[] { "app", "dmg" }));
            NativeBundler.ValidateConfiguration(config, target, "app-store", true);
            Environment.SetEnvironmentVariable("APPLE_SIGNING_IDENTITY", "Developer ID Application: Fixture");
            Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, false));
            var settings = MacAppStoreSettings.Load(config, true);
            Check(settings.SigningIdentity == "-" && settings.InstallerSigningIdentity is null);
            var mac = config.Root["bundle"]!["macOS"]!.AsObject();
            var store = new JsonObject
            {
                ["signingIdentity"] = "Apple Distribution: Fixture (TEAM)",
                ["installerSigningIdentity"] = "3rd Party Mac Developer Installer: Fixture (TEAM)",
                ["provisioningProfile"] = "fixture.provisionprofile"
            };
            mac["appStore"] = store;
            Throws<FileNotFoundException>(() => MacAppStoreSettings.Load(config, false));
            File.WriteAllText(Path.Combine(root, "fixture.provisionprofile"), "synthetic profile input");
            Check(MacAppStoreSettings.Load(config, false).ProvisioningProfile == Path.Combine(root, "fixture.provisionprofile"));
            store["signingIdentity"] = "Developer ID Application: Fixture";
            Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, false));
            store["signingIdentity"] = "3rd Party Mac Developer Application: Fixture";
            store["installerSigningIdentity"] = "Developer ID Installer: Fixture";
            Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, false));
            store["installerSigningIdentity"] = "3rd Party Mac Developer Installer: Fixture";
            _ = MacAppStoreSettings.Load(config, false);
            store["unknown"] = true;
            Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, true));
            store.Remove("unknown");
            store["signingIdentity"] = false;
            Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, true));
            mac.Remove("appStore");
            Environment.SetEnvironmentVariable(variables[0], "Apple Distribution: Fixture");
            Environment.SetEnvironmentVariable(variables[1], "3rd Party Mac Developer Installer: Fixture");
            Environment.SetEnvironmentVariable(variables[2], Path.Combine(root, "fixture.provisionprofile"));
            _ = MacAppStoreSettings.Load(config, false);
            foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, null);

            foreach (var malformed in new[]
            {
                "<plist><dict/></plist>", Entitlements.Replace("<true/>", "<false/>", StringComparison.Ordinal),
                Entitlements.Replace("<true/>", "<true>invalid</true>", StringComparison.Ordinal),
                Entitlements.Replace("</dict>", "<key>com.apple.security.get-task-allow</key><true/></dict>", StringComparison.Ordinal),
                "<plist><dict><key>duplicate</key><true/><key>duplicate</key><false/></dict></plist>",
                "<plist><dict><key>missing-value</key></dict></plist>", "<plist><array/></plist>", "not XML"
            })
            {
                File.WriteAllText(Path.Combine(root, "entitlements.plist"), malformed);
                Throws<NetWivConfigException>(() => MacAppStoreSettings.Load(config, true));
            }
            File.WriteAllText(Path.Combine(root, "entitlements.plist"), Entitlements);
            mac["infoPlist"] = "Info.plist";
            File.WriteAllText(Path.Combine(root, "Info.plist"), """
                <plist><dict><key>NSHumanReadableCopyright</key><string>Example &amp; contributors</string>
                <key>NSCameraUsageDescription</key><string>Scan a document</string>
                <key>ITSAppUsesNonExemptEncryption</key><false/>
                <key>CFBundleURLTypes</key><array><dict><key>CFBundleURLSchemes</key><array><string>example</string></array></dict></array>
                </dict></plist>
                """);
            var plist = NativeBundler.MacInfoPlist(config, "App", "12.0").Root!.Element("dict")!;
            Check(MacPropertyList.Get(plist, "NSHumanReadableCopyright")!.Value == "Example & contributors");
            Check(MacPropertyList.Get(plist, "ITSAppUsesNonExemptEncryption")!.Name == "false");
            Check(MacPropertyList.Get(plist, "CFBundleURLTypes")!.Descendants("string").Single().Value == "example");
            foreach (var key in new[] { "CFBundleIdentifier", "CFBundleExecutable", "CFBundleVersion", "CFBundleShortVersionString", "CFBundlePackageType", "CFBundleIconFile", "LSMinimumSystemVersion" })
            {
                File.WriteAllText(Path.Combine(root, "Info.plist"), $"<plist><dict><key>{key}</key><string>override</string></dict></plist>");
                Throws<NetWivConfigException>(() => NativeBundler.MacInfoPlist(config, "App", "12.0"));
            }
            mac.Remove("infoPlist");
            mac["minimumSystemVersion"] = false;
            Throws<NetWivConfigException>(() => NativeBundler.ValidateConfiguration(config, target, "app-store", true));
            mac.Remove("minimumSystemVersion");

            // Reject managed builds before the project or build hooks are touched.
            if (OperatingSystem.IsMacOS())
                await ThrowsAsync<NotSupportedException>(() => BuildCommand.RunAsync(["--bundles", "app-store", "--no-sign"], TextWriter.Null, root, CancellationToken.None));
        }
        finally
        {
            for (int i = 0; i < variables.Length; i++) Environment.SetEnvironmentVariable(variables[i], previous[i]);
            Directory.Delete(root, true);
        }
    }

    internal static Task ProfilesAndSigning()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entitlements = MacPropertyList.Parse(Entitlements);
        MacAppStoreSettings.ApplyProfile(MacPropertyList.Parse(Profile), entitlements, "dev.netwiv.store", now);
        var dictionary = entitlements.Root!.Element("dict")!;
        Check(MacPropertyList.Get(dictionary, "com.apple.application-identifier")!.Value == "PREFIX.dev.netwiv.store");
        Check(MacPropertyList.Get(dictionary, "com.apple.developer.team-identifier")!.Value == "TEAM");
        Check(dictionary.Elements("key").Count() == 4);
        foreach (var profile in new[]
        {
            Profile.Replace("2040-01-01", "2020-01-01", StringComparison.Ordinal),
            Profile.Replace("dev.netwiv.store", "dev.netwiv.other", StringComparison.Ordinal),
            Profile.Replace("dev.netwiv.store", "*", StringComparison.Ordinal),
            Profile.Replace("<false/>", "<true/>", StringComparison.Ordinal),
            Profile.Replace("<key>ExpirationDate", "<key>ProvisionedDevices</key><array/><key>ExpirationDate", StringComparison.Ordinal),
            Profile.Replace("<key>ExpirationDate", "<key>ProvisionsAllDevices</key><true/><key>ExpirationDate", StringComparison.Ordinal),
            Profile.Replace("com.apple.developer.team-identifier", "missing-team", StringComparison.Ordinal)
        })
            Throws<NetWivConfigException>(() => MacAppStoreSettings.ApplyProfile(MacPropertyList.Parse(profile), MacPropertyList.Parse(Entitlements), "dev.netwiv.store", now));
        MacPropertyList.Set(dictionary, "com.apple.developer.team-identifier", new XElement("string", "OTHER"));
        Throws<NetWivConfigException>(() => MacAppStoreSettings.ApplyProfile(MacPropertyList.Parse(Profile), entitlements, "dev.netwiv.store", now));
        var profileWithCertificate = MacPropertyList.Parse(Profile);
        MacPropertyList.Set(profileWithCertificate.Root!.Element("dict")!, "DeveloperCertificates", new XElement("array", new XElement("data", "AQID")));
        MacAppStoreSettings.ValidateSigningCertificate(profileWithCertificate, [1, 2, 3]);
        Throws<NetWivConfigException>(() => MacAppStoreSettings.ValidateSigningCertificate(profileWithCertificate, [4, 5, 6]));
        Throws<NetWivConfigException>(() => MacAppStoreSettings.ValidateSigningCertificate(MacPropertyList.Parse(Profile), [1, 2, 3]));

        var signed = NativeBundler.MacSignArguments("Apple Distribution: Fixture", "App with spaces.app", "entitlements.plist", true);
        Check(signed.SequenceEqual(new[] { "--force", "--sign", "Apple Distribution: Fixture", "--entitlements", "entitlements.plist", "App with spaces.app" }));
        Check(NativeBundler.MacSignArguments("Developer ID Application: Fixture", "App", null, false).Contains("runtime"));
        Check(!NativeBundler.MacSignArguments("-", "App", null, false).Contains("runtime"));
        Check(!NativeBundler.MacSignArguments("Apple Distribution: Fixture", "lib.dylib", null, true).Contains("--entitlements"));
        Check(NativeBundler.MacProductBuildArguments("App.app", "out.pkg", "3rd Party Mac Developer Installer: Fixture")
            .SequenceEqual(new[] { "--sign", "3rd Party Mac Developer Installer: Fixture", "--component", "App.app", "/Applications", "out.pkg" }));
        Check(!NativeBundler.MacProductBuildArguments("App.app", "out.pkg", null).Contains("--sign"));
        return Task.CompletedTask;
    }

    internal static async Task Packaging()
    {
        if (!OperatingSystem.IsMacOS()) { Console.WriteLine("SKIP macOS App Store packaging tools: macOS only"); return; }
        var root = Directory.CreateTempSubdirectory("netwiv store bundle-").FullName;
        try
        {
            var config = CreateConfig(root);
            var publish = Path.Combine(root, "publish"); Directory.CreateDirectory(publish);
            var binary = Path.Combine(publish, "App");
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"/>");
            var source = Path.Combine(root, "probe.c");
            File.WriteAllText(source, "#include <stdio.h>\nint main(void) { puts(\"__NETWIV_BUNDLE_TYPE_VAR_UNK\"); return 0; }\n");
            await Run("/usr/bin/cc", [source, "-o", binary], root);
            await Run("/usr/bin/cc", ["-dynamiclib", source, "-o", Path.Combine(publish, "libfixture.dylib")], root);
            var original = File.ReadAllBytes(binary);
            var target = BuildTarget.Resolve(null);
            await NativeBundler.BundleAsync(config, target, publish, "app-store", true, TextWriter.Null, CancellationToken.None);
            var directory = Path.Combine(publish, "bundle", "app-store");
            var package = Directory.GetFiles(directory, "*-unsigned.pkg").Single();
            var expanded = Path.Combine(root, "expanded");
            await Run("/usr/sbin/pkgutil", ["--expand-full", package, expanded], root);
            var app = Directory.GetDirectories(expanded, "*.app", SearchOption.AllDirectories).Single();
            await Run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], root);
            await Run("/usr/bin/codesign", ["--verify", "--strict", Path.Combine(app, "Contents/MacOS/libfixture.dylib")], root);
            var signed = await ProcessRunner.RunAsync(new ProcessSpec("/usr/bin/codesign", ["-d", "--entitlements", ":-", app], root, CaptureOutput: true));
            Check(signed.ExitCode == 0);
            var plist = MacPropertyList.Parse(signed.StandardOutput).Root!.Element("dict")!;
            Check(MacPropertyList.Get(plist, "com.apple.security.app-sandbox")!.Name == "true");
            Check((await Run(Path.Combine(app, "Contents/MacOS/App"), [], root)).Contains("__NETWIV_BUNDLE_TYPE_VAR_MAC", StringComparison.Ordinal));
            Check(!File.Exists(Path.Combine(app, "Contents/embedded.provisionprofile")));
            Check(original.SequenceEqual(File.ReadAllBytes(binary)));

            File.WriteAllText(Path.Combine(publish, "App.runtimeconfig.json"), "{}");
            await ThrowsAsync<NotSupportedException>(() => NativeBundler.BuildMacAppStoreAsync(config, target, publish, binary, true, CancellationToken.None));
            File.Delete(Path.Combine(publish, "App.runtimeconfig.json"));
            config.Root["bundle"]!["macOS"]!["appStore"] = new JsonObject { ["provisioningProfile"] = "invalid.provisionprofile" };
            File.WriteAllText(Path.Combine(root, "invalid.provisionprofile"), "not CMS");
            await ThrowsAsync<NetWivConfigException>(() => NativeBundler.BuildMacAppStoreAsync(config, target, publish, binary, true, CancellationToken.None));

            // An ephemeral self-signed CMS fixture exercises decoding/copying, not Apple trust.
            var profileXml = Path.Combine(root, "profile.plist");
            File.WriteAllText(profileXml, Profile);
            var certificate = Path.Combine(root, "fixture.pem");
            var key = Path.Combine(root, "fixture.key");
            var profile = Path.Combine(root, "fixture.provisionprofile");
            await Run("/usr/bin/openssl", ["req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", key, "-out", certificate, "-days", "1", "-subj", "/CN=NetWiv Synthetic Profile"], root);
            await Run("/usr/bin/openssl", ["smime", "-sign", "-binary", "-nodetach", "-outform", "DER", "-in", profileXml, "-signer", certificate, "-inkey", key, "-out", profile], root);
            var store = config.Root["bundle"]!["macOS"]!["appStore"]!.AsObject();
            store["provisioningProfile"] = "fixture.provisionprofile";
            await NativeBundler.BuildMacAppStoreAsync(config, target, publish, binary, true, CancellationToken.None);
            var bundled = Path.Combine(directory, "Store-Fixture.app");
            Check(File.ReadAllBytes(Path.Combine(bundled, "Contents/embedded.provisionprofile")).SequenceEqual(File.ReadAllBytes(profile)));
            var profileEntitlements = await Run("/usr/bin/codesign", ["-d", "--entitlements", ":-", bundled], root);
            Check(MacPropertyList.Get(MacPropertyList.Parse(profileEntitlements).Root!.Element("dict")!, "com.apple.application-identifier")!.Value == "PREFIX.dev.netwiv.store");
            store["signingIdentity"] = "Apple Distribution: NetWiv Missing Test Identity";
            store["installerSigningIdentity"] = "3rd Party Mac Developer Installer: NetWiv Missing Test Identity";
            await ThrowsAsync<InvalidOperationException>(() => NativeBundler.BuildMacAppStoreAsync(config, target, publish, binary, false, CancellationToken.None));
            Check(!Directory.GetFiles(directory, "*.pkg").Any(file => !file.EndsWith("-unsigned.pkg", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ConfigDocument CreateConfig(string root)
    {
        File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
            {"identifier":"dev.netwiv.store","productName":"Store Fixture","version":"1.0.0","mainBinaryName":"App",
             "build":{"dotnetProject":"App.csproj"},"bundle":{"targets":["app-store"],"category":"Utility","macOS":{"entitlements":"entitlements.plist"}}}
            """);
        File.WriteAllText(Path.Combine(root, "entitlements.plist"), Entitlements);
        return ConfigLoader.Load(root, "macos");
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("Mac App Store assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task<string> Run(string command, IReadOnlyList<string> args, string cwd)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec(command, args, cwd, CaptureOutput: true));
        if (result.ExitCode != 0) throw new Exception("macOS packaging tool failed: " + Path.GetFileName(command));
        return result.StandardOutput;
    }
}
