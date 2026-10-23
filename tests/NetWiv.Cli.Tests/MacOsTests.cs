using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class MacOsTests
{
    internal static Task Configuration()
    {
        foreach (var (name, rid, architecture) in new[] {
            ("aarch64-apple-darwin", "osx-arm64", "aarch64"), ("osx-arm64", "osx-arm64", "aarch64"),
            ("x86_64-apple-darwin", "osx-x64", "x86_64"), ("osx-x64", "osx-x64", "x86_64") })
        {
            var target = BuildTarget.Resolve(name);
            Check(target.RuntimeIdentifier == rid && target.Platform == "macos" && target.Architecture == architecture);
            var env = HookEnvironment.Create(target, false);
            Check(env["TAURI_ENV_PLATFORM"] == "darwin" && env["TAURI_ENV_FAMILY"] == "unix");
            if (OperatingSystem.IsMacOS()) target.EnsureHostOperatingSystem();
            else Throws<PlatformNotSupportedException>(target.EnsureHostOperatingSystem);
        }
        Throws<ArgumentException>(() => BuildTarget.Resolve("universal-apple-darwin"));
        Throws<PlatformNotSupportedException>(() => BuildTarget.Resolve("win-arm64"));
        Throws<PlatformNotSupportedException>(() => BuildTarget.Resolve("linux-arm64"));
        Check(HookEnvironment.Create(BuildTarget.Resolve("win-x64"), true)["TAURI_ENV_FAMILY"] == "windows");
        Check(HookEnvironment.Create(BuildTarget.Resolve("linux-x64"), false)["TAURI_ENV_PLATFORM"] == "linux");
        if (OperatingSystem.IsMacOS())
        {
            Check(BuildTarget.Resolve(null).Platform == "macos");
            Throws<PlatformNotSupportedException>(() => BuildTarget.Resolve("win-x64").EnsureHostOperatingSystem());
            Throws<PlatformNotSupportedException>(() => BuildTarget.Resolve("linux-x64").EnsureHostOperatingSystem());
        }
        var root = Directory.CreateTempSubdirectory("netwiv-macos-config-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mac","productName":"Base","version":"1.2.3","bundle":{"targets":"all","category":"DeveloperTool"}}""");
            File.WriteAllText(Path.Combine(root, "netwiv.macos.conf.json"), """{"productName":"Mac & <App>","bundle":{"macOS":{"minimumSystemVersion":"13.0","bundleVersion":"42"}}}""");
            var config = ConfigLoader.Load(root, "macos");
            Check(config.String("productName") == "Mac & <App>");
            var capabilities = JsonNode.Parse("""{"app":{"security":{"capabilities":[{"identifier":"mac-only","platforms":["macOS"],"windows":["main"],"permissions":["unresolved"]}]}}}""")!.AsObject();
            Directory.CreateDirectory(Path.Combine(root, "permissions"));
            File.WriteAllText(Path.Combine(root, "permissions/default.json"), """{"permission":[{"identifier":"known","commands":{"allow":["known"]}}]}""");
            Throws<NetWivConfigException>(() => EffectivePermissions.Apply(capabilities, root, "macos", []));
            EffectivePermissions.Apply(capabilities, root, "linux", []);
            var target = BuildTarget.Resolve("osx-arm64");
            Check(NativeBundler.ParseFormats(config, target, null).SequenceEqual(new[] { "app", "dmg" }));
            Check(NativeBundler.ParseFormats(config, target, "app,app").SequenceEqual(new[] { "app" }));
            Throws<ArgumentException>(() => NativeBundler.ParseFormats(config, target, "deb"));
            Throws<ArgumentException>(() => NativeBundler.ParseFormats(config, BuildTarget.Resolve("win-x64"), "app"));
            var plist = NativeBundler.MacInfoPlist(config, "my app", NativeBundler.MacMinimumVersion(config));
            var xml = XDocument.Parse(plist.ToString());
            Check(Entry(xml, "CFBundleName") == "Mac & <App>" && Entry(xml, "CFBundleExecutable") == "my app");
            Check(Entry(xml, "CFBundleVersion") == "42" && Entry(xml, "LSMinimumSystemVersion") == "13.0");
            var mac = (JsonObject)config.Root["bundle"]!["macOS"]!;
            foreach (var invalid in new[] { "11.0", "invalid", "" })
            {
                mac["minimumSystemVersion"] = invalid;
                Throws<NetWivConfigException>(() => NativeBundler.MacMinimumVersion(config));
            }
            mac.Remove("minimumSystemVersion");
            Check(NativeBundler.MacMinimumVersion(config) == "12.0");
            mac["frameworks"] = new JsonArray("Custom.framework");
            Throws<NotSupportedException>(() => NativeBundler.MacMinimumVersion(config));
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    internal static async Task Packaging()
    {
        if (!OperatingSystem.IsMacOS()) { Console.WriteLine("SKIP macOS packaging tools: macOS only"); return; }
        var root = Directory.CreateTempSubdirectory("netwiv mac bundle-").FullName;
        try
        {
            var publish = Path.Combine(root, "publish"); Directory.CreateDirectory(publish);
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(root, "data.txt"), "resource fixture");
            var starterIcon = Path.Combine(AppContext.BaseDirectory, "templates/react-app/src-netwiv/icons/icon.icns");
            File.Copy(starterIcon, Path.Combine(root, "icon.icns"));
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mac","productName":"Mac App","mainBinaryName":"App","version":"1.2.3","build":{"dotnetProject":"App.csproj"},"bundle":{"resources":["data.txt"],"icon":["icon.icns"]}}""");
            var config = ConfigLoader.Load(root, "macos");
            var source = Path.Combine(root, "probe.c");
            File.WriteAllText(source, "#include <stdio.h>\nint main(void) { puts(\"__NETWIV_BUNDLE_TYPE_VAR_UNK\"); return 0; }\n");
            var binary = Path.Combine(publish, "App");
            await Run("/usr/bin/cc", [source, "-o", binary], root);
            await Run("/usr/bin/cc", ["-dynamiclib", source, "-o", Path.Combine(publish, "libfixture.dylib")], root);
            var sidecarSource = Path.Combine(root, "helper.c");
            File.WriteAllText(sidecarSource, "#include <stdio.h>\nint main(void) { puts(\"helper fixture\"); return 0; }\n");
            var helper = Path.Combine(root, "helper-" + (BuildTarget.Resolve(null).Architecture == "aarch64" ? "aarch64" : "x86_64") + "-apple-darwin");
            await Run("/usr/bin/cc", [sidecarSource, "-o", helper], root);
            config.Root["bundle"]!["externalBin"] = new JsonArray("helper");
            var original = File.ReadAllBytes(binary);
            var app = await NativeBundler.BuildMacAppAsync(config, publish, binary, true, CancellationToken.None);
            Check(original.SequenceEqual(File.ReadAllBytes(binary)));
            Check(File.ReadAllText(Path.Combine(app, "Contents/Resources/data.txt")) == "resource fixture");
            Check(File.Exists(Path.Combine(app, "Contents/Resources/icon.icns")));
            Check(File.ReadAllBytes(starterIcon).SequenceEqual(File.ReadAllBytes(Path.Combine(app, "Contents/Resources/icon.icns"))));
            Check(Entry(XDocument.Load(Path.Combine(app, "Contents/Info.plist")), "CFBundleIconFile") == "icon.icns");
            var iconset = Path.Combine(root, "decoded.iconset");
            await Run("/usr/bin/iconutil", ["-c", "iconset", "-o", iconset, Path.Combine(app, "Contents/Resources/icon.icns")], root);
            Check(Directory.EnumerateFiles(iconset, "*.png").Any());
            await Run("/usr/bin/plutil", ["-lint", Path.Combine(app, "Contents/Info.plist")], root);
            var executed = await Run(Path.Combine(app, "Contents/MacOS/App"), [], root);
            Check(executed.Contains("__NETWIV_BUNDLE_TYPE_VAR_MAC", StringComparison.Ordinal));
            Check(!File.Exists(Path.Combine(app, "Contents/Resources/helper")));
            Check((await Run(Path.Combine(app, "Contents/MacOS/helper"), [], root)).Contains("helper fixture", StringComparison.Ordinal));
            await Run("/usr/bin/codesign", ["--verify", "--strict", Path.Combine(app, "Contents/MacOS/helper")], root);
            await NativeBundler.BundleAsync(config, BuildTarget.Resolve(null), publish, "dmg", true, TextWriter.Null, CancellationToken.None);
            var dmg = Directory.GetFiles(Path.Combine(publish, "bundle/dmg"), "*.dmg").Single();
            await Run("/usr/bin/hdiutil", ["verify", dmg], root);
            File.WriteAllText(Path.Combine(root, "entitlements.plist"), """<?xml version="1.0"?><plist version="1.0"><dict/></plist>""");
            config.Root["bundle"]!["macOS"] = new JsonObject { ["entitlements"] = "entitlements.plist" };
            config.Root["bundle"]!["icon"] = new JsonArray();
            await NativeBundler.BuildMacAppAsync(config, publish, binary, true, CancellationToken.None);
            // Signing and missing-marker errors must fail instead of reporting an artifact.
            config.Root["bundle"]!["macOS"] = new JsonObject { ["signingIdentity"] = "NetWiv missing test identity" };
            await ThrowsAsync<InvalidOperationException>(() => NativeBundler.BuildMacAppAsync(config, publish, binary, false, CancellationToken.None));
            config.Root["bundle"]!["macOS"] = new JsonObject { ["entitlements"] = "missing.plist" };
            await ThrowsAsync<FileNotFoundException>(() => NativeBundler.BuildMacAppAsync(config, publish, binary, true, CancellationToken.None));
            config.Root["bundle"]!.AsObject().Remove("macOS");
            File.WriteAllText(binary, "no marker");
            await ThrowsAsync<InvalidOperationException>(() => NativeBundler.BuildMacAppAsync(config, publish, binary, true, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string? Entry(XDocument document, string key) => document.Root!.Element("dict")!.Elements("key").Single(element => element.Value == key).ElementsAfterSelf().First().Value;
    private static void Check(bool condition) { if (!condition) throw new Exception("macOS assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task ThrowsAsync<T>(Func<Task<string>> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task<string> Run(string command, IReadOnlyList<string> args, string cwd)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec(command, args, cwd, CaptureOutput: true));
        if (result.ExitCode != 0) throw new Exception($"{command}: {result.StandardError}");
        return result.StandardOutput;
    }
}
