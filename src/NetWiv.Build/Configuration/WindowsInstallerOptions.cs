using System.Text;
using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

/// <summary>NetWiv's shared NSIS/MSI options; separate from upstream format-specific settings.</summary>
public sealed record WindowsInstallerOptions(
    bool PerMachine, bool AllowDirectorySelection, string? ShortcutName,
    IReadOnlyList<string> Arguments, string WorkingDirectory, bool? Desktop, bool StartMenu)
{
    public string RegistryHive => PerMachine ? "HKLM" : "HKCU";

    public static WindowsInstallerOptions FromJson(JsonObject root)
    {
        var windows = root["bundle"]?["windows"];
        if (windows is not null and not JsonObject) throw Error("bundle.windows must be an object");
        var owner = windows as JsonObject;
        var installer = Object(owner, "installer");
        Keys(installer, "scope", "allowDirectorySelection", "shortcuts");
        var scope = Text(installer, "scope") ?? "currentUser";
        if (scope is not ("currentUser" or "perMachine")) throw Error("scope must be currentUser or perMachine");
        var shortcuts = Object(installer, "shortcuts");
        Keys(shortcuts, "name", "arguments", "workingDirectory", "desktop", "startMenu");
        var name = Text(shortcuts, "name");
        if (name is not null) ValidateFileName(name, "shortcuts.name");
        var workingDirectory = Text(shortcuts, "workingDirectory") ?? ".";
        if (workingDirectory.Length > 1000) throw Error("shortcuts.workingDirectory exceeds 1000 characters");
        if (workingDirectory != ".")
            foreach (var segment in workingDirectory.Replace('/', '\\').Split('\\'))
                ValidateFileName(segment, "shortcuts.workingDirectory (a relative directory inside the payload)");
        var arguments = new List<string>();
        if (shortcuts?.ContainsKey("arguments") == true)
        {
            if (shortcuts["arguments"] is not JsonArray array) throw Error("shortcuts.arguments must be a string array");
            foreach (var node in array)
            {
                if (node is not JsonValue value || !value.TryGetValue<string>(out var argument) || argument.Any(char.IsControl))
                    throw Error("shortcuts.arguments entries must be strings without control characters");
                arguments.Add(argument);
            }
        }
        var result = new WindowsInstallerOptions(scope == "perMachine", Flag(installer, "allowDirectorySelection") ?? false,
            name, arguments, workingDirectory.Replace('/', '\\'), Flag(shortcuts, "desktop"), Flag(shortcuts, "startMenu") ?? true);
        // NSIS standard builds have 1024-character strings. Fail rather than silently truncate a launch command.
        if (result.CommandLine.Length > 1000) throw Error("shortcuts.arguments exceeds the supported 1000-character Windows command line");
        // Windows Shell expands paired percent signs when executing .lnk arguments.
        // Reject them instead of promising literal argv that the Shell cannot preserve.
        if (result.CommandLine.Count(c => c == '%') > 1)
            throw Error("shortcuts.arguments cannot contain paired percent signs because Windows Shell expands environment variables");
        return result;
    }

    public string CommandLine => string.Join(" ", Arguments.Select(QuoteArgument));

    // Windows CRT/CommandLineToArgvW quoting. Empty arguments and trailing backslashes need quotes too.
    public static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static void ValidateFileName(string value, string field)
    {
        var stem = value.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) ||
            stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]))
            throw Error(field + " must be a valid Windows file name");
    }

    private static JsonObject? Object(JsonObject? owner, string key)
    {
        if (owner?.ContainsKey(key) != true) return null;
        return owner[key] as JsonObject ?? throw Error(key + " must be an object");
    }
    private static string? Text(JsonObject? owner, string key)
    {
        if (owner?.ContainsKey(key) != true) return null;
        if (owner[key] is JsonValue value && value.TryGetValue<string>(out var text) && !text.Any(char.IsControl)) return text;
        throw Error(key + " must be a string without control characters");
    }
    private static bool? Flag(JsonObject? owner, string key)
    {
        if (owner?.ContainsKey(key) != true) return null;
        if (owner[key] is JsonValue value && value.TryGetValue<bool>(out var flag)) return flag;
        throw Error(key + " must be a boolean");
    }
    private static void Keys(JsonObject? owner, params string[] allowed)
    {
        if (owner is not null)
            foreach (var key in owner.Select(item => item.Key))
                if (!allowed.Contains(key, StringComparer.Ordinal)) throw Error("unknown property " + key);
    }
    private static NetWivConfigException Error(string message) => new("bundle.windows.installer: " + message + ".");
}
