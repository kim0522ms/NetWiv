using System.Xml.Linq;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static class MacAppStoreEntitlements
{
    // TN3125 exempts App Sandbox, Hardened Runtime and macOS app groups from
    // provisioning. Use documented keys, not a namespace-wide exemption:
    // unknown/restricted capabilities must still be authorized by a profile.
    // https://developer.apple.com/documentation/technotes/tn3125-inside-code-signing-provisioning-profiles
    private static readonly HashSet<string> UnrestrictedSecurityKeys = new(StringComparer.Ordinal)
    {
        "app-sandbox", "application-groups", "get-task-allow", "inherit",
        "network.client", "network.server", "print", "scripting-targets",
        "device.camera", "device.microphone", "device.audio-input", "device.usb",
        "device.bluetooth", "device.serial", "device.firewire", "device.audio-video-bridging",
        "personal-information.addressbook", "personal-information.location",
        "personal-information.calendars", "personal-information.photos-library",
        "files.user-selected.read-only", "files.user-selected.read-write", "files.user-selected.executable",
        "files.downloads.read-only", "files.downloads.read-write",
        "files.bookmarks.app-scope", "files.bookmarks.document-scope", "files.bookmarks.collection-scope",
        "assets.pictures.read-only", "assets.pictures.read-write",
        "assets.music.read-only", "assets.music.read-write", "assets.movies.read-only", "assets.movies.read-write",
        "cs.allow-jit", "cs.allow-unsigned-executable-memory", "cs.allow-dyld-environment-variables",
        "cs.disable-library-validation", "cs.disable-executable-page-protection", "cs.debugger",
        "automation.apple-events",
        "temporary-exception.apple-events", "temporary-exception.audio-unit-host",
        "temporary-exception.files.absolute-path.read-only", "temporary-exception.files.absolute-path.read-write",
        "temporary-exception.files.home-relative-path.read-only", "temporary-exception.files.home-relative-path.read-write",
        "temporary-exception.iokit-user-client-class", "temporary-exception.mach-lookup.global-name",
        "temporary-exception.mach-register.global-name", "temporary-exception.shared-preference.read-only",
        "temporary-exception.shared-preference.read-write"
    };

    private static bool IsUnrestricted(string key) => key.StartsWith("com.apple.security.", StringComparison.Ordinal)
        && UnrestrictedSecurityKeys.Contains(key["com.apple.security.".Length..]);

    internal static void Validate(XElement claimed, XElement? allowed)
    {
        foreach (var key in claimed.Elements("key"))
        {
            if (IsUnrestricted(key.Value)) continue;
            var permission = allowed is null ? null : MacPropertyList.Get(allowed, key.Value);
            if (permission is null || !Allows(permission, key.ElementsAfterSelf().First(), key.Value == "com.apple.developer.associated-domains"))
                throw new NetWivConfigException($"Entitlement '{key.Value}' requires a provisioning profile that authorizes its claimed value.");
        }
    }

    private static bool Allows(XElement allowed, XElement claimed, bool allowDomainWildcard)
    {
        if (claimed.Name == "array")
            return (allowed.Name == "array" || (allowDomainWildcard && allowed.Name == "string")) && claimed.Elements().All(item =>
                (allowed.Name == "array" ? allowed.Elements() : [allowed]).Any(permission => Allows(permission, item, allowDomainWildcard)));
        if (claimed.Name == "dict")
            return allowed.Name == "dict" && claimed.Elements("key").All(key =>
                MacPropertyList.Get(allowed, key.Value) is { } permission && Allows(permission, key.ElementsAfterSelf().First(), allowDomainWildcard));
        if (claimed.Name == "string" && allowed.Name == "string")
        {
            // Profile wildcards grant a prefix; never interpret app claims as
            // glob patterns. Associated domains separately permits wildcard hosts.
            if (!allowDomainWildcard && claimed.Value.Contains('*', StringComparison.Ordinal)) return false;
            var wildcard = allowed.Value.IndexOf('*');
            return wildcard < 0 ? allowed.Value == claimed.Value
                : wildcard == allowed.Value.Length - 1 && claimed.Value.StartsWith(allowed.Value[..wildcard], StringComparison.Ordinal);
        }
        if (claimed.Name == "false" && allowed.Name == "true") return true;
        return XNode.DeepEquals(allowed, claimed);
    }
}
