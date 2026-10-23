using System.Text.Json.Nodes;
using System.Security.Cryptography;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;

namespace NetWiv.Build.Packaging;

public sealed record ExternalBinary(string Source, string DestinationName, string SourceRoot);

public static class ExternalBinaryStager
{
    public static void ValidateConfiguration(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _ = ParseEntries(root);
    }

    public static IReadOnlyList<ExternalBinary> Resolve(ConfigDocument config, BuildTarget target, string appBinaryName)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(appBinaryName) || Path.GetFileName(appBinaryName) != appBinaryName)
            throw new NetWivConfigException("The application binary name must be a file name.");

        var entries = ParseEntries(config.Root);
        var triple = CanonicalTriple(target);
        var executableExtension = target.Platform == "windows" ? ".exe" : string.Empty;
        var comparer = target.Platform is "windows" or "macos" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var destinations = new HashSet<string>(comparer) { appBinaryName };
        var result = new List<ExternalBinary>(entries.Count);
        foreach (var entry in entries)
        {
            var relative = entry.Relative;
            var segments = entry.Segments;
            var name = entry.Name;
            if (target.Platform == "windows")
                foreach (var segment in segments) WindowsInstallerOptions.ValidateFileName(segment, "bundle.externalBin path");

            var destinationName = name + executableExtension;
            if (target.Platform == "windows") WindowsInstallerOptions.ValidateFileName(destinationName, "bundle.externalBin destination");
            if (target.Platform == "windows" && destinationName.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase))
                throw new NetWivConfigException("bundle.externalBin cannot use the reserved Windows installer name uninstall.exe.");
            if (!destinations.Add(destinationName))
                throw new NetWivConfigException($"bundle.externalBin entries collide with each other or the application binary: {destinationName}");

            var sourceDirectory = segments.Length == 1
                ? Path.GetFullPath(config.ConfigDirectory)
                : Path.GetFullPath(Path.Combine(config.ConfigDirectory, Path.Combine(segments[..^1])));
            var source = Path.GetFullPath(name + "-" + triple + executableExtension, sourceDirectory);
            EnsureContained(config.ConfigDirectory, source, relative);
            EnsureRegularFileWithoutLinks(config.ConfigDirectory, source, relative);
            EnsureExecutable(source);
            result.Add(new ExternalBinary(source, destinationName, config.ConfigDirectory));
        }
        return result;
    }

    public static void Stage(IReadOnlyList<ExternalBinary> binaries, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(binaries);
        if (binaries.Count == 0) return;
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var destinations = new HashSet<string>(comparer);
        foreach (var binary in binaries)
        {
            if (string.IsNullOrWhiteSpace(binary.DestinationName) || Path.GetFileName(binary.DestinationName) != binary.DestinationName ||
                binary.DestinationName is "." or "..")
                throw new NetWivConfigException($"External binary destination must be a file name: {binary.DestinationName}");
            if (!destinations.Add(binary.DestinationName))
                throw new NetWivConfigException($"External binary destinations collide: {binary.DestinationName}");
            EnsureRegularFileWithoutLinks(binary.SourceRoot, Path.GetFullPath(binary.Source), binary.Source);
            EnsureExecutable(binary.Source);
        }

        EnsureDestinationRoot(destinationRoot);
        Directory.CreateDirectory(destinationRoot);
        EnsureDestinationRoot(destinationRoot);
        var copies = binaries.Select(binary =>
        {
            var output = Path.GetFullPath(binary.DestinationName, destinationRoot);
            EnsureContained(destinationRoot, output, binary.DestinationName);
            if (!TryGetAttributes(output, out var attributes)) return (binary.Source, Output: output, Copy: true);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException($"External binary destination already exists and will not be overwritten: {output}");
            if (!SameContent(binary.Source, output))
                throw new IOException($"External binary destination already exists with different content: {output}. For a changed development helper, remove the old generated copy and restart.");
            if (!OperatingSystem.IsWindows() && !HasExecuteBit(output))
                throw new IOException($"External binary destination has matching content but is not executable; it will not be modified: {output}");
            return (binary.Source, Output: output, Copy: false);
        }).ToArray();

        foreach (var copy in copies)
        {
            if (!copy.Copy) continue;
            using (var source = File.OpenRead(copy.Source))
            using (var destination = new FileStream(copy.Output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(destination);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(copy.Output, File.GetUnixFileMode(copy.Source));
        }
    }

    private static string CanonicalTriple(BuildTarget target) => (target.Platform, target.Architecture) switch
    {
        ("windows", "x86_64") => "x86_64-pc-windows-msvc",
        ("linux", "x86_64") => "x86_64-unknown-linux-gnu",
        ("macos", "aarch64") => "aarch64-apple-darwin",
        ("macos", "x86_64") => "x86_64-apple-darwin",
        _ => throw new PlatformNotSupportedException($"No external binary target triple is defined for {target.Platform}/{target.Architecture}.")
    };

    private static IReadOnlyList<ExternalBinaryEntry> ParseEntries(JsonObject root)
    {
        if (root["bundle"] is not JsonObject bundle || !bundle.ContainsKey("externalBin")) return [];
        if (bundle["externalBin"] is not JsonArray array)
            throw new NetWivConfigException("bundle.externalBin must be an array of relative extensionless paths.");

        var result = new List<ExternalBinaryEntry>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var relative) || string.IsNullOrWhiteSpace(relative))
                throw new NetWivConfigException("Each bundle.externalBin entry must be a nonempty relative extensionless path.");
            var normalized = relative.Replace('\\', '/');
            if (Path.IsPathRooted(relative) || normalized[0] == '/' ||
                (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':'))
                throw new NetWivConfigException($"bundle.externalBin path must be relative: {relative}");
            var segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
                throw new NetWivConfigException($"bundle.externalBin path is malformed: {relative}");
            if (segments.Any(segment => segment.Any(char.IsControl)))
                throw new NetWivConfigException($"bundle.externalBin paths cannot contain control characters: {relative}");
            var name = segments[^1];
            if (Path.HasExtension(name))
                throw new NetWivConfigException($"bundle.externalBin path must be extensionless: {relative}");
            result.Add(new ExternalBinaryEntry(relative, segments, name));
        }
        return result;
    }

    private static void EnsureContained(string root, string path, string description)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new NetWivConfigException($"External binary path escapes its root: {description}");
    }

    private static void EnsureRegularFileWithoutLinks(string root, string path, string description)
    {
        EnsureContained(root, path, description);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!TryGetAttributes(fullRoot, out var rootAttributes) || (rootAttributes & FileAttributes.Directory) == 0)
            throw new DirectoryNotFoundException($"External binary source root does not exist: {fullRoot}");
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            throw new NetWivConfigException($"External binary source root cannot be a symbolic link: {fullRoot}");
        var relative = Path.GetRelativePath(fullRoot, path);
        var current = fullRoot;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                throw new FileNotFoundException($"External binary source does not exist: {description}", current);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new NetWivConfigException($"External binary paths cannot contain symbolic links: {description}");
            if (!current.Equals(path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                (attributes & FileAttributes.Directory) == 0)
                throw new NetWivConfigException($"External binary source parent is not a directory: {description}");
            if (current.Equals(path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                (attributes & FileAttributes.Directory) != 0)
                throw new NetWivConfigException($"External binary source must be a regular file: {description}");
        }
    }

    private static void EnsureDestinationRoot(string path)
    {
        if (TryGetAttributes(path, out var attributes) && (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"External binary destination directory cannot be a symbolic link: {path}");
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private static bool SameContent(string sourcePath, string destinationPath)
    {
        using var source = File.OpenRead(sourcePath);
        using var destination = File.OpenRead(destinationPath);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(source), SHA256.HashData(destination));
    }

    private static void EnsureExecutable(string path)
    {
        if (!OperatingSystem.IsWindows() && !HasExecuteBit(path))
            throw new NetWivConfigException($"External binary source must have at least one Unix execute permission bit: {path}");
    }

    private static bool HasExecuteBit(string path)
    {
        const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & executeBits) != 0;
    }

    private sealed record ExternalBinaryEntry(string Relative, string[] Segments, string Name);
}
