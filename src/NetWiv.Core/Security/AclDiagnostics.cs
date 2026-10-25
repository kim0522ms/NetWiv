// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
// Diagnostic formatting follows tauri 2.11.5 ipc/authority.rs.
namespace NetWiv;

internal sealed partial class AclAuthorizer
{
    internal string DebugMessage(AuthorizationRequest request)
    {
        var command = request.Command;
        var plugin = command.StartsWith("plugin:", StringComparison.Ordinal) ? command[7..].Split('|')[0] : null;
        var pretty = plugin is null ? command : plugin + "." + command[(command.IndexOf('|') + 1)..];
        var local = IsLocal(request.Source);
        var origin = local ? "local" : request.Source.AbsoluteUri;
        var denied = Entries(command, deny: true).ToArray();
        if (denied.Length > 0)
            return $"{pretty} explicitly denied on origin {(local ? "local" : "remote: " + origin)}\n\nreferenced by: {References(denied)}";
        var allowed = Entries(command, deny: false).ToArray();
        if (allowed.Length > 0)
        {
            if (allowed.Any(entry => (entry.Url is null ? local : !local && _remotePatterns[entry.Url].Matches(request.Source)) &&
                MatchesTarget(entry.Capability, request.WindowLabel, request.WebviewLabel))) return "allowed";
            var targets = allowed.Select(entry => "[" +
                (entry.Capability.Windows is { Count: > 0 } windows ? "windows: " + Quote(windows) + ", " : "") +
                (entry.Capability.Webviews is { Count: > 0 } webviews ? "webviews: " + Quote(webviews) + ", " : "") +
                "URL: " + (entry.Url ?? "local") + "]");
            return $"{pretty} not allowed on window \"{request.WindowLabel}\", webview \"{request.WebviewLabel}\", URL: {origin}\n\nallowed on: {string.Join(", ", targets)}\n\nreferenced by: {References(allowed)}";
        }
        var prefix = plugin is null ? "" : _definitions.Keys.Any(key => key.StartsWith(plugin + ":", StringComparison.Ordinal)) ? plugin + ":" : "core:" + plugin + ":";
        var exists = plugin is null ? _hasAppCommandManifest : _knownPlugins.Contains(plugin) || _definitions.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
        if (!exists) return $"{pretty} not allowed. Plugin not found";
        var permissions = _definitions.Values.Where(definition => plugin is null ? !definition.Identifier.Contains(':') : definition.Identifier.StartsWith(prefix, StringComparison.Ordinal))
            .Where(definition => Allows(definition, command, new HashSet<string>(StringComparer.Ordinal)))
            .Select(definition => definition.Identifier).Order(StringComparer.Ordinal).ToArray();
        return $"{pretty} not allowed. " + (permissions.Length == 0 ? "Command not found" : "Permissions associated with this command: " + string.Join(", ", permissions));
    }

    private IEnumerable<DiagnosticEntry> Entries(string command, bool deny)
    {
        foreach (var capability in _capabilities.Where(capability => !capability.Backend).OrderBy(capability => capability.Identifier, StringComparer.Ordinal))
            foreach (var permissionName in capability.Permissions)
                foreach (var permission in _resolved[permissionName])
                {
                    if (!(deny ? permission.Deny : permission.Allow).Contains(command, StringComparer.Ordinal)) continue;
                    if (capability.Local) yield return new(capability, permissionName, null);
                    foreach (var url in capability.RemoteUrls ?? []) yield return new(capability, permissionName, url);
                }
    }

    private bool Allows(PermissionDefinition permission, string command, HashSet<string> visiting)
    {
        if (!visiting.Add(permission.Identifier)) return false;
        if (permission.References is null) return permission.Allow.Contains(command, StringComparer.Ordinal);
        return permission.References.Any(reference => _definitions.TryGetValue(reference, out var definition) &&
            Allows(reference == "default" ? definition : definition.NestedPermission ?? definition, command, new HashSet<string>(visiting, StringComparer.Ordinal)));
    }
    private static string Quote(IEnumerable<string> values) => string.Join(", ", values.Select(value => "\"" + value + "\""));
    private static string References(IEnumerable<DiagnosticEntry> entries) => string.Join(" || ", entries.Select(entry => $"capability: {entry.Capability.Identifier}, permission: {entry.Permission}"));
    private sealed record DiagnosticEntry(Capability Capability, string Permission, string? Url);
}
