using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NetWiv.Plugin.Autostart;

/// <summary>Registers a desktop app at user login for the official autostart guest.</summary>
public sealed class AutostartPlugin : IPlugin
{
    private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string[] _arguments;
    private readonly string? _name;
    private string? _resolvedName;

    public AutostartPlugin(string? appName = null, params string[] arguments)
    {
        if (appName is not null) ValidateName(appName);
        _name = appName;
        _arguments = arguments;
    }

    public string Name => "autostart";
    public void Setup(AppHandle app)
    {
        var name = _name ?? app.Metadata.Name;
        ValidateName(name);
        _resolvedName = name;
    }

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("enable", (_, _, _) =>
        {
            Enable(); return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("disable", (_, _, _) =>
        {
            Disable(); return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("is_enabled", (_, _, _) => ValueTask.FromResult(JsonValue.From(IsEnabled())));
        foreach (var command in new[] { "enable", "disable", "is_enabled" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default", "allow-enable", "allow-disable", "allow-is-enabled");
    }

    private string EntryName => _resolvedName ?? _name ?? throw new InvokeException("Autostart plugin has not started.");

    private void Enable()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKey, true);
            key.SetValue(EntryName, string.Join(' ', LaunchCommand().Select(QuoteWindows)), RegistryValueKind.String);
            return;
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Autostart supports Windows and Linux desktop.");
        var path = LinuxEntryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var command = string.Join(' ', LaunchCommand().Select(QuoteDesktop));
        var content = "[Desktop Entry]\nType=Application\nName=" + EntryName + "\nExec=" + command +
            "\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private void Disable()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(WindowsRunKey, true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
            return;
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Autostart supports Windows and Linux desktop.");
        File.Delete(LinuxEntryPath());
    }

    private bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(WindowsRunKey);
            return key?.GetValue(EntryName) is string;
        }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Autostart supports Windows and Linux desktop.");
        return File.Exists(LinuxEntryPath());
    }

    private string LinuxEntryPath()
    {
        var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "autostart", EntryName + ".desktop");
    }

    private IEnumerable<string> LaunchCommand()
    {
        var executable = OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
            ? appImage : Environment.ProcessPath ?? throw new InvokeException("The executable path is unavailable.");
        yield return executable;
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            yield return Environment.GetCommandLineArgs()[0];
        foreach (var argument in _arguments) yield return argument;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Autostart app name must be a single file and registry value name.", nameof(name));
    }

    private static string QuoteWindows(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            result.Append(character);
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
    private static string QuoteDesktop(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("$", "\\$").Replace("`", "\\`").Replace("%", "%%") + "\"";
}
