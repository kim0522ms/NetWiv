using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    private const string WebView2Client = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    public static async Task<int> BundleAsync(ConfigDocument config, BuildTarget target, string publishDirectory, string? requested, bool noSign, TextWriter output, CancellationToken cancellationToken)
    {
        ValidateConfiguration(config, target, requested);
        var formats = ParseFormats(config, target, requested);
        var project = CommandConfig.Project(config);
        var binary = BuildCommand.EnsureBinary(config, project, publishDirectory);
        using (var document = System.Text.Json.JsonDocument.Parse(config.Root.ToJsonString()))
        {
            var resources = document.RootElement.TryGetProperty("bundle", out var bundle) && bundle.TryGetProperty("resources", out var configured) ? configured : default;
            var expansion = ResourcePaths.Expand(resources, config.ConfigDirectory);
            if (expansion.Errors.Count > 0) throw new NetWivConfigException("Bundle resource expansion failed: " + string.Join("; ", expansion.Errors.Select(error => error.Message)));
            ResourceStager.Stage(expansion.Files, config.ConfigDirectory, publishDirectory);
        }
        foreach (var format in formats)
        {
            var artifact = format switch
            {
                "app" => await BuildMacAppAsync(config, publishDirectory, binary, noSign, cancellationToken),
                "dmg" => await BuildDmgAsync(config, target, publishDirectory, binary, noSign, cancellationToken),
                "nsis" => await BuildNsisAsync(config, publishDirectory, binary, noSign, cancellationToken),
                "msi" => await BuildMsiAsync(config, publishDirectory, binary, noSign, cancellationToken),
                "deb" => await BuildDebAsync(config, publishDirectory, binary, cancellationToken),
                "rpm" => await BuildRpmAsync(config, publishDirectory, binary, cancellationToken),
                "appimage" => await BuildAppImageAsync(config, publishDirectory, binary, cancellationToken),
                _ => throw new NotSupportedException($"Bundle target '{format}' is not available in the desktop runtime.")
            };
            if (!noSign && target.Platform == "windows") await SignWindowsAsync(config, artifact, cancellationToken);
            await output.WriteLineAsync($"Bundled {format}: {artifact}");
        }
        return 0;
    }

    internal static IReadOnlyList<string> ParseFormats(ConfigDocument config, BuildTarget target, string? requested)
    {
        IEnumerable<string> values;
        if (requested is not null) values = requested.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        else if (config.Node("bundle", "targets") is JsonArray array) values = array.Select(value => value?.GetValue<string>() ?? string.Empty);
        else if (config.String("bundle", "targets") is { } one) values = one == "all" ? Defaults(target.Platform) : [one];
        else values = Defaults(target.Platform);
        var result = values.Select(value => value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var allowed = Defaults(target.Platform);
        foreach (var value in result)
            if (!allowed.Contains(value, StringComparer.Ordinal)) throw new ArgumentException($"Bundle target '{value}' is not valid on {target.Platform}.");
        return result;
    }

    private static string[] Defaults(string platform) => platform switch
    {
        "windows" => ["msi", "nsis"],
        "linux" => ["deb", "rpm", "appimage"],
        "macos" => ["app", "dmg"],
        _ => throw new PlatformNotSupportedException($"Unsupported bundle platform: {platform}")
    };

    private static async Task<string> BuildNsisAsync(ConfigDocument config, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        EnsurePlatform(windows: true, "NSIS");
        var tool = FindTool("makensis.exe", "makensis") ?? throw Missing("NSIS", "makensis");
        var product = Product(config);
        var version = Version(config);
        var directory = BundleDirectory(publishDirectory, "nsis");
        var output = Path.Combine(directory, $"{SafeName(product)}_{version}_x64-setup.exe");
        var script = Path.Combine(directory, "installer.nsi");
        var payload = FreshStaging(directory, "payload");
        CopyTree(publishDirectory, payload);
        PatchBundleType(Path.Combine(payload, Path.GetRelativePath(publishDirectory, binary)), "NSS");
        if (!noSign) await SignWindowsAsync(config, Path.Combine(payload, Path.GetRelativePath(publishDirectory, binary)), cancellationToken);
        File.WriteAllText(script, RenderNsis(config, payload, Path.GetFileName(binary), output), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await RequireSuccess(tool, ["/V2", script], directory, cancellationToken);
        return RequireArtifact(output);
    }

    private static async Task<string> BuildMsiAsync(ConfigDocument config, string publishDirectory, string binary, bool noSign, CancellationToken cancellationToken)
    {
        EnsurePlatform(windows: true, "MSI");
        var candle = FindTool("candle.exe") ?? throw Missing("WiX Toolset v3", "candle.exe");
        var light = FindTool("light.exe") ?? throw Missing("WiX Toolset v3", "light.exe");
        var product = Product(config);
        var version = MsiVersion.Resolve(Version(config), config.String("bundle", "windows", "wix", "version"));
        var directory = BundleDirectory(publishDirectory, "msi");
        var wxs = Path.Combine(directory, "main.wxs");
        var wixobj = Path.Combine(directory, "main.wixobj");
        var output = Path.Combine(directory, $"{SafeName(product)}_{version}_x64_en-US.msi");
        var payload = FreshStaging(directory, "payload");
        CopyTree(publishDirectory, payload);
        PatchBundleType(Path.Combine(payload, Path.GetRelativePath(publishDirectory, binary)), "MSI");
        if (!noSign) await SignWindowsAsync(config, Path.Combine(payload, Path.GetRelativePath(publishDirectory, binary)), cancellationToken);
        File.WriteAllText(wxs, RenderMsi(config, payload, Path.GetFileName(binary)));
        await RequireSuccess(candle, ["-nologo", "-arch", "x64", "-out", wixobj, wxs], directory, cancellationToken);
        var lightArguments = new List<string> { "-nologo" };
        if (Environment.GetEnvironmentVariable("NETWIV_WIX_SUPPRESS_VALIDATION") == "1") lightArguments.Add("-sval");
        if (WindowsInstallerOptions.FromJson(config.Root).AllowDirectorySelection) lightArguments.AddRange(["-ext", "WixUIExtension"]);
        lightArguments.AddRange(["-out", output, wixobj]);
        await RequireSuccess(light, lightArguments, directory, cancellationToken);
        return RequireArtifact(output);
    }

    private static async Task<string> BuildDebAsync(ConfigDocument config, string publishDirectory, string binary, CancellationToken cancellationToken)
    {
        EnsurePlatform(windows: false, "Debian package");
        var tool = FindTool("dpkg-deb") ?? throw Missing("Debian packaging", "dpkg-deb");
        var product = SafeName(Product(config)).ToLowerInvariant();
        var version = Version(config);
        var directory = BundleDirectory(publishDirectory, "deb");
        var output = Path.Combine(directory, $"{product}_{version}_amd64.deb");
        var staging = TemporaryBundleDirectory("deb");
        try
        {
            var root = Path.Combine(staging, "root"); Directory.CreateDirectory(root);
            var appDir = Path.Combine(root, "usr", "lib", product);
            CopyTree(publishDirectory, appDir);
            var appBinary = Path.Combine(appDir, Path.GetRelativePath(publishDirectory, binary));
            PatchBundleType(appBinary, "DEB");
            var binDir = Path.Combine(root, "usr", "bin"); Directory.CreateDirectory(binDir);
            var launcher = Path.Combine(binDir, product);
            WriteUnixExecutable(launcher, $"#!/bin/sh\nexec /usr/lib/{product}/{Path.GetFileName(binary)} \"$@\"\n");
            var applications = Path.Combine(root, "usr", "share", "applications"); Directory.CreateDirectory(applications);
            File.WriteAllText(Path.Combine(applications, product + ".desktop"), DesktopEntry(config, product));
            InstallLinuxIcon(config, root, product);
            var debian = Path.Combine(root, "DEBIAN"); Directory.CreateDirectory(debian);
            File.WriteAllText(Path.Combine(debian, "control"), $"Package: {product}\nVersion: {version}\nArchitecture: amd64\nMaintainer: {Publisher(config)}\nDepends: libwebkit2gtk-4.1-0, libgtk-3-0\nDescription: {Description(config)}\n");
            NormalizeUnixTree(root, appBinary, launcher);
            await RequireSuccess(tool, ["--root-owner-group", "--build", root, output], directory, cancellationToken);
            return RequireArtifact(output);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static async Task<string> BuildRpmAsync(ConfigDocument config, string publishDirectory, string binary, CancellationToken cancellationToken)
    {
        EnsurePlatform(windows: false, "RPM package");
        var tool = FindTool("rpmbuild") ?? throw Missing("RPM packaging", "rpmbuild");
        var product = SafeName(Product(config)).ToLowerInvariant();
        var version = Version(config);
        var directory = BundleDirectory(publishDirectory, "rpm");
        var staging = TemporaryBundleDirectory("rpm");
        try
        {
            var top = Path.Combine(staging, "rpmbuild");
            foreach (var item in new[] { "BUILD", "BUILDROOT", "RPMS", "SOURCES", "SPECS", "SRPMS" }) Directory.CreateDirectory(Path.Combine(top, item));
            var source = Path.Combine(top, "SOURCES", product); CopyTree(publishDirectory, source);
            var appBinary = Path.Combine(source, Path.GetRelativePath(publishDirectory, binary));
            PatchBundleType(appBinary, "RPM");
            File.WriteAllText(Path.Combine(source, product + ".desktop"), DesktopEntry(config, product));
            var rpmIcon = ResolveIcon(config);
            var rpmIconName = rpmIcon is null ? null : product + Path.GetExtension(rpmIcon);
            if (rpmIcon is not null) File.Copy(rpmIcon, Path.Combine(source, rpmIconName!), true);
            NormalizeUnixTree(source, appBinary);
            var rpmIconInstall = rpmIconName is null ? string.Empty : $"mkdir -p $RPM_BUILD_ROOT/usr/share/icons/hicolor/scalable/apps\ncp \"{source}/{rpmIconName}\" $RPM_BUILD_ROOT/usr/share/icons/hicolor/scalable/apps/{rpmIconName}";
            var rpmIconFiles = rpmIconName is null ? string.Empty : $"/usr/share/icons/hicolor/scalable/apps/{rpmIconName}";
            var spec = Path.Combine(top, "SPECS", product + ".spec");
            File.WriteAllText(spec, $"""
Name: {product}
Version: {version}
Release: 1
Summary: {Description(config)}
License: {config.String("bundle", "license") ?? "MIT OR Apache-2.0"}
BuildArch: x86_64
Requires: webkit2gtk4.1, gtk3

%description
{Description(config)}

%install
mkdir -p $RPM_BUILD_ROOT/usr/lib/{product} $RPM_BUILD_ROOT/usr/bin $RPM_BUILD_ROOT/usr/share/applications
cp -a "{source}/." $RPM_BUILD_ROOT/usr/lib/{product}/
ln -s /usr/lib/{product}/{Path.GetFileName(binary)} $RPM_BUILD_ROOT/usr/bin/{product}
cp "{source}/{product}.desktop" $RPM_BUILD_ROOT/usr/share/applications/{product}.desktop
{rpmIconInstall}

%files
/usr/lib/{product}
/usr/bin/{product}
/usr/share/applications/{product}.desktop
{rpmIconFiles}
""");
            await RequireSuccess(tool, ["-bb", "--define", $"_topdir {top}", spec], directory, cancellationToken);
            var built = Directory.EnumerateFiles(Path.Combine(top, "RPMS"), "*.rpm", SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidOperationException("rpmbuild completed without producing an RPM artifact.");
            var output = Path.Combine(directory, Path.GetFileName(built)); File.Copy(built, output, true);
            return output;
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static async Task<string> BuildAppImageAsync(ConfigDocument config, string publishDirectory, string binary, CancellationToken cancellationToken)
    {
        EnsurePlatform(windows: false, "AppImage");
        var tool = FindTool("appimagetool") ?? throw Missing("AppImage", "appimagetool");
        var product = SafeName(Product(config));
        var version = Version(config);
        var directory = BundleDirectory(publishDirectory, "appimage");
        var output = Path.Combine(directory, $"{product}_{version}_amd64.AppImage");
        var runtime = Environment.GetEnvironmentVariable("NETWIV_APPIMAGE_RUNTIME_FILE")
            ?? throw new NotSupportedException("Deterministic AppImage bundling requires NETWIV_APPIMAGE_RUNTIME_FILE to name a pinned x86_64 runtime.");
        if (!File.Exists(runtime)) throw new FileNotFoundException($"Pinned AppImage runtime not found: {runtime}");
        var staging = TemporaryBundleDirectory("appimage");
        try
        {
            var root = Path.Combine(staging, product + ".AppDir"); Directory.CreateDirectory(root);
            var appDir = Path.Combine(root, "usr", "lib", product); CopyTree(publishDirectory, appDir);
            var appBinary = Path.Combine(appDir, Path.GetRelativePath(publishDirectory, binary));
            PatchBundleType(appBinary, "APP");
            var binDir = Path.Combine(root, "usr", "bin"); Directory.CreateDirectory(binDir);
            var appRun = Path.Combine(root, "AppRun");
            WriteUnixExecutable(appRun, $"#!/bin/sh\nHERE=\"$(dirname \"$(readlink -f \"$0\")\")\"\nexec \"$HERE/usr/lib/{product}/{Path.GetFileName(binary)}\" \"$@\"\n");
            var launcherName = product.ToLowerInvariant();
            var launcher = Path.Combine(binDir, launcherName);
            WriteUnixExecutable(launcher, $"#!/bin/sh\nHERE=\"$(dirname \"$(readlink -f \"$0\")\")\"\nexec \"$HERE/../lib/{product}/{Path.GetFileName(binary)}\" \"$@\"\n");
            File.WriteAllText(Path.Combine(root, product + ".desktop"), DesktopEntry(config, launcherName));
            var icon = ResolveIcon(config) ?? throw new NetWivConfigException("AppImage bundling requires bundle.icon to contain an existing desktop icon.");
            File.Copy(icon, Path.Combine(root, launcherName + Path.GetExtension(icon)), true);
            NormalizeUnixTree(root, appBinary, appRun, launcher);
            await RequireSuccess(tool, ["--runtime-file", runtime, root, output], directory, cancellationToken);
            return RequireArtifact(output);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static async Task SignWindowsAsync(ConfigDocument config, string path, CancellationToken cancellationToken)
    {
        if (config.Node("bundle", "windows", "signCommand") is JsonObject custom)
        {
            var command = custom["cmd"]?.GetValue<string>() ?? throw new NetWivConfigException("bundle.windows.signCommand.cmd is required.");
            var customArguments = custom["args"] is JsonArray array ? array.Select(item => (item?.GetValue<string>() ?? string.Empty).Replace("%1", path, StringComparison.Ordinal)).ToArray() : [];
            if (!customArguments.Any(argument => argument.Contains(path, StringComparison.Ordinal))) throw new NetWivConfigException("bundle.windows.signCommand.args must contain the %1 file placeholder.");
            await RequireSuccess(command, customArguments, config.ProjectDirectory, cancellationToken);
            return;
        }
        var thumbprint = config.String("bundle", "windows", "certificateThumbprint");
        if (thumbprint is null) return;
        var signtool = FindSignTool() ?? throw Missing("Windows code signing", "signtool.exe");
        var digest = config.String("bundle", "windows", "digestAlgorithm") ?? "sha256";
        var args = new List<string> { "sign", "/sha1", thumbprint, "/fd", digest };
        if (config.String("bundle", "windows", "timestampUrl") is { } timestamp)
            args.AddRange(config.Boolean("bundle", "windows", "tsp") == true ? ["/tr", timestamp, "/td", digest] : ["/t", timestamp]);
        args.Add(path);
        await RequireSuccess(signtool, args, config.ProjectDirectory, cancellationToken);
    }

    private static async Task RequireSuccess(string command, IReadOnlyList<string> arguments, string cwd, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec(command, arguments, cwd, CaptureOutput: true), cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(command)} exited with code {result.ExitCode}: {(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim()}");
    }

    private static string? FindTool(params string[] names)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names)
            {
                var path = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    private static string? FindSignTool()
    {
        var direct = FindTool("signtool.exe");
        if (direct is not null) return direct;
        var kits = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var root = Path.Combine(kits, "Windows Kits", "10", "bin");
        return Directory.Exists(root) ? Directory.EnumerateFiles(root, "signtool.exe", SearchOption.AllDirectories).OrderDescending().FirstOrDefault() : null;
    }

    private static Exception Missing(string feature, string tool) => new NotSupportedException($"{feature} bundling requires '{tool}' on PATH. No artifact was created.");
    private static void EnsurePlatform(bool windows, string feature) { if (windows != OperatingSystem.IsWindows()) throw new PlatformNotSupportedException($"{feature} must be built on {(windows ? "Windows" : "Linux")}."); }
    private static string BundleDirectory(string publish, string format) { var path = Path.Combine(publish, "bundle", format); Directory.CreateDirectory(path); return path; }
    private static string Product(ConfigDocument config) => config.String("productName") ?? throw new NetWivConfigException("productName is required for bundling.");
    private static string Version(ConfigDocument config) => config.String("version") ?? throw new NetWivConfigException("version is required for bundling.");
    private static string Description(ConfigDocument config) => config.String("bundle", "shortDescription") ?? Product(config);
    private static string Publisher(ConfigDocument config) => config.String("bundle", "publisher") ?? "NetWiv application publisher";
    private static string SafeName(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
    internal static string EscapeNsis(string value) => value.Replace("$", "$$", StringComparison.Ordinal).Replace("\"", "$\\\"", StringComparison.Ordinal);
    private static string Xml(string value) => SecurityElement.Escape(value) ?? string.Empty;
    private static string UpgradeCode(ConfigDocument config) => config.String("bundle", "windows", "wix", "upgradeCode") ?? Uuid5(Product(config) + ".exe.app.x64");
    internal static string Uuid5(string value)
    {
        var ns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray(); Swap(ns);
        var name = Encoding.UTF8.GetBytes(value); var input = new byte[ns.Length + name.Length]; ns.CopyTo(input, 0); name.CopyTo(input, ns.Length);
        var hash = SHA1.HashData(input); hash[6] = (byte)((hash[6] & 0x0f) | 0x50); hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var bytes = hash[..16]; Swap(bytes); return new Guid(bytes).ToString().ToUpperInvariant();
    }
    private static void Swap(byte[] bytes) { Array.Reverse(bytes, 0, 4); Array.Reverse(bytes, 4, 2); Array.Reverse(bytes, 6, 2); }
    private static string RequireArtifact(string path) => File.Exists(path) ? path : throw new InvalidOperationException($"Packaging tool completed without producing expected artifact: {path}");
    internal static void PatchBundleType(string binary, string bundleType)
    {
        if (bundleType.Length != 3 || bundleType.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Bundle type marker must be exactly three uppercase ASCII characters.", nameof(bundleType));
        // Managed apphosts contain no framework metadata: patch the staged Core assembly.
        var directory = Path.GetDirectoryName(binary)!;
        var name = Path.GetFileNameWithoutExtension(binary);
        if (!OperatingSystem.IsWindows()) name = Path.GetFileName(binary);
        if (File.Exists(Path.Combine(directory, name + ".runtimeconfig.json")))
            binary = Path.Combine(directory, "NetWiv.Core.dll");
        var marker = Encoding.ASCII.GetBytes("__NETWIV_BUNDLE_TYPE_VAR_UNK");
        var replacement = Encoding.ASCII.GetBytes("__NETWIV_BUNDLE_TYPE_VAR_" + bundleType);
        var bytes = File.ReadAllBytes(binary);
        var match = -1;
        for (var index = 0; index <= bytes.Length - marker.Length; index++)
        {
            if (!bytes.AsSpan(index, marker.Length).SequenceEqual(marker)) continue;
            if (match >= 0) throw new InvalidOperationException($"Published payload contains multiple NetWiv bundle type markers: {binary}");
            match = index;
        }
        if (match < 0) throw new InvalidOperationException($"Published payload does not contain the NetWiv bundle type marker: {binary}");
        replacement.CopyTo(bytes, match);
        File.WriteAllBytes(binary, bytes);
    }
    private static string TemporaryBundleDirectory(string format)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var path = Path.Combine(Path.GetTempPath(), $"netwiv-{format}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
    private static void NormalizeUnixTree(string root, params string[] executables)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        const UnixFileMode directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        const UnixFileMode fileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
            File.SetUnixFileMode(directory, directoryMode);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetUnixFileMode(file, fileMode);
        foreach (var executable in executables) File.SetUnixFileMode(executable, directoryMode);
    }
    private static string FreshStaging(string parent, string name)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(parent, name));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException($"Staging path escapes its bundle directory: {path}");
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static void CopyTree(string source, string destination)
    {
        PayloadStager.Stage(source, destination);
    }
    private static void WriteUnixExecutable(string path, string contents) { if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException(); File.WriteAllText(path, contents.Replace("\r\n", "\n", StringComparison.Ordinal)); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
    internal static string DesktopEntry(ConfigDocument config, string executable)
    {
        var category = config.String("bundle", "category");
        var categories = category is null ? string.Empty : AppCategory.Parse(category).FreedesktopCategories;
        return $"[Desktop Entry]\nType=Application\nName={Product(config)}\nExec={executable}\nIcon={executable}\nTerminal=false\nCategories={categories}\n";
    }

    private static string? ResolveIcon(ConfigDocument config)
    {
        IEnumerable<string> values = config.Node("bundle", "icon") switch
        {
            JsonArray array => array.Select(item => item?.GetValue<string>() ?? string.Empty),
            JsonValue value when value.TryGetValue<string>(out var icon) => [icon],
            _ => []
        };
        return values.Select(value => Path.GetFullPath(value, config.ConfigDirectory)).FirstOrDefault(File.Exists);
    }

    private static void InstallLinuxIcon(ConfigDocument config, string root, string product)
    {
        if (ResolveIcon(config) is not { } icon) return;
        var destination = Path.Combine(root, "usr", "share", "icons", "hicolor", "scalable", "apps");
        Directory.CreateDirectory(destination);
        File.Copy(icon, Path.Combine(destination, product + Path.GetExtension(icon)), true);
    }

    private sealed class WixDirectoryNode(string relativePath)
    {
        private readonly SortedDictionary<string, WixDirectoryNode> _children = new(StringComparer.Ordinal);
        private readonly List<(string Source, string Id, string Guid)> _files = [];

        public void Add(string relative, string source, string id, string guid)
        {
            var directory = Path.GetDirectoryName(relative);
            var owner = string.IsNullOrEmpty(directory) ? this : AddDirectory(directory);
            owner._files.Add((source, id, guid));
        }

        public WixDirectoryNode AddDirectory(string relative)
        {
            var owner = this;
            foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!owner._children.TryGetValue(part, out var child)) owner._children[part] = child = new WixDirectoryNode(Path.Combine(owner.RelativePath, part));
                owner = child;
            }
            return owner;
        }

        public string Render(StringBuilder refs, string registryKey, string directoryId, string hive, bool perMachine)
        {
            var result = new StringBuilder();
            foreach (var file in _files.OrderBy(file => file.Source, StringComparer.Ordinal))
            {
                result.Append($"<Component Id=\"C{file.Id}\" Guid=\"{file.Guid}\" Win64=\"{(perMachine ? "yes" : "no")}\"><File Id=\"{file.Id}\" Source=\"{WixXml(file.Source)}\" /><RegistryValue Root=\"{hive}\" Key=\"{Xml(registryKey)}\" Name=\"{file.Id}\" Type=\"integer\" Value=\"1\" KeyPath=\"yes\" /></Component>");
            }
            var cleanupId = "X" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RelativePath)))[..16];
            refs.AppendLine($"<ComponentRef Id=\"C{cleanupId}\" />");
            result.Append($"<Component Id=\"C{cleanupId}\" Guid=\"{Uuid5(registryKey + ":cleanup:" + RelativePath)}\" Win64=\"{(perMachine ? "yes" : "no")}\"><CreateFolder /><RemoveFolder Id=\"R{cleanupId}\" Directory=\"{directoryId}\" On=\"uninstall\" /><RegistryValue Root=\"{hive}\" Key=\"{Xml(registryKey)}\" Name=\"{cleanupId}\" Type=\"integer\" Value=\"1\" KeyPath=\"yes\" /></Component>");
            foreach (var child in _children)
            {
                var id = "D" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(child.Value.RelativePath)))[..16];
                result.Append($"<Directory Id=\"{id}\" Name=\"{WixXml(child.Key)}\">{child.Value.Render(refs, registryKey, id, hive, perMachine)}</Directory>");
            }
            return result.ToString();
        }

        private string RelativePath { get; } = relativePath;
    }
}
