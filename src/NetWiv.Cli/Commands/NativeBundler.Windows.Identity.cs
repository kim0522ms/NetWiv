using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static partial class NativeBundler
{
    private static string WindowsInstallerIdentifier(ConfigDocument config)
    {
        if (config.Root["identifier"] is not JsonValue value || !value.TryGetValue<string>(out var identifier) ||
            string.IsNullOrWhiteSpace(identifier) ||
            identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-'))
            throw new NetWivConfigException("Windows installers require a nonempty 'identifier' containing only ASCII letters, digits, hyphens, and periods.");
        return identifier;
    }
}
