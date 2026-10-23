using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    private static void ValidateMacConfiguration(ConfigDocument config, IReadOnlyList<string> formats, bool noSign)
    {
        _ = MacInfoPlist(config, "app", MacMinimumVersion(config));
        if (formats.Contains("app-store")) _ = MacAppStoreSettings.Load(config, noSign);
    }

    internal static async Task<string> BuildMacAppAsync(ConfigDocument config, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken,
        MacAppStoreSettings? store = null, string? storeEntitlements = null, BuildTarget? target = null)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS applications must be bundled on macOS.");
        var minimum = MacMinimumVersion(config);
        var identity = store?.SigningIdentity ?? (noSign ? "-" : config.String("bundle", "macOS", "signingIdentity") ?? Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY") ?? "-");
        var entitlements = storeEntitlements ?? config.String("bundle", "macOS", "entitlements");
        if (entitlements is not null)
        {
            entitlements = Path.GetFullPath(entitlements, config.ConfigDirectory);
            if (!File.Exists(entitlements)) throw new FileNotFoundException("macOS entitlements file is missing.", entitlements);
        }
        var managed = File.Exists(Path.Combine(publishDirectory, Path.GetFileName(binary) + ".runtimeconfig.json"));
        if (store is not null && managed) throw new NotSupportedException("The app-store target requires NativeAOT publish output; rebuild with --aot.");
        var plist = MacInfoPlist(config, Path.GetFileName(binary), minimum);
        var directory = BundleDirectory(publishDirectory, store is null ? "macos" : "app-store");
        var app = Path.Combine(directory, SafeName(Product(config)) + ".app");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        var contents = Path.Combine(app, "Contents");
        var executables = Path.Combine(contents, "MacOS");
        var resourcesDirectory = Path.Combine(contents, "Resources");
        Directory.CreateDirectory(executables);
        // Keep managed runtime data in Resources. A launcher preserves the apphost's
        // sibling DLL/runtime resolution without treating JSON and DLLs as bundle code.
        var payload = PayloadStager.Stage(publishDirectory, resourcesDirectory);
        var sidecars = ExternalBinaryStager.Resolve(config, target ?? BuildTarget.Resolve(null), Path.GetFileName(binary));
        ExternalBinaryStager.Stage(sidecars, resourcesDirectory);
        // Managed apphosts execute from Resources; helpers are code and belong in MacOS.
        foreach (var sidecar in sidecars)
            File.Move(Path.Combine(resourcesDirectory, sidecar.DestinationName), Path.Combine(executables, sidecar.DestinationName));
        var binaryRelative = Path.GetRelativePath(publishDirectory, binary);
        foreach (var relative in payload.Where(path => !managed && (path == binaryRelative || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))))
        {
            var destination = Path.Combine(executables, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(Path.Combine(resourcesDirectory, relative), destination);
        }
        var appBinary = Path.Combine(executables, binaryRelative);
        var payloadBinary = managed ? Path.Combine(resourcesDirectory, binaryRelative) : appBinary;
        if (managed)
        {
            var relative = ("../Resources/" + binaryRelative).Replace("'", "'\"'\"'", StringComparison.Ordinal);
            File.WriteAllText(appBinary, "#!/bin/sh\nHERE=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd) || exit 1\nexec \"$HERE\"/'" + relative + "' \"$@\"\n");
        }
        PatchBundleType(payloadBinary, "MAC");
        File.SetUnixFileMode(appBinary, File.GetUnixFileMode(appBinary) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        using (var document = JsonDocument.Parse(config.Root.ToJsonString()))
        {
            var configured = document.RootElement.TryGetProperty("bundle", out var bundle) && bundle.TryGetProperty("resources", out var value) ? value : default;
            var expansion = ResourcePaths.Expand(configured, config.ConfigDirectory);
            if (expansion.Errors.Count > 0) throw new NetWivConfigException("Bundle resource expansion failed: " + string.Join("; ", expansion.Errors.Select(error => error.Message)));
            ResourceStager.Stage(expansion.Files, config.ConfigDirectory, resourcesDirectory);
        }
        if (store?.ProvisioningProfile is { } profile)
            File.Copy(profile, Path.Combine(contents, "embedded.provisionprofile"));
        var icon = (config.Node("bundle", "icon") as JsonArray)?.Select(value => value?.GetValue<string>())
            .FirstOrDefault(value => value?.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) == true);
        if (icon is not null)
        {
            File.Copy(Path.GetFullPath(icon, config.ConfigDirectory), Path.Combine(resourcesDirectory, "icon.icns"));
            MacPropertyList.Set(plist.Root!.Element("dict")!, "CFBundleIconFile", new XElement("string", "icon.icns"));
        }
        plist.Save(Path.Combine(contents, "Info.plist"));
        // NativeAOT's linker signature is invalidated by the bundle marker patch.
        // Even --no-sign needs a fresh ad-hoc signature to execute on Apple Silicon.
        foreach (var library in Directory.EnumerateFiles(managed ? resourcesDirectory : executables, "*.dylib", SearchOption.AllDirectories))
            await Sign(library, false);
        if (managed) await Sign(payloadBinary, true);
        foreach (var sidecar in sidecars) await Sign(Path.Combine(executables, sidecar.DestinationName), true);
        await Sign(appBinary, true);
        await Sign(app, true);
        await RequireSuccess("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], directory, cancellationToken);
        return app;

        async Task Sign(string path, bool executable)
        {
            var arguments = MacSignArguments(identity, path, executable ? entitlements : null, store is not null);
            await RequireSuccess("/usr/bin/codesign", arguments, directory, cancellationToken);
        }
    }

    internal static string MacMinimumVersion(ConfigDocument config)
    {
        if (config.Node("bundle", "macOS") is not null and not JsonObject)
            throw new NetWivConfigException("bundle.macOS must be an object.");
        if (config.Node("bundle", "macOS") is JsonObject mac)
        {
            foreach (var option in mac)
            {
                if (option.Value is not null && option.Key is not ("minimumSystemVersion" or "signingIdentity" or "entitlements" or "bundleVersion" or "infoPlist" or "appStore"))
                    throw new NotSupportedException($"bundle.macOS.{option.Key} is not implemented by NetWiv's macOS bundler.");
                if (option.Value is not null && option.Key != "appStore" &&
                    (option.Value is not JsonValue optionValue || !optionValue.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)))
                    throw new NetWivConfigException($"bundle.macOS.{option.Key} must be a nonempty string.");
            }
        }
        var value = config.String("bundle", "macOS", "minimumSystemVersion") ?? "12.0";
        if (!System.Version.TryParse(value, out var parsed) || parsed < new System.Version(12, 0))
            throw new NetWivConfigException("NetWiv's macOS baseline requires minimumSystemVersion 12.0 or newer.");
        return value;
    }

    internal static XDocument MacInfoPlist(ConfigDocument config, string executable, string minimum)
    {
        var values = new Dictionary<string, string>
        {
            ["CFBundleExecutable"] = executable,
            ["CFBundleIdentifier"] = config.String("identifier") ?? throw new NetWivConfigException("identifier is required for bundling."),
            ["CFBundleName"] = Product(config),
            ["CFBundleDisplayName"] = Product(config),
            ["CFBundlePackageType"] = "APPL",
            ["CFBundleShortVersionString"] = Version(config),
            ["CFBundleVersion"] = config.String("bundle", "macOS", "bundleVersion") ?? Version(config),
            ["LSMinimumSystemVersion"] = minimum
        };
        if (config.String("bundle", "category") is { } category)
            values["LSApplicationCategoryType"] = AppCategory.Parse(category).MacOSApplicationCategoryType;
        var dictionary = new XElement("dict", values.SelectMany(pair => new[] { new XElement("key", pair.Key), new XElement("string", pair.Value) }));
        dictionary.Add(new XElement("key", "NSHighResolutionCapable"), new XElement("true"));
        if (config.String("bundle", "macOS", "infoPlist") is { } infoPlist)
        {
            var additions = MacPropertyList.Load(Path.GetFullPath(infoPlist, config.ConfigDirectory)).Root!.Element("dict")!;
            foreach (var key in additions.Elements("key"))
            {
                if (key.Value is "CFBundleExecutable" or "CFBundleIdentifier" or "CFBundlePackageType" or "CFBundleShortVersionString" or "CFBundleVersion" or "LSMinimumSystemVersion" or "CFBundleIconFile")
                    throw new NetWivConfigException($"Info.plist cannot override generated {key.Value}; use the corresponding bundle configuration.");
                MacPropertyList.Set(dictionary, key.Value, key.ElementsAfterSelf().First());
            }
        }
        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), dictionary));
    }

    internal static IReadOnlyList<string> MacSignArguments(string identity, string path, string? entitlements, bool appStore)
    {
        var arguments = new List<string> { "--force", "--sign", identity };
        // Developer ID distribution uses hardened runtime; Store sandbox signing is separate.
        if (identity != "-" && !appStore) arguments.AddRange(["--timestamp", "--options", "runtime"]);
        if (entitlements is not null) arguments.AddRange(["--entitlements", entitlements]);
        arguments.Add(path);
        return arguments;
    }

    internal static IReadOnlyList<string> MacProductBuildArguments(string app, string package, string? installerIdentity)
    {
        var arguments = new List<string>();
        if (installerIdentity is not null) arguments.AddRange(["--sign", installerIdentity]);
        arguments.AddRange(["--component", app, "/Applications", package]);
        return arguments;
    }

    internal static async Task<string> BuildMacAppStoreAsync(ConfigDocument config, BuildTarget target, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("App Store packages must be bundled on macOS.");
        var settings = MacAppStoreSettings.Load(config, noSign);
        var temporary = Directory.CreateTempSubdirectory("netwiv-app-store-").FullName;
        try
        {
            XDocument? decodedProfile = null;
            if (settings.ProvisioningProfile is { } profile)
            {
                var decoded = await ProcessRunner.RunAsync(new ProcessSpec("/usr/bin/security", ["cms", "-D", "-i", profile], temporary, CaptureOutput: true), cancellationToken);
                if (decoded.ExitCode != 0) throw new NetWivConfigException("Cannot decode the Mac App Store provisioning profile.");
                decodedProfile = MacPropertyList.Parse(decoded.StandardOutput);
                MacAppStoreSettings.ApplyProfile(decodedProfile, settings.Entitlements,
                    config.String("identifier") ?? throw new NetWivConfigException("identifier is required for bundling."), DateTimeOffset.UtcNow);
            }
            var entitlements = Path.Combine(temporary, "entitlements.plist");
            settings.Entitlements.Save(entitlements);
            // AMFI's entitlement parser is stricter than a general XML parser
            // (for example, it rejects the XML writer's spaced empty elements).
            await RequireSuccess("/usr/bin/plutil", ["-convert", "xml1", entitlements], temporary, cancellationToken);
            var app = await BuildMacAppAsync(config, publishDirectory, binary, noSign, cancellationToken, settings, entitlements, target);
            var directory = Path.GetDirectoryName(app)!;
            if (!noSign && decodedProfile is not null)
            {
                var prefix = Path.Combine(temporary, "certificate");
                await RequireSuccess("/usr/bin/codesign", ["--display", "--extract-certificates", prefix, app], directory, cancellationToken);
                MacAppStoreSettings.ValidateSigningCertificate(decodedProfile, File.ReadAllBytes(prefix + "0"));
            }
            var package = Path.Combine(temporary, "application.pkg");
            await RequireSuccess("/usr/bin/productbuild", MacProductBuildArguments(app, package, settings.InstallerSigningIdentity), directory, cancellationToken);
            RequireArtifact(package);
            if (!noSign) await RequireSuccess("/usr/sbin/pkgutil", ["--check-signature", package], directory, cancellationToken);
            var artifact = Path.Combine(directory, $"{SafeName(Product(config))}_{Version(config)}_{target.Architecture}{(noSign ? "-unsigned" : "")}.pkg");
            File.Move(package, artifact, overwrite: true);
            return artifact;
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static async Task<string> BuildDmgAsync(ConfigDocument config, BuildTarget target, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        var app = await BuildMacAppAsync(config, publishDirectory, binary, noSign, cancellationToken, target: target);
        var directory = BundleDirectory(publishDirectory, "dmg");
        var staging = FreshStaging(directory, "payload");
        await RequireSuccess("/usr/bin/ditto", [app, Path.Combine(staging, Path.GetFileName(app))], directory, cancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(staging, "Applications"), "/Applications");
        var artifact = Path.Combine(directory, $"{SafeName(Product(config))}_{Version(config)}_{target.Architecture}.dmg");
        await RequireSuccess("/usr/bin/hdiutil", ["create", "-ov", "-format", "UDZO", "-volname", Product(config), "-srcfolder", staging, artifact], directory, cancellationToken);
        return RequireArtifact(artifact);
    }
}
