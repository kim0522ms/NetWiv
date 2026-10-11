using NetWiv.Build.Configuration;
using System.Text.Json.Nodes;

namespace NetWiv.Cli;

internal static class EffectivePermissions
{
    private const string DefaultIdentifier = "default";

    public static void Apply(
        JsonObject root,
        string configDirectory,
        string targetPlatform,
        IReadOnlyList<string> capabilitySources)
    {
        var files = new JsonArray();
        var manifest = new ManifestIndex();
        var permissionDirectory = Path.Combine(configDirectory, "permissions");
        if (Directory.Exists(permissionDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(permissionDirectory, "*", SearchOption.AllDirectories)
                .Where(IsPermissionFile)
                .Where(static path => !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "schemas", StringComparison.Ordinal))
                .OrderBy(static path => path, StringComparer.Ordinal))
            {
                JsonObject file;
                try
                {
                    file = ConfigLoader.ParseFile(path) as JsonObject
                        ?? throw new NetWivConfigException("The permission file root must be an object.");
                }
                catch (NetWivConfigException exception)
                {
                    throw new NetWivConfigException($"Invalid permission file '{path}': {exception.Message}");
                }

                ValidateFile(file, path, manifest);
                files.Add((JsonNode)file);
            }
        }

        var hasAppManifest = manifest.HasDeclarations;
        ValidateCapabilities(root, manifest, hasAppManifest, targetPlatform, capabilitySources);
        root["$netwivAcl"] = new JsonObject
        {
            ["version"] = 1,
            ["hasAppManifest"] = hasAppManifest,
            ["files"] = files
        };
    }

    private static bool IsPermissionFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".json" or ".toml";
    }

    private static void ValidateFile(JsonObject file, string source, ManifestIndex manifest)
    {
        if (file["default"] is not null)
        {
            var value = RequireObject(file["default"], "default", source);
            ValidateOptionalVersion(value, "default.version", source);
            ValidateOptionalString(value, "description", "default.description", source);
            var references = RequireStrings(value["permissions"], "default.permissions", source);
            manifest.Default = new(DefaultIdentifier, references, source, true);
        }

        if (file.ContainsKey("set"))
        {
            var sets = RequireArray(file["set"], "set", source);
            for (var index = 0; index < sets.Count; index++)
            {
                var set = RequireObject(sets[index], $"set[{index}]", source);
                var identifier = RequireString(set["identifier"], $"set[{index}].identifier", source);
                _ = RequireString(set["description"], $"set[{index}].description", source);
                var references = RequireStrings(set["permissions"], $"set[{index}].permissions", source);
                manifest.Sets[identifier] = new(identifier, references, source, false);
            }
        }

        if (file.ContainsKey("permission"))
        {
            var permissions = RequireArray(file["permission"], "permission", source);
            for (var index = 0; index < permissions.Count; index++)
            {
                var permission = RequireObject(permissions[index], $"permission[{index}]", source);
                var identifier = RequireString(permission["identifier"], $"permission[{index}].identifier", source);
                ValidateOptionalVersion(permission, $"permission[{index}].version", source);
                ValidateOptionalString(permission, "description", $"permission[{index}].description", source);
                if (permission.ContainsKey("commands")) ValidateCommands(permission["commands"], $"permission[{index}].commands", source);
                if (permission.ContainsKey("scope")) ValidateScope(permission["scope"], $"permission[{index}].scope", source);
                ValidatePlatforms(permission["platforms"], $"permission[{index}].platforms", source);
                manifest.Permissions[identifier] = source;
            }
        }
    }

    private static void ValidateCommands(JsonNode? node, string field, string source)
    {
        var commands = RequireObject(node, field, source);
        if (commands.ContainsKey("allow")) _ = RequireStrings(commands["allow"], $"{field}.allow", source);
        if (commands.ContainsKey("deny")) _ = RequireStrings(commands["deny"], $"{field}.deny", source);
    }

    private static void ValidateScope(JsonNode? node, string field, string source)
    {
        var scope = RequireObject(node, field, source);
        if (scope["allow"] is not null) _ = RequireArray(scope["allow"], $"{field}.allow", source);
        if (scope["deny"] is not null) _ = RequireArray(scope["deny"], $"{field}.deny", source);
    }

    private static void ValidatePlatforms(JsonNode? node, string field, string source)
    {
        if (node is null) return;
        foreach (var platform in RequireStrings(node, field, source))
            if (platform is not ("macOS" or "windows" or "linux" or "android" or "iOS"))
                throw Invalid(source, $"'{field}' contains unsupported platform '{platform}'.");
    }

    private static void ValidateOptionalVersion(JsonObject owner, string field, string source)
    {
        var name = field[(field.LastIndexOf('.') + 1)..];
        if (owner[name] is null) return;
        if (owner[name] is not JsonValue value ||
            !(value.TryGetValue<long>(out var signed) && signed > 0) &&
            !(value.TryGetValue<ulong>(out var unsigned) && unsigned > 0))
        {
            throw Invalid(source, $"'{field}' must be a positive integer.");
        }
    }

    private static void ValidateOptionalString(JsonObject owner, string name, string field, string source)
    {
        if (owner[name] is not null) _ = RequireString(owner[name], field, source);
    }

    private static void VisitSet(
        Declaration declaration,
        ManifestIndex manifest,
        Dictionary<string, VisitState> states,
        List<string> path)
    {
        var key = declaration.IsDefault ? "\0default" : declaration.Identifier;
        if (states.TryGetValue(key, out var state))
        {
            if (state == VisitState.Complete) return;
            var start = path.IndexOf(declaration.Identifier);
            var cycle = path.Skip(start).Append(declaration.Identifier);
            throw new NetWivConfigException($"Application permission set cycle in '{declaration.Source}': {string.Join(" -> ", cycle)}.");
        }

        states[key] = VisitState.Visiting;
        path.Add(declaration.Identifier);
        foreach (var reference in declaration.References)
        {
            if (IsNamespaced(reference)) continue;
            if (reference == DefaultIdentifier)
            {
                if (manifest.Default is not null) VisitSet(manifest.Default, manifest, states, path);
                continue;
            }
            if (manifest.Permissions.ContainsKey(reference)) continue;
            if (!manifest.Sets.TryGetValue(reference, out var target))
                throw new NetWivConfigException($"Application permission '{reference}' referenced by set '{declaration.Identifier}' in '{declaration.Source}' was not found.");
            VisitSet(target, manifest, states, path);
        }
        path.RemoveAt(path.Count - 1);
        states[key] = VisitState.Complete;
    }

    private static void ValidateCapabilities(
        JsonObject root,
        ManifestIndex manifest,
        bool hasAppManifest,
        string targetPlatform,
        IReadOnlyList<string> capabilitySources)
    {
        if (root["app"]?["security"]?["capabilities"] is not JsonArray capabilities) return;
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        for (var index = 0; index < capabilities.Count; index++)
        {
            var source = index < capabilitySources.Count ? capabilitySources[index] : $"app.security.capabilities[{index}]";
            if (capabilities[index] is not JsonObject capability) throw Invalid(source, "A capability must be an object.");
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(capability.ToJsonString());
                CapabilityValidation.Validate(document.RootElement);
            }
            catch (FormatException error) { throw Invalid(source, error.Message); }
            if (!AppliesToTarget(capability, targetPlatform, source)) continue;
            var permissions = (JsonArray)capability["permissions"]!;
            var capabilityName = capability["identifier"] is JsonValue identifierValue && identifierValue.TryGetValue<string>(out var identifier)
                ? identifier
                : $"index {index}";
            for (var permissionIndex = 0; permissionIndex < permissions.Count; permissionIndex++)
            {
                var entry = permissions[permissionIndex];
                var reference = entry is JsonObject extension
                    ? RequireString(extension["identifier"], $"permissions[{permissionIndex}].identifier", source)
                    : RequireString(entry, $"permissions[{permissionIndex}]", source);
                if (IsNamespaced(reference)) continue;
                if (!hasAppManifest)
                    throw new NetWivConfigException($"Capability '{capabilityName}' in '{source}' references unresolved application permission or set '{reference}'.");
                if (reference == DefaultIdentifier)
                {
                    if (manifest.Default is not null) VisitSet(manifest.Default, manifest, states, []);
                }
                else if (manifest.Sets.TryGetValue(reference, out var set))
                {
                    VisitSet(set, manifest, states, []);
                }
                else if (!manifest.Permissions.ContainsKey(reference))
                {
                    throw new NetWivConfigException($"Capability '{capabilityName}' in '{source}' references unresolved application permission or set '{reference}'.");
                }
            }
        }
    }

    private static bool AppliesToTarget(JsonObject capability, string targetPlatform, string source)
    {
        if (capability["platforms"] is null) return true;
        return RequireStrings(capability["platforms"], "platforms", source).Contains(targetPlatform == "macos" ? "macOS" : targetPlatform, StringComparer.Ordinal);
    }

    private static bool IsNamespaced(string identifier) => identifier.Contains(':', StringComparison.Ordinal);

    private static JsonObject RequireObject(JsonNode? node, string field, string source) => node as JsonObject
        ?? throw Invalid(source, $"'{field}' must be an object.");

    private static JsonArray RequireArray(JsonNode? node, string field, string source) => node as JsonArray
        ?? throw Invalid(source, $"'{field}' must be an array.");

    private static string RequireString(JsonNode? node, string field, string source)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var result)) return result;
        throw Invalid(source, $"'{field}' must be a string.");
    }

    private static IReadOnlyList<string> RequireStrings(JsonNode? node, string field, string source)
    {
        var array = RequireArray(node, field, source);
        var values = new List<string>(array.Count);
        for (var index = 0; index < array.Count; index++) values.Add(RequireString(array[index], $"{field}[{index}]", source));
        return values;
    }

    private static NetWivConfigException Invalid(string source, string message) =>
        new($"Invalid permission data in '{source}': {message}");

    private sealed record Declaration(string Identifier, IReadOnlyList<string> References, string Source, bool IsDefault);
    private sealed class ManifestIndex
    {
        public Declaration? Default { get; set; }
        public Dictionary<string, Declaration> Sets { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Permissions { get; } = new(StringComparer.Ordinal);
        public bool HasDeclarations => Default is not null || Sets.Count > 0 || Permissions.Count > 0;
    }
    private enum VisitState { Visiting, Complete }
}
