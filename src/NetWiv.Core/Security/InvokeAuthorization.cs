using System.Text.Json;

namespace NetWiv;

/// <summary>An application-issued, immutable grant for non-WebView managed command calls.</summary>
public sealed class InvokeAuthorization
{
    private readonly IReadOnlyDictionary<string, Runtime.RuntimeCommandScope> _commands;

    internal InvokeAuthorization(AppHandle app, string[] capabilities, IReadOnlyDictionary<string, Runtime.RuntimeCommandScope> commands)
    {
        App = app;
        Capabilities = Array.AsReadOnly(capabilities);
        _commands = commands;
    }

    internal AppHandle App { get; }
    public IReadOnlyList<string> Capabilities { get; }

    internal AuthorizationResult Authorize(string command) => _commands.TryGetValue(command, out var scope)
        ? new(true, new(scope.Allow.Select(value => value.Clone()).ToArray(), scope.Deny.Select(value => value.Clone()).ToArray()))
        : AuthorizationResult.Denied;
}

internal sealed partial class AclAuthorizer
{
    internal InvokeAuthorization CreateBackendAuthorization(AppHandle app, string[] identifiers)
    {
        var selected = identifiers.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var identifier in selected)
            if (!_capabilities.Any(capability => capability.Identifier == identifier && capability.Backend))
                throw new ArgumentException($"Capability '{identifier}' is not enabled for backend invocation.", nameof(identifiers));

        // Denies belong to the application policy, not to the caller-selected allow subset.
        var backendCapabilities = _capabilities.Where(capability => capability.Backend).ToArray();
        var denied = backendCapabilities
            .SelectMany(capability => capability.Permissions).SelectMany(permission => _resolved[permission])
            .SelectMany(permission => permission.Deny).ToHashSet(StringComparer.Ordinal);
        var capabilities = backendCapabilities.Where(capability => selected.Contains(capability.Identifier, StringComparer.Ordinal)).ToArray();
        var commands = new Dictionary<string, Runtime.RuntimeCommandScope>(StringComparer.Ordinal);
        foreach (var command in capabilities.SelectMany(capability => capability.Permissions).SelectMany(permission => _resolved[permission])
            .SelectMany(permission => permission.Allow).Distinct(StringComparer.Ordinal))
        {
            if (denied.Contains(command)) continue;
            var allow = new List<JsonElement>();
            var deny = new List<JsonElement>();
            foreach (var capability in backendCapabilities)
            {
                var selectedCapability = selected.Contains(capability.Identifier, StringComparer.Ordinal);
                for (var index = 0; index < capability.Permissions.Count; index++)
                {
                    var name = capability.Permissions[index];
                    foreach (var permission in _resolved[name])
                    {
                        var grants = permission.Allow.Contains(command, StringComparer.Ordinal);
                        var pluginScope = permission.Allow.Count == 0 && permission.Deny.Count == 0 &&
                            (permission.ScopeAllow is not null || permission.ScopeDeny is not null || permission.Identifier.EndsWith(":scope", StringComparison.Ordinal)) &&
                            command.StartsWith("plugin:" + permission.Identifier.Split(':', 2)[0] + "|", StringComparison.Ordinal);
                        if (!grants && !pluginScope) continue;
                        foreach (var extension in capability.PermissionScopes ?? [])
                        {
                            if (extension.Identifier != name || extension.EntryIndex >= 0 && extension.EntryIndex != index) continue;
                            if (selectedCapability) AddScope(allow, extension.Allow);
                            AddScope(deny, extension.Deny);
                        }
                        if (selectedCapability) AddScope(allow, permission.ScopeAllow);
                        AddScope(deny, permission.ScopeDeny);
                    }
                }
            }
            commands.Add(command, new(allow.ToArray(), deny.ToArray()));
        }
        return new(app, selected, commands);
    }
}
