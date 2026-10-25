using System.Text.Json;

namespace NetWiv;

public sealed record PermissionDefinition(
    string Identifier,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<JsonElement>? ScopeAllow = null,
    IReadOnlyList<JsonElement>? ScopeDeny = null)
{
    internal IReadOnlyList<string>? References { get; init; }
    internal PermissionDefinition? NestedPermission { get; init; }

    public PermissionDefinition(string identifier, params string[] allow)
        : this(identifier, allow, Array.Empty<string>()) { }
}

public sealed record Capability(
    string Identifier,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string>? Windows = null,
    IReadOnlyList<string>? Webviews = null,
    bool Local = true,
    IReadOnlyList<string>? RemoteUrls = null,
    IReadOnlyList<PermissionScopeExtension>? PermissionScopes = null)
{
    /// <summary>Allows this capability to be selected for non-WebView managed command invocation.</summary>
    public bool Backend { get; init; }
}

public sealed record PermissionScopeExtension(
    string Identifier,
    IReadOnlyList<JsonElement>? Allow = null,
    IReadOnlyList<JsonElement>? Deny = null)
{
    internal int EntryIndex { get; init; } = -1;
}

public sealed record AppCommandManifest(IReadOnlyList<string> Commands);

public sealed record AuthorizationRequest(string Command, string WindowLabel, string WebviewLabel, Uri Source);

internal sealed partial class AclAuthorizer
{
    private readonly Dictionary<string, PermissionDefinition> _definitions;
    private readonly Dictionary<string, PermissionDefinition[]> _resolved = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<Capability> _capabilities;
    private readonly IReadOnlySet<string> _localOrigins;
    private readonly IReadOnlySet<string> _localProtocols;
    private readonly bool _hasAppCommandManifest;
    private readonly Dictionary<string, LabelPattern> _labelPatterns;
    private readonly Dictionary<string, RemoteUrlPattern> _remotePatterns;
    private readonly HashSet<string> _knownPlugins;

    public AclAuthorizer(
        IReadOnlyList<PermissionDefinition> definitions,
        IReadOnlyList<Capability> capabilities,
        IReadOnlySet<string> localOrigins,
        bool hasAppCommandManifest,
        IEnumerable<string>? knownPlugins = null,
        IReadOnlySet<string>? localProtocols = null)
    {
        _definitions = definitions.ToDictionary(static value => value.Identifier, StringComparer.Ordinal);
        _capabilities = capabilities;
        _localOrigins = localOrigins;
        _localProtocols = localProtocols ?? new HashSet<string>();
        _hasAppCommandManifest = hasAppCommandManifest;
        _knownPlugins = new(knownPlugins ?? [], StringComparer.Ordinal);
        _labelPatterns = capabilities.SelectMany(capability => (capability.Windows ?? []).Concat(capability.Webviews ?? []))
            .Distinct(StringComparer.Ordinal).ToDictionary(pattern => pattern, pattern => new LabelPattern(pattern), StringComparer.Ordinal);
        _remotePatterns = capabilities.SelectMany(capability => capability.RemoteUrls ?? [])
            .Distinct(StringComparer.Ordinal).ToDictionary(pattern => pattern, pattern => new RemoteUrlPattern(pattern), StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            if (capability.Backend && (capability.Windows is { Count: > 0 } || capability.Webviews is { Count: > 0 } || capability.RemoteUrls is { Count: > 0 }))
                throw new InvalidOperationException($"Backend capability '{capability.Identifier}' cannot target windows, webviews or remote URLs.");
            foreach (var permission in capability.Permissions)
            {
                _ = PermissionIdentifier.Parse(permission);
                if (!_definitions.ContainsKey(permission))
                    throw new InvalidOperationException($"Capability '{capability.Identifier}' references unknown permission '{permission}'.");
                Resolve(permission);
            }
        }
    }

