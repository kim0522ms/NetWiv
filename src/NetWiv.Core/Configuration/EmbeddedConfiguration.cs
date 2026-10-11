using System.Reflection;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

internal sealed record EmbeddedConfiguration(
    AppMetadata Metadata,
    IReadOnlyList<WindowOptions> Windows,
    bool HasWindowList,
    IReadOnlyList<Capability> Capabilities,
    string? DevelopmentUrl,
    string? ContentSecurityPolicy,
    bool FreezePrototype,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlySet<string> DisabledCspModifications,
    EmbeddedAcl Acl,
    JsonElement AssetProtocol,
    bool Isolation,
    IReadOnlyDictionary<string, JsonElement> Plugins)
{
    public static EmbeddedConfiguration Load(Assembly assembly, string resourceName, AppMetadata defaults, EmbeddedConfigMode mode)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Embedded NetWiv configuration '{resourceName}' was not found in '{assembly.GetName().Name}'.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Embedded NetWiv configuration must be a JSON object.");
        var plugins = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("plugins", out var pluginConfiguration))
        {
            if (pluginConfiguration.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("'plugins' must be an object in embedded NetWiv configuration.");
            foreach (var plugin in pluginConfiguration.EnumerateObject()) plugins[plugin.Name] = plugin.Value.Clone();
        }

        var metadata = new AppMetadata(
            String(root, "productName") ?? defaults.Name,
            (resourceName == "netwiv.config.json" ? assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "NetWiv.ConfigVersion")?.Value : null)
                ?? String(root, "version") ?? defaults.Version,
            String(root, "identifier") ?? defaults.Identifier);
        var developmentUrl = root.TryGetProperty("build", out var build) ? String(build, "devUrl") : null;

        var windows = new List<WindowOptions>();
        var hasWindowList = false;
        var capabilities = new List<Capability>();
        string? csp = null;
        string? devCsp = null;
        var freezePrototype = false;
        var isolation = false;
        JsonElement assetProtocol = default;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var disabledCspModifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.Object)
        {
            if (app.TryGetProperty("windows", out var configuredWindows) && configuredWindows.ValueKind == JsonValueKind.Array)
            {
                hasWindowList = true;
                foreach (var window in configuredWindows.EnumerateArray())
                    if (Boolean(window, "create") != false) windows.Add(ReadWindow(window));
            }

            if (app.TryGetProperty("security", out var security) && security.ValueKind == JsonValueKind.Object)
            {
                if (security.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.Object &&
                    string.Equals(String(pattern, "use"), "isolation", StringComparison.Ordinal))
                {
                    isolation = true;
                }
                freezePrototype = Boolean(security, "freezePrototype") ?? false;
                if (security.TryGetProperty("assetProtocol", out var configuredAssetProtocol)) assetProtocol = configuredAssetProtocol.Clone();
                if (security.TryGetProperty("dangerousDisableAssetCspModification", out var disabled))
                {
                    if (disabled.ValueKind == JsonValueKind.True)
                    {
                        disabledCspModifications.Add("script-src");
                        disabledCspModifications.Add("style-src");
                    }
                    else if (disabled.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var directive in ReadStrings(disabled)) disabledCspModifications.Add(directive);
                    }
                }
                if (security.TryGetProperty("csp", out var configuredCsp) && configuredCsp.ValueKind != JsonValueKind.Null) csp = ReadCsp(configuredCsp);
                if (security.TryGetProperty("devCsp", out var configuredDevCsp) && configuredDevCsp.ValueKind != JsonValueKind.Null) devCsp = ReadCsp(configuredDevCsp);
                if (security.TryGetProperty("headers", out var configuredHeaders) && configuredHeaders.ValueKind == JsonValueKind.Object)
                {
                    foreach (var header in configuredHeaders.EnumerateObject())
                    {
                        if (header.Value.ValueKind != JsonValueKind.Null) headers[header.Name] = ReadHeader(header.Value);
                    }
                }
                if (security.TryGetProperty("capabilities", out var configuredCapabilities) && configuredCapabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var capability in configuredCapabilities.EnumerateArray())
                    {
                        if (capability.ValueKind == JsonValueKind.String)
                            throw new InvalidOperationException($"Capability '{capability.GetString()}' must be expanded inline before embedding netwiv.config.json.");
                        var parsed = ReadCapability(capability);
                        if (AppliesToCurrentPlatform(capability)) capabilities.Add(parsed);
                    }
                }
            }
        }

        var activeCsp = mode == EmbeddedConfigMode.Development ? devCsp ?? csp : csp;
        return new(metadata, windows, hasWindowList, capabilities, developmentUrl, activeCsp, freezePrototype, headers, disabledCspModifications, EmbeddedAcl.Read(root), assetProtocol, isolation, plugins);
    }

    private static WindowOptions ReadWindow(JsonElement value)
    {
        var label = String(value, "label") ?? "main";
        return new(label, String(value, "title") ?? "NetWiv App", String(value, "url") ?? "index.html",
            Number(value, "width") ?? 800, Number(value, "height") ?? 600, value.Clone());
    }

    internal static Capability ReadCapability(JsonElement value)
    {
        CapabilityValidation.Validate(value);
        var identifier = String(value, "identifier") ?? throw new InvalidOperationException("An inline capability requires an identifier.");
        if (!value.TryGetProperty("permissions", out var configuredPermissions) || configuredPermissions.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Capability '{identifier}' requires a permissions array.");
        var permissions = new List<string>();
        var permissionScopes = new List<PermissionScopeExtension>();
        foreach (var permission in configuredPermissions.EnumerateArray())
        {
            var permissionIdentifier = permission.ValueKind == JsonValueKind.String ? permission.GetString() : String(permission, "identifier");
            if (string.IsNullOrWhiteSpace(permissionIdentifier)) throw new InvalidOperationException($"Capability '{identifier}' contains an invalid permission entry.");
            permissions.Add(permissionIdentifier);
            if (permission.ValueKind == JsonValueKind.Object)
            {
                permissionScopes.Add(new(permissionIdentifier,
                    ReadOptionalElements(permission, "allow"), ReadOptionalElements(permission, "deny"))
                    { EntryIndex = permissions.Count - 1 });
            }
        }
        IReadOnlyList<string>? remoteUrls = null;
        if (value.TryGetProperty("remote", out var remote) && remote.ValueKind == JsonValueKind.Object &&
            remote.TryGetProperty("urls", out var urls)) remoteUrls = ReadStrings(urls);
        return new(identifier, permissions, ReadOptionalStrings(value, "windows"), ReadOptionalStrings(value, "webviews"),
            Boolean(value, "local") ?? true, remoteUrls, permissionScopes);
    }

    internal static bool AppliesToCurrentPlatform(JsonElement value)
    {
        if (!value.TryGetProperty("platforms", out var platforms) || platforms.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        var current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macOS" : string.Empty;
        return platforms.EnumerateArray().Any(item => string.Equals(item.GetString(), current, StringComparison.Ordinal));
    }

    private static string ReadCsp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("CSP must be a string or directive object.");
        return string.Join("; ", value.EnumerateObject().Select(static directive =>
            directive.Value.ValueKind == JsonValueKind.Array
                ? $"{directive.Name} {string.Join(' ', ReadStrings(directive.Value))}"
                : $"{directive.Name} {directive.Value.GetString()}"));
    }

    private static string ReadHeader(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Array => string.Join(", ", ReadStrings(value)),
        JsonValueKind.Object => string.Join("; ", value.EnumerateObject().Select(static item => $"{item.Name} {item.Value.GetString()}")),
        _ => throw new InvalidOperationException("Security header values must be strings, arrays, or objects.")
    };

    private static IReadOnlyList<string>? ReadOptionalStrings(JsonElement value, string property) =>
        value.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array ? ReadStrings(items) : null;
    private static IReadOnlyList<JsonElement>? ReadOptionalElements(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var items) || items.ValueKind == JsonValueKind.Null) return null;
        if (items.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"Permission '{property}' scope must be an array or null.");
        return [.. items.EnumerateArray().Select(static item => item.Clone())];
    }
    private static string[] ReadStrings(JsonElement value) => [.. value.EnumerateArray().Select(static item => item.GetString() ?? string.Empty)];
    private static string? String(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static bool? Boolean(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
    private static double? Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number ? item.GetDouble() : null;
}
