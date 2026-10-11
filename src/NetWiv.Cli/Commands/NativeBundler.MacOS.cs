using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    internal static async Task<string> BuildMacAppAsync(ConfigDocument config, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS applications must be bundled on macOS.");
        var minimum = MacMinimumVersion(config);
        var identity = noSign ? "-" : config.String("bundle", "macOS", "signingIdentity") ?? Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY") ?? "-";
        var entitlements = config.String("bundle", "macOS", "entitlements");
        if (entitlements is not null)
        {
            entitlements = Path.GetFullPath(entitlements, config.ConfigDirectory);
            if (!File.Exists(entitlements)) throw new FileNotFoundException("macOS entitlements file is missing.", entitlements);
        }
        var directory = BundleDirectory(publishDirectory, "macos");
        var app = Path.Combine(directory, SafeName(Product(config)) + ".app");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        var contents = Path.Combine(app, "Contents");
        var executables = Path.Combine(contents, "MacOS");
        var resourcesDirectory = Path.Combine(contents, "Resources");
        Directory.CreateDirectory(executables);
        var payload = PayloadStager.Stage(publishDirectory, resourcesDirectory);
        var binaryRelative = Path.GetRelativePath(publishDirectory, binary);
        foreach (var relative in payload.Where(path => path == binaryRelative || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)))
        {
            var destination = Path.Combine(executables, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(Path.Combine(resourcesDirectory, relative), destination);
        }
        var appBinary = Path.Combine(executables, binaryRelative);
        PatchBundleType(appBinary, "MAC");
        File.SetUnixFileMode(appBinary, File.GetUnixFileMode(appBinary) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        using (var document = JsonDocument.Parse(config.Root.ToJsonString()))
        {
            var configured = document.RootElement.TryGetProperty("bundle", out var bundle) && bundle.TryGetProperty("resources", out var value) ? value : default;
            var expansion = ResourcePaths.Expand(configured, config.ConfigDirectory);
            if (expansion.Errors.Count > 0) throw new NetWivConfigException("Bundle resource expansion failed: " + string.Join("; ", expansion.Errors.Select(error => error.Message)));
            ResourceStager.Stage(expansion.Files, config.ConfigDirectory, resourcesDirectory);
        }
        var plist = MacInfoPlist(config, Path.GetFileName(appBinary), minimum);
        var icon = (config.Node("bundle", "icon") as JsonArray)?.Select(value => value?.GetValue<string>())
            .FirstOrDefault(value => value?.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) == true);
        if (icon is not null)
        {
            File.Copy(Path.GetFullPath(icon, config.ConfigDirectory), Path.Combine(resourcesDirectory, "icon.icns"));
            plist.Root!.Element("dict")!.Add(new XElement("key", "CFBundleIconFile"), new XElement("string", "icon.icns"));
        }
        plist.Save(Path.Combine(contents, "Info.plist"));
        // NativeAOT's linker signature is invalidated by the bundle marker patch.
        // Even --no-sign needs a fresh ad-hoc signature to execute on Apple Silicon.
        foreach (var library in Directory.EnumerateFiles(executables, "*.dylib", SearchOption.AllDirectories))
            await Sign(library, false);
        await Sign(appBinary, true);
        await Sign(app, true);
        await RequireSuccess("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], directory, cancellationToken);
        return app;

        async Task Sign(string path, bool executable)
        {
            var arguments = new List<string> { "--force", "--sign", identity };
            if (identity != "-") arguments.AddRange(["--timestamp", "--options", "runtime"]);
            if (executable && entitlements is not null) arguments.AddRange(["--entitlements", entitlements]);
            arguments.Add(path);
            await RequireSuccess("/usr/bin/codesign", arguments, directory, cancellationToken);
        }
    }

    internal static string MacMinimumVersion(ConfigDocument config)
    {
        if (config.Node("bundle", "macOS") is JsonObject mac)
        {
            foreach (var option in mac)
                if (option.Value is not null && option.Key is not ("minimumSystemVersion" or "signingIdentity" or "entitlements" or "bundleVersion"))
                    throw new NotSupportedException($"bundle.macOS.{option.Key} is not implemented by NetWiv's macOS bundler.");
        }
        var value = config.String("bundle", "macOS", "minimumSystemVersion") ?? "12.0";
        if (!System.Version.TryParse(value, out var parsed) || parsed < new System.Version(12, 0))
            throw new NetWivConfigException("NetWiv's .NET 10 macOS baseline requires minimumSystemVersion 12.0 or newer.");
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
        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), dictionary));
    }

    private static async Task<string> BuildDmgAsync(ConfigDocument config, BuildTarget target, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        var app = await BuildMacAppAsync(config, publishDirectory, binary, noSign, cancellationToken);
        var directory = BundleDirectory(publishDirectory, "dmg");
        var staging = FreshStaging(directory, "payload");
        await RequireSuccess("/usr/bin/ditto", [app, Path.Combine(staging, Path.GetFileName(app))], directory, cancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(staging, "Applications"), "/Applications");
        var artifact = Path.Combine(directory, $"{SafeName(Product(config))}_{Version(config)}_{target.Architecture}.dmg");
        await RequireSuccess("/usr/bin/hdiutil", ["create", "-ov", "-format", "UDZO", "-volname", Product(config), "-srcfolder", staging, artifact], directory, cancellationToken);
        return RequireArtifact(artifact);
    }
}
