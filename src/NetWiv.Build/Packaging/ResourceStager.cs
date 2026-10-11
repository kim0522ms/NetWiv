using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWiv.Build.Packaging;

public static class ResourceStager
{
    public static void Stage(IReadOnlyList<BundleResource> resources, string baseDirectory, string outputDirectory)
    {
        baseDirectory = Path.GetFullPath(baseDirectory);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var current = resources.Select(resource => resource.TargetPath).ToHashSet(comparer);
        // Validate the entire copy plan before writing or removing generated files.
        foreach (var resource in resources)
        {
            _ = Destination(resource.TargetPath);
            if (!File.Exists(Path.GetFullPath(resource.SourcePath, baseDirectory)))
                throw new FileNotFoundException("Resource source disappeared before staging.", resource.SourcePath);
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? output.ToUpperInvariant() : output)));
        var manifest = Path.Combine(baseDirectory, "obj", "netwiv", "resource-manifests", key + ".json");
        var previous = new List<string>();
        if (File.Exists(manifest))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            foreach (var value in document.RootElement.EnumerateArray())
            {
                var relative = value.GetString() ?? throw new IOException("Invalid resource staging manifest.");
                _ = Destination(relative);
                previous.Add(relative);
            }
        }
        foreach (var resource in resources)
        {
            var source = Path.GetFullPath(resource.SourcePath, baseDirectory);
            var destination = Destination(resource.TargetPath);
            if (source.Equals(destination, comparison)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var original = new FileInfo(source);
            var existing = new FileInfo(destination);
            if (!existing.Exists || original.Length != existing.Length || original.LastWriteTimeUtc != existing.LastWriteTimeUtc)
                File.Copy(source, destination, true);
        }
        foreach (var relative in previous.Where(relative => !current.Contains(relative)))
            File.Delete(Destination(relative));
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartArray();
            foreach (var relative in current.Order(comparer)) writer.WriteStringValue(relative);
            writer.WriteEndArray();
        }
        var temporary = manifest + ".tmp";
        File.WriteAllBytes(temporary, bytes.ToArray());
        File.Move(temporary, manifest, true);

        string Destination(string relative)
        {
            if (Path.IsPathRooted(relative)) throw new IOException("Resource destination must be relative to the output directory.");
            var destination = Path.GetFullPath(relative, output);
            var prefix = output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(prefix, comparison)) throw new IOException("Resource destination escapes the output directory.");
            for (var entry = destination; !entry.Equals(output, comparison); entry = Path.GetDirectoryName(entry)!)
            {
                if ((File.Exists(entry) || Directory.Exists(entry)) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Resource destination contains a symbolic link: {entry}");
            }
            return destination;
        }
    }
}