    public AuthorizationResult Authorize(AuthorizationRequest request)
    {
        var isLocal = IsLocal(request.Source);
        var requiresGrant = request.Command.StartsWith("plugin:", StringComparison.Ordinal) ||
            request.Command.StartsWith("core:", StringComparison.Ordinal) ||
            _hasAppCommandManifest;

        if (!requiresGrant && isLocal)
        {
            return AuthorizationResult.AllowedWithoutScope;
        }

        var allowed = false;
        var scopeAllow = new List<JsonElement>();
        var scopeDeny = new List<JsonElement>();
        foreach (var capability in _capabilities)
        {
            if (capability.Backend) continue;
            if (!capability.Local && capability.RemoteUrls is not { Count: > 0 }) continue;
            foreach (var permissionName in capability.Permissions)
            {
                if (_resolved[permissionName].Any(permission => permission.Deny.Contains(request.Command, StringComparer.Ordinal)))
                    return AuthorizationResult.Denied;
            }
        }

        foreach (var capability in _capabilities)
        {
            if (capability.Backend) continue;
            if (!MatchesTarget(capability, request.WindowLabel, request.WebviewLabel) ||
                !MatchesOrigin(capability, request.Source, isLocal))
            {
                continue;
            }

            for (var entryIndex = 0; entryIndex < capability.Permissions.Count; entryIndex++)
            {
                var permissionName = capability.Permissions[entryIndex];
                foreach (var permission in _resolved[permissionName])
                {
                    var grantsCommand = permission.Allow.Contains(request.Command, StringComparer.Ordinal);
                    var suppliesPluginScope = !grantsCommand && permission.Allow.Count == 0 && permission.Deny.Count == 0 &&
                        (permission.ScopeAllow is not null || permission.ScopeDeny is not null || permission.Identifier.EndsWith(":scope", StringComparison.Ordinal)) &&
                        request.Command.StartsWith("plugin:" + permission.Identifier.Split(':', 2)[0] + "|", StringComparison.Ordinal);
                    if (!grantsCommand && !suppliesPluginScope) continue;
                    if (grantsCommand) allowed = true;
                    foreach (var extension in capability.PermissionScopes ?? [])
                    {
                        if (!string.Equals(extension.Identifier, permissionName, StringComparison.Ordinal) ||
                            extension.EntryIndex >= 0 && extension.EntryIndex != entryIndex) continue;
                        AddScope(scopeAllow, extension.Allow);
                        AddScope(scopeDeny, extension.Deny);
                    }
                    AddScope(scopeAllow, permission.ScopeAllow);
                    AddScope(scopeDeny, permission.ScopeDeny);
                }
            }
        }

        return allowed
            ? new(true, new Runtime.RuntimeCommandScope(scopeAllow, scopeDeny))
            : AuthorizationResult.Denied;
    }

    internal IReadOnlyList<PermissionDefinition> Resolve(string identifier) => Resolve(identifier, new HashSet<string>(StringComparer.Ordinal));

    internal bool IsLocal(Uri source) => _localOrigins.Contains(source.GetLeftPart(UriPartial.Authority)) ||
        (OperatingSystem.IsWindows()
            ? source.Scheme is "http" or "https" && source.Host.EndsWith(".localhost", StringComparison.Ordinal) &&
                _localProtocols.Contains(source.Host[..^".localhost".Length])
            : _localProtocols.Contains(source.Scheme));

    private PermissionDefinition[] Resolve(string identifier, HashSet<string> visiting, bool nested = false)
    {
        if (!_definitions.TryGetValue(identifier, out var permission))
            throw new InvalidOperationException($"Permission set references unknown permission '{identifier}'.");
        if (nested && permission.NestedPermission is { } leaf) return [leaf];
        if (_resolved.TryGetValue(identifier, out var resolved)) return resolved;
        if (!visiting.Add(identifier))
            throw new InvalidOperationException($"Permission set cycle includes '{identifier}'.");
        resolved = permission.References is null ? [permission]
            : permission.References.SelectMany(reference => Resolve(reference, visiting, nested: true)).ToArray();
        visiting.Remove(identifier);
        _resolved.Add(identifier, resolved);
        return resolved;
    }

    private static void AddScope(List<JsonElement> target, IReadOnlyList<JsonElement>? values)
    {
        if (values is null) return;
        foreach (var value in values) target.Add(value.Clone());
    }

    private bool MatchesTarget(Capability capability, string windowLabel, string webviewLabel)
    {
        var hasWindows = capability.Windows is { Count: > 0 };
        var hasWebviews = capability.Webviews is { Count: > 0 };
        if (!hasWindows && !hasWebviews) return false;
        return (hasWindows && capability.Windows!.Any(pattern => _labelPatterns[pattern].Matches(windowLabel))) ||
            (hasWebviews && capability.Webviews!.Any(pattern => _labelPatterns[pattern].Matches(webviewLabel)));
    }

    private bool MatchesOrigin(Capability capability, Uri source, bool isLocal) =>
        isLocal ? capability.Local : capability.RemoteUrls?.Any(pattern => _remotePatterns[pattern].Matches(source)) == true;
}

internal sealed record AuthorizationResult(bool Allowed, Runtime.RuntimeCommandScope Scope)
{
    public static AuthorizationResult Denied { get; } = new(false, Runtime.RuntimeCommandScope.Empty);
    public static AuthorizationResult AllowedWithoutScope { get; } = new(true, Runtime.RuntimeCommandScope.Empty);
}
