using System.Text.Json;

namespace NetWiv;

internal static class CapabilityValidation
{
    internal static void Validate(JsonElement capability)
    {
        if (capability.ValueKind != JsonValueKind.Object) throw new FormatException("A capability must be an object.");
        RequireString(capability, "identifier");
        if (capability.TryGetProperty("description", out _)) RequireString(capability, "description");
        if (capability.TryGetProperty("local", out var local) && local.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new FormatException("Capability local must be a boolean.");
        foreach (var name in new[] { "windows", "webviews" })
            if (capability.TryGetProperty(name, out var labels)) Strings(labels, name);
        if (capability.TryGetProperty("remote", out var remote) && remote.ValueKind != JsonValueKind.Null)
        {
            if (remote.ValueKind != JsonValueKind.Object || !remote.TryGetProperty("urls", out var urls)) throw new FormatException("Capability remote requires a urls array.");
            Strings(urls, "remote.urls");
        }
        if (capability.TryGetProperty("platforms", out var platforms) && platforms.ValueKind != JsonValueKind.Null)
        {
            Strings(platforms, "platforms");
            foreach (var platform in platforms.EnumerateArray())
                if (platform.GetString() is not ("windows" or "linux" or "macOS" or "iOS" or "android")) throw new FormatException("Unknown capability platform.");
        }
        if (!capability.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Array) throw new FormatException("Capability permissions must be an array.");
        foreach (var permission in permissions.EnumerateArray())
        {
            var identifier = permission.ValueKind == JsonValueKind.String ? permission.GetString()! : RequireString(permission, "identifier");
            _ = PermissionIdentifier.Parse(identifier);
            if (permission.ValueKind != JsonValueKind.Object) continue;
            foreach (var name in new[] { "allow", "deny" })
                if (permission.TryGetProperty(name, out var scope) && scope.ValueKind is not (JsonValueKind.Null or JsonValueKind.Array))
                    throw new FormatException($"Permission {name} scope must be an array or null.");
        }
    }

    private static string RequireString(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new FormatException($"Capability {name} must be a string.");

    private static void Strings(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new FormatException($"Capability {name} must be an array of strings.");
    }
}
