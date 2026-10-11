using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NetWiv;

// Rules adapted from tauri 2.11.5.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: MIT OR Apache-2.0
// Mirrors the path matching and canonicalization rules used by Tauri's fs scope.
public sealed partial class FileScope
{
    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private static readonly IReadOnlyDictionary<string, int> BaseDirectories = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["$AUDIO"] = 1,
        ["$CACHE"] = 2,
        ["$CONFIG"] = 3,
        ["$DATA"] = 4,
        ["$LOCALDATA"] = 5,
        ["$DOCUMENT"] = 6,
        ["$DOWNLOAD"] = 7,
        ["$PICTURE"] = 8,
        ["$PUBLIC"] = 9,
        ["$VIDEO"] = 10,
        ["$RESOURCE"] = 11,
        ["$TEMP"] = 12,
        ["$APPCONFIG"] = 13,
        ["$APPDATA"] = 14,
        ["$APPLOCALDATA"] = 15,
        ["$APPCACHE"] = 16,
        ["$APPLOG"] = 17,
        ["$DESKTOP"] = 18,
        ["$EXE"] = 19,
        ["$FONT"] = 20,
        ["$HOME"] = 21,
        ["$RUNTIME"] = 22,
        ["$TEMPLATE"] = 23
    };

    private readonly Dictionary<string, LabelPattern> _allowed;
    private readonly Dictionary<string, LabelPattern> _denied;
    private readonly GlobMatchOptions _options;
    private readonly object _scopeGate = new();

    public FileScope(JsonElement scope, string appIdentifier)
    {
        var parsed = Parse(scope);
        _options = new GlobMatchOptions(
            RequireLiteralSeparator: true,
            RequireLiteralLeadingDot: parsed.RequireLiteralLeadingDot ?? !OperatingSystem.IsWindows());

        _allowed = Compile(parsed.Allow, appIdentifier, _options);
        _denied = Compile(parsed.Deny, appIdentifier, _options);
    }

    public IReadOnlySet<string> AllowedPatterns { get { lock (_scopeGate) return _allowed.Keys.ToHashSet(StringComparer.Ordinal); } }
    public IReadOnlySet<string> ForbiddenPatterns { get { lock (_scopeGate) return _denied.Keys.ToHashSet(StringComparer.Ordinal); } }

    public bool IsAllowed(string path)
    {
        string candidate;
        try
        {
            candidate = CanonicalizeRequest(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception)
        {
            return false;
        }

        lock (_scopeGate) return !_denied.Values.Any(pattern => pattern.Matches(candidate)) &&
            _allowed.Values.Any(pattern => pattern.Matches(candidate));
    }

    public bool IsForbidden(string path)
    {
        try
        {
            var candidate = CanonicalizeRequest(path);
            lock (_scopeGate) return _denied.Values.Any(pattern => pattern.Matches(candidate));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception)
        {
            return true;
        }
    }

    private static Dictionary<string, LabelPattern> Compile(
        IReadOnlyList<string> configuredPatterns,
        string appIdentifier,
        GlobMatchOptions options)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuredPattern in configuredPatterns)
        {
            if (!TryResolvePattern(configuredPattern, appIdentifier, out var resolved)) continue;

            AddPattern(patterns, resolved);
            if (OperatingSystem.IsWindows() && TryRemoveVerbatimDiskPrefix(resolved, out var simplified))
                AddPattern(patterns, simplified);

            var canonicalized = CanonicalizeParent(resolved);
            if (canonicalized is not null) AddPattern(patterns, canonicalized);
        }

        return patterns.ToDictionary(pattern => pattern, pattern => new LabelPattern(pattern, options, "file scope"), StringComparer.Ordinal);
    }

    private static void AddPattern(HashSet<string> patterns, string pattern)
    {
        patterns.Add(NormalizeSeparators(pattern));
    }

    private static bool TryResolvePattern(string pattern, string appIdentifier, out string resolved)
    {
        var normalized = NormalizeSeparators(pattern);
        var components = SplitComponents(normalized).ToArray();
        if (components.Length == 0)
        {
            resolved = normalized;
            return true;
        }

        var first = components[0];
        if (!BaseDirectories.TryGetValue(first, out var directory))
        {
            resolved = NormalizePatternComponents(normalized);
            return true;
        }

        string basePath;
        try
        {
            basePath = BuiltInCommands.ResolveDirectory(directory, appIdentifier);
        }
        catch (InvokeException)
        {
            resolved = string.Empty;
            return false;
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            resolved = string.Empty;
            return false;
        }

        resolved = NormalizePatternComponents(components.Skip(1).Aggregate(
            basePath,
            static (current, component) => component == "." || component == ".." ? current : Path.Combine(current, component)));
        return true;
    }

    private static ScopeConfiguration Parse(JsonElement scope)
    {
        if (scope.ValueKind == JsonValueKind.Undefined)
            return new([], [], null);

        if (scope.ValueKind == JsonValueKind.Array)
            return new(ReadPatterns(scope, "scope"), [], null);

        if (scope.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("File scope must be an array or object.", nameof(scope));

        var allow = scope.TryGetProperty("allow", out var allowElement) ? ReadPatterns(allowElement, "allow") : [];
        var deny = scope.TryGetProperty("deny", out var denyElement) ? ReadPatterns(denyElement, "deny") : [];
        bool? leadingDot = null;
        bool hasCamel = scope.TryGetProperty("requireLiteralLeadingDot", out var camelElement);
        bool hasKebab = scope.TryGetProperty("require-literal-leading-dot", out var kebabElement);
        if (hasCamel && hasKebab)
            throw new ArgumentException("File scope cannot define both requireLiteralLeadingDot and require-literal-leading-dot.", nameof(scope));
        if (hasCamel && camelElement.ValueKind != JsonValueKind.Null)
            leadingDot = ReadBoolean(camelElement, "requireLiteralLeadingDot");
        if (hasKebab && kebabElement.ValueKind != JsonValueKind.Null)
            leadingDot = ReadBoolean(kebabElement, "require-literal-leading-dot");
        return new(allow, deny, leadingDot);
    }

    private static string[] ReadPatterns(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"File scope '{property}' must be an array.");

        return element.EnumerateArray().Select((item, index) => item.ValueKind == JsonValueKind.String
            ? item.GetString()!
            : throw new ArgumentException($"File scope '{property}[{index}]' must be a string.")).ToArray();
    }

    private static bool ReadBoolean(JsonElement element, string property) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ArgumentException($"File scope '{property}' must be a boolean.")
    };

    private static string CanonicalizeRequest(string path)
    {
        var normalized = NormalizeSeparators(path);
        // Rust Path permits NUL. Its existence check fails and leaves the path
        // lexical for scope matching; File::open reports the invalid input later.
        if (normalized.Contains('\0')) return normalized;
        if (TryGetLinkTarget(normalized, out var target))
        {
            // std::fs::read_link returns the stored target. Pinned Tauri checks a
            // relative target against the process working directory, not the link parent.
            var storedTarget = NormalizeSeparators(target);
            return File.Exists(storedTarget) || Directory.Exists(storedTarget)
                ? CanonicalizeExisting(storedTarget)
                : storedTarget;
        }
        return File.Exists(normalized) || Directory.Exists(normalized)
            ? CanonicalizeExisting(normalized)
            : normalized;
    }

    private static string? CanonicalizeParent(string pattern)
    {
        var normalized = NormalizePatternComponents(pattern);
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        var relative = normalized[root.Length..];
        var components = SplitComponents(relative).ToList();
        var removed = new Stack<string>();

        while (components.Count > 0)
        {
            var candidate = Combine(root, components);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                string canonical;
                try
                {
                    canonical = CanonicalizeExisting(candidate);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception)
                {
                    return null;
                }

                while (removed.Count > 0) canonical = Path.Combine(canonical, removed.Pop());
                return NormalizeSeparators(canonical);
            }

            removed.Push(components[^1]);
            components.RemoveAt(components.Count - 1);
        }

        if (root.Length == 0 || !Directory.Exists(root)) return null;
        var canonicalRoot = CanonicalizeExisting(root);
        while (removed.Count > 0) canonicalRoot = Path.Combine(canonicalRoot, removed.Pop());
        return NormalizeSeparators(canonicalRoot);
    }

    private static string CanonicalizeExisting(string path)
    {
        if (OperatingSystem.IsWindows()) return CanonicalizeWindows(path);

        var rootedPath = Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path);
        var root = Path.GetPathRoot(rootedPath) ?? throw new ArgumentException("Path has no root.", nameof(path));
        var current = root;
        var pending = new Queue<string>(SplitComponents(rootedPath[root.Length..]));
        int followedLinks = 0;
        while (pending.Count > 0)
        {
            var component = pending.Dequeue();
            if (component == ".") continue;
            if (component == "..")
            {
                current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar)) ?? root;
                continue;
            }
            var candidate = Path.Combine(current, component);
            if (TryGetLinkTarget(candidate, out var target))
            {
                if (++followedLinks > 40) throw new IOException($"Too many symbolic links while resolving '{path}'.");
                var remaining = pending.ToArray();
                if (Path.IsPathRooted(target))
                {
                    root = Path.GetPathRoot(target) ?? throw new IOException($"Link target '{target}' has no root.");
                    pending = new Queue<string>(SplitComponents(target[root.Length..]).Concat(remaining));
                    current = root;
                }
                else
                {
                    pending = new Queue<string>(SplitComponents(target).Concat(remaining));
                }
            }
            else
            {
                current = candidate;
            }
        }
        return NormalizeSeparators(current);
    }

    private static string CanonicalizeWindows(string path)
    {
        using var handle = CreateFile(
            path,
            desiredAccess: 0,
            shareMode: FileShareAll,
            securityAttributes: 0,
            creationDisposition: OpenExisting,
            flagsAndAttributes: FileFlagBackupSemantics,
            templateFile: 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());

        uint capacity = 512;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(checked((int)capacity * sizeof(char)));
            try
            {
                var length = GetFinalPathNameByHandle(handle, buffer, capacity, flags: 0);
                if (length == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (length < capacity) return Marshal.PtrToStringUni(buffer, checked((int)length));
                capacity = checked(length + 1);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        throw new IOException($"Resolved path for '{path}' is too long.");
    }

    private static bool TryGetLinkTarget(string path, out string target)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        try
        {
            target = info.LinkTarget ?? string.Empty;
            return target.Length > 0;
        }
        catch (FileNotFoundException)
        {
            target = string.Empty;
            return false;
        }
    }

    private static string NormalizePatternComponents(string pattern)
    {
        var normalized = NormalizeSeparators(pattern);
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        var components = SplitComponents(normalized[root.Length..]).ToArray();
        var retained = components.Where((component, index) => component != "." &&
            (component != ".." || root.Length == 0 && index == 0)).ToArray();
        return Combine(root, retained);
    }

    private static string Combine(string root, IReadOnlyList<string> components)
    {
        var current = root;
        foreach (var component in components)
            current = current.Length == 0 ? component : Path.Combine(current, component);
        return current.Length == 0 ? root : NormalizeSeparators(current);
    }

    private static IEnumerable<string> SplitComponents(string path) => path.Split(
        OperatingSystem.IsWindows() ? ['/', '\\'] : ['/'],
        StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeSeparators(string path) => OperatingSystem.IsWindows()
        ? path.Replace('/', '\\')
        : path;

    private static bool TryRemoveVerbatimDiskPrefix(string pattern, out string simplified)
    {
        if (pattern.Length >= 6 && pattern.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            char.IsAsciiLetter(pattern[4]) && pattern[5] == ':')
        {
            simplified = pattern[4..];
            return true;
        }
        simplified = pattern;
        return false;
    }

    private sealed record ScopeConfiguration(
        IReadOnlyList<string> Allow,
        IReadOnlyList<string> Deny,
        bool? RequireLiteralLeadingDot);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        nint path,
        uint pathLength,
        uint flags);
}
