namespace NetWiv.Build.Packaging;

public static class PayloadStager
{
    public static IReadOnlyList<string> Stage(string publishDirectory, string destination)
    {
        var publish = Path.GetFullPath(publishDirectory);
        var target = Path.GetFullPath(destination);
        var comparison = PathComparison();
        if (target.Equals(publish, comparison) || publish.StartsWith(target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("The staging directory cannot contain the publish directory.");
        if (target.StartsWith(publish.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison) &&
            !FirstSegment(publish, target).Equals("bundle", comparison))
            throw new ArgumentException("A staging directory inside the publish directory must be under bundle.");
        Directory.CreateDirectory(target);
        var copied = new List<string>();
        foreach (var file in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories)
                     .Where(path => !FirstSegment(publish, path).Equals("bundle", comparison))
                     .OrderBy(path => Path.GetRelativePath(publish, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(publish, file);
            var output = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output, true);
            File.SetLastWriteTimeUtc(output, DateTime.UnixEpoch);
            copied.Add(relative);
        }
        return copied;
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string FirstSegment(string root, string path) => Path.GetRelativePath(root, path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 2)[0];
}
