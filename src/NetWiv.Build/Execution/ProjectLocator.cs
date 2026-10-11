namespace NetWiv.Build.Execution;

public static class ProjectLocator
{
    public static string FindApplicationProject(string configDirectory)
    {
        var candidates = Directory.EnumerateFiles(configDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Contains("Test", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new FileNotFoundException($"No .NET application project found next to the configuration in '{configDirectory}'."),
            _ => throw new InvalidOperationException($"Multiple .NET projects found next to the configuration; set build.dotnetProject explicitly: {string.Join(", ", candidates.Select(Path.GetFileName))}")
        };
    }
}
