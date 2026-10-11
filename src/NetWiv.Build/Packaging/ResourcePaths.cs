// Resource mapping rules adapted from Tauri 2.11.5 tauri-utils/src/resources.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json;

namespace NetWiv.Build.Packaging;

public sealed record BundleResource(string SourcePath, string TargetPath);
public sealed record ResourceExpansion(IReadOnlyList<BundleResource> Files, IReadOnlyList<Exception> Errors);

public static class ResourcePaths
{
    public static IReadOnlyList<string> WatchRoots(JsonElement resources, string baseDirectory)
    {
        var roots = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var (input, _) in Patterns(resources))
        {
            var pattern = Normalize(input);
            string directory;
            if (pattern.Contains('*'))
            {
                var root = Path.GetPathRoot(pattern) ?? "";
                var stable = Components(pattern[root.Length..]).TakeWhile(part => part.IndexOfAny(['*', '?', '[']) < 0);
                directory = Absolute(Path.Combine(new[] { root }.Concat(stable).ToArray()), baseDirectory);
            }
            else
            {
                var absolute = Absolute(pattern, baseDirectory);
                directory = Directory.Exists(absolute) ? absolute : Path.GetDirectoryName(absolute)!;
            }
            while (!Directory.Exists(directory)) directory = Path.GetDirectoryName(directory) ?? throw new DirectoryNotFoundException(input);
            roots.Add(directory);
        }
        return roots.ToArray();
    }

    public static Func<string, bool> SourceMatcher(JsonElement resources, string baseDirectory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rules = new List<Func<string, bool>>();
        foreach (var (input, _) in Patterns(resources))
        {
            var absolute = Absolute(input, baseDirectory);
            if (input.Contains('*'))
            {
                var matcher = new LabelPattern(absolute, new(true, false), "resource");
                rules.Add(matcher.Matches);
            }
            else
            {
                var prefix = Path.TrimEndingDirectorySeparator(absolute) + Path.DirectorySeparatorChar;
                rules.Add(path => path.Equals(absolute, comparison) || path.StartsWith(prefix, comparison));
            }
        }
        return path => rules.Any(rule => rule(Path.GetFullPath(path)));
    }

    public static string RelativePath(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var parts = Components(path[root.Length..]).Where(part => part != ".")
            .Select(part => part == ".." ? "_up_" : part);
        if (root.IndexOfAny(['/', '\\']) >= 0) parts = new[] { "_root_" }.Concat(parts);
        return Path.Combine(parts.ToArray());
    }

    public static ResourceExpansion Expand(JsonElement resources, string baseDirectory, bool allowWalk = true)
    {
        var files = new List<BundleResource>();
        var errors = new List<Exception>();
        if (resources.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return new(files, errors);
        baseDirectory = Path.GetFullPath(baseDirectory);
        foreach (var (input, destination) in Patterns(resources))
        {
            var pattern = Normalize(input);
            var target = destination is null ? null : RelativePath(destination);
            try
            {
                if (pattern.Contains('*'))
                {
                    _ = new LabelPattern(pattern, new(true, false), "resource");
                    var matches = Glob(pattern, baseDirectory).ToArray();
                    if (matches.Length == 0) throw new FileNotFoundException($"Glob pattern '{input}' did not match any resource files.", input);
                    foreach (var match in matches)
                        files.Add(new(match, target is null ? RelativePath(match) : Path.Combine(target, Path.GetFileName(match))));
                }
                else if (Directory.Exists(Absolute(pattern, baseDirectory)))
                {
                    if (!allowWalk) throw new InvalidOperationException($"Resource directory traversal is disabled: {input}");
                    foreach (var match in Walk(pattern, baseDirectory))
                        files.Add(new(match, target is null ? RelativePath(match) : Path.Combine(target,
                            Path.GetRelativePath(Absolute(pattern, baseDirectory), Absolute(match, baseDirectory)))));
                }
                else
                {
                    if (!File.Exists(Absolute(pattern, baseDirectory))) throw new FileNotFoundException($"Resource path '{input}' does not exist.", input);
                    files.Add(new(pattern, target is null ? RelativePath(pattern) : target.Length == 0 ? Path.GetFileName(pattern) : target));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                errors.Add(error);
            }
        }
        return new(files, errors);
    }

    private static IEnumerable<string> Walk(string directory, string baseDirectory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(Absolute(directory, baseDirectory)).Order(StringComparer.Ordinal))
        {
            var relative = Path.Combine(directory, Path.GetFileName(entry));
            if (!Directory.Exists(entry)) yield return relative;
            else if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
                foreach (var file in Walk(relative, baseDirectory)) yield return file;
        }
    }

    private static IEnumerable<string> Glob(string pattern, string baseDirectory)
    {
        var root = Path.GetPathRoot(pattern) ?? "";
        var parts = Components(pattern[root.Length..]).ToArray();
        var matchers = parts.Select(part => new LabelPattern(part, new(true, false), "resource")).ToArray();
        var ancestors = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return Visit(root, 0).Distinct(StringComparer.Ordinal);

        IEnumerable<string> Visit(string current, int index)
        {
            var absolute = Absolute(current, baseDirectory);
            if (index == parts.Length)
            {
                if (File.Exists(absolute) && !Directory.Exists(absolute)) yield return current;
                yield break;
            }
            if (!Directory.Exists(absolute)) yield break;
            var part = parts[index];
            if (part == "**")
            {
                foreach (var match in Visit(current, index + 1)) yield return match;
                var info = new DirectoryInfo(absolute);
                var canonical = info.ResolveLinkTarget(true)?.FullName ?? info.FullName;
                if (!ancestors.Add(canonical)) throw new IOException($"Resource glob encountered a directory link cycle: {current}");
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(absolute).Order(StringComparer.Ordinal))
                    {
                        var next = Path.Combine(current, Path.GetFileName(entry));
                        if (Directory.Exists(entry))
                        {
                            foreach (var match in Visit(next, index)) yield return match;
                        }
                        else if (index == parts.Length - 1 && File.Exists(entry)) yield return next;
                    }
                }
                finally { ancestors.Remove(canonical); }
            }
            else if (part.IndexOfAny(['*', '?', '[']) < 0)
            {
                foreach (var match in Visit(Path.Combine(current, part), index + 1)) yield return match;
            }
            else
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(absolute).Order(StringComparer.Ordinal))
                    if (matchers[index].Matches(Path.GetFileName(entry)))
                        foreach (var match in Visit(Path.Combine(current, Path.GetFileName(entry)), index + 1)) yield return match;
            }
        }
    }

    private static string Absolute(string path, string baseDirectory) => path.Length == 0 ? baseDirectory : Path.GetFullPath(path, baseDirectory);
    private static IEnumerable<(string Pattern, string? Target)> Patterns(JsonElement resources) => resources.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => [],
        JsonValueKind.Array => resources.EnumerateArray().Select(value => (ReadString(value), (string?)null)),
        JsonValueKind.Object => resources.EnumerateObject().Select(value => (value.Name, (string?)ReadString(value.Value))),
        _ => throw new ArgumentException("bundle.resources must be an array of paths or an object mapping paths to destinations.")
    };
    private static string Normalize(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        return Path.Combine(new[] { root }.Concat(Components(path[root.Length..]).Where(part => part != ".")).ToArray());
    }
    private static IEnumerable<string> Components(string path) => path.Split(OperatingSystem.IsWindows() ? ['/', '\\'] : ['/'], StringSplitOptions.RemoveEmptyEntries);
    private static string ReadString(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! :
        throw new ArgumentException("bundle.resources paths and destinations must be strings.");
}
