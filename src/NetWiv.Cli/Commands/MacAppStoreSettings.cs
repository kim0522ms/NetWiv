using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal sealed record MacAppStoreSettings(string SigningIdentity, string? InstallerSigningIdentity, string? ProvisioningProfile, XDocument Entitlements)
{
    internal static MacAppStoreSettings Load(ConfigDocument config, bool noSign)
    {
        var node = config.Node("bundle", "macOS", "appStore");
        if (node is not null and not JsonObject) throw new NetWivConfigException("bundle.macOS.appStore must be an object.");
        if (node is JsonObject options)
            foreach (var option in options)
                if (option.Key is not ("signingIdentity" or "installerSigningIdentity" or "provisioningProfile") ||
                    (option.Value is not null && (option.Value is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))))
                    throw new NetWivConfigException("Unknown or invalid bundle.macOS.appStore option.");

        string? Setting(string name, string environment) => config.String("bundle", "macOS", "appStore", name) ?? Environment.GetEnvironmentVariable(environment);
        var identity = noSign ? "-" : Setting("signingIdentity", "APPLE_APP_STORE_SIGNING_IDENTITY");
        var installer = noSign ? null : Setting("installerSigningIdentity", "APPLE_APP_STORE_INSTALLER_IDENTITY");
        var profile = Setting("provisioningProfile", "APPLE_APP_STORE_PROVISIONING_PROFILE");
        if (!noSign)
        {
            if (identity is null || !(identity.StartsWith("Apple Distribution:", StringComparison.Ordinal) || identity.StartsWith("3rd Party Mac Developer Application:", StringComparison.Ordinal)))
                throw new NetWivConfigException("app-store requires an Apple Distribution or 3rd Party Mac Developer Application signingIdentity; Developer ID and ad-hoc identities are not accepted.");
            if (installer is null || !installer.StartsWith("3rd Party Mac Developer Installer:", StringComparison.Ordinal))
                throw new NetWivConfigException("app-store requires a Mac Installer Distribution certificate (3rd Party Mac Developer Installer) as installerSigningIdentity.");
            if (string.IsNullOrWhiteSpace(profile)) throw new NetWivConfigException("app-store requires a Mac App Store Connect provisioningProfile.");
        }
        if (profile is not null)
        {
            profile = Path.GetFullPath(profile, config.ConfigDirectory);
            if (!File.Exists(profile)) throw new FileNotFoundException("Mac App Store provisioning profile is missing.");
        }
        var path = config.String("bundle", "macOS", "entitlements")
            ?? throw new NetWivConfigException("app-store requires bundle.macOS.entitlements.");
        var entitlements = MacPropertyList.Load(Path.GetFullPath(path, config.ConfigDirectory));
        var dictionary = entitlements.Root!.Element("dict")!;
        foreach (var key in new[] { "com.apple.security.app-sandbox", "com.apple.security.network.client" })
            if (MacPropertyList.Get(dictionary, key)?.Name != "true")
                throw new NetWivConfigException($"app-store requires {key}=true for sandboxed WKWebView execution.");
        foreach (var key in new[] { "com.apple.security.get-task-allow", "get-task-allow", "com.apple.security.inherit" })
            if (MacPropertyList.Get(dictionary, key) is { Name.LocalName: not "false" })
                throw new NetWivConfigException($"app-store does not accept {key} on the main application.");
        return new(identity!, installer, profile, entitlements);
    }

    // Apply only signing identity entitlements. Other capabilities remain explicit app choices.
    internal static void ApplyProfile(XDocument profile, XDocument entitlements, string identifier, DateTimeOffset now)
    {
        var root = profile.Root!.Element("dict")!;
        var expiry = MacPropertyList.Get(root, "ExpirationDate");
        if (expiry?.Name != "date" || !DateTimeOffset.TryParse(expiry.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiration) || expiration <= now)
            throw new NetWivConfigException("The provisioning profile is expired or has no valid expiration date.");
        var allowed = MacPropertyList.Get(root, "Entitlements");
        if (allowed?.Name != "dict" || MacPropertyList.Get(root, "ProvisionedDevices") is not null ||
            MacPropertyList.Get(root, "ProvisionsAllDevices")?.Name == "true" ||
            MacPropertyList.Get(allowed, "get-task-allow")?.Name == "true" ||
            MacPropertyList.Get(allowed, "com.apple.security.get-task-allow")?.Name == "true")
            throw new NetWivConfigException("A Mac App Store Connect distribution profile is required.");
        var team = MacPropertyList.Get(allowed, "com.apple.developer.team-identifier");
        var app = MacPropertyList.Get(allowed, "com.apple.application-identifier");
        if (team?.Name != "string" || string.IsNullOrWhiteSpace(team.Value) || app?.Name != "string")
            throw new NetWivConfigException("The profile must contain macOS team and application identifiers.");
        // App ID prefixes need not equal Team IDs for transferred/older applications.
        var separator = app.Value.IndexOf('.');
        if (separator <= 0 || app.Value[(separator + 1)..] != identifier)
            throw new NetWivConfigException("The provisioning profile must match the configured bundle identifier exactly.");
        var dictionary = entitlements.Root!.Element("dict")!;
        foreach (var key in new[] { "com.apple.application-identifier", "com.apple.developer.team-identifier" })
        {
            var value = MacPropertyList.Get(allowed, key)!;
            if (MacPropertyList.Get(dictionary, key) is { } configured && !XNode.DeepEquals(configured, value))
                throw new NetWivConfigException("Configured signing entitlements do not match the provisioning profile.");
            MacPropertyList.Set(dictionary, key, value);
        }
    }

    internal static void ValidateSigningCertificate(XDocument profile, byte[] certificate)
    {
        var certificates = MacPropertyList.Get(profile.Root!.Element("dict")!, "DeveloperCertificates");
        if (certificates?.Name == "array")
            foreach (var value in certificates.Elements("data"))
            {
                byte[] allowed;
                try { allowed = Convert.FromBase64String(value.Value); }
                catch (FormatException) { throw new NetWivConfigException("Invalid certificate data in provisioning profile."); }
                if (certificate.AsSpan().SequenceEqual(allowed)) return;
            }
        throw new NetWivConfigException("The app signing certificate is not included in the provisioning profile.");
    }
}
