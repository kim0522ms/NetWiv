using System.Text.Json;

namespace NetWiv;

internal sealed record EmbeddedAcl(bool HasAppManifest, IReadOnlyList<PermissionDefinition> Permissions)
{
    internal static EmbeddedAcl Read(JsonElement root)
    {
        if (!root.TryGetProperty("$netwivAcl", out var acl)) return new(false, []);
        if (acl.GetProperty("version").GetInt32() != 1)
            throw new InvalidOperationException("The embedded NetWiv ACL version is unsupported. Rebuild with matching NetWiv packages.");

        var permissions = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        var sets = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        PermissionDefinition? defaults = null;
        foreach (var file in acl.GetProperty("files").EnumerateArray())
        {
            foreach (var value in Array(file, "permission"))
            {
                var identifier = Identifier(value);
                var commands = value.TryGetProperty("commands", out var configuredCommands) ? configuredCommands : default;
                var scope = value.TryGetProperty("scope", out var configuredScope) ? configuredScope : default;
                var active = EmbeddedConfiguration.AppliesToCurrentPlatform(value);
                permissions[identifier] = new(identifier, active ? Strings(commands, "allow") : [], active ? Strings(commands, "deny") : [],
                    active ? Elements(scope, "allow") : null, active ? Elements(scope, "deny") : null);
            }
            foreach (var set in Array(file, "set"))
                sets[Identifier(set)] = new(Identifier(set), [], []) { References = Strings(set, "permissions") };
            if (file.TryGetProperty("default", out var defaultSet) && defaultSet.ValueKind != JsonValueKind.Null)
                defaults = new("default", [], []) { References = Strings(defaultSet, "permissions") };
        }
        var hasManifest = permissions.Count != 0 || sets.Count != 0 || defaults is not null;
        if (acl.GetProperty("hasAppManifest").GetBoolean() != hasManifest)
            throw new InvalidOperationException("The embedded NetWiv ACL manifest flag does not match its permission declarations.");
        foreach (var set in sets.Values)
            permissions[set.Identifier] = set with { NestedPermission = permissions.GetValueOrDefault(set.Identifier) };
        // Top-level lookup prefers sets; nested lookup prefers individual permissions.
        // `default` always resolves the default set, or an empty set when absent.
        if (hasManifest) permissions["default"] = defaults ?? new("default", [], []) { References = [] };
        return new(hasManifest, permissions.Values.ToArray());
    }

    private static string Identifier(JsonElement value) => value.GetProperty("identifier").GetString()
        ?? throw new InvalidOperationException("An embedded application permission requires an identifier.");

    private static IEnumerable<JsonElement> Array(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var array)
            ? array.EnumerateArray() : [];

    private static string[] Strings(JsonElement value, string name) =>
        Array(value, name).Select(item => item.GetString()
            ?? throw new InvalidOperationException($"Embedded permission '{name}' entries must be strings.")).ToArray();

    private static JsonElement[]? Elements(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var array) && array.ValueKind != JsonValueKind.Null
            ? array.EnumerateArray().Select(item => item.Clone()).ToArray() : null;
}
