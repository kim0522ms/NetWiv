using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NetWiv.Plugin.DeepLink;

/// <summary>Receives configured deep links and registers desktop URL handlers.</summary>
public sealed class DeepLinkPlugin(IReadOnlyList<string>? arguments = null) : IPlugin
{
    private readonly IReadOnlyList<string>? _arguments = arguments;
    private readonly HashSet<string> _schemes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private AppHandle? _app;
    private string? _current;
    private IDisposable? _secondInstanceListener;

    public string Name => "deep-link";

    public void Initialize(PluginContext context)
    {
        var config = context.Config;
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty("desktop", out var desktop))
        {
            if (desktop.ValueKind == JsonValueKind.Object) ReadDesktop(desktop);
            else if (desktop.ValueKind == JsonValueKind.Array)
                foreach (var protocol in desktop.EnumerateArray()) ReadDesktop(protocol);
            else throw new InvokeException("Deep link desktop configuration must be an object or array.");
        }
        context.RegisterCommand("get_current", (_, _, _) => ValueTask.FromResult(Current()));
        context.RegisterCommand("register", (_, args, _) =>
        {
            Register(ReadProtocol(args)); return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("unregister", (_, args, _) =>
        {
            Unregister(ReadProtocol(args)); return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("is_registered", (_, args, _) =>
            ValueTask.FromResult(JsonValue.From(IsRegistered(ReadProtocol(args)))));
        foreach (var command in new[] { "get_current", "register", "unregister", "is_registered" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default", "allow-get-current");
    }

    public void Setup(AppHandle app)
    {
        _app = app;
        HandleLaunchArguments(_arguments ?? Environment.GetCommandLineArgs());
        _secondInstanceListener = app.Events.Listen("single-instance://new-instance", eventInfo =>
        {
            var arguments = eventInfo.Payload.GetProperty("args").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty).ToArray();
            HandleLaunchArguments(arguments);
        });
    }

    public void Dispose() => _secondInstanceListener?.Dispose();

    /// <summary>Pass the second process's argv here from SingleInstancePlugin's callback.</summary>
    public void HandleLaunchArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 2 || !Uri.TryCreate(arguments[1], UriKind.Absolute, out var url) ||
            !_schemes.Contains(url.Scheme)) return;
        lock (_gate) _current = url.AbsoluteUri;
        var payload = UrlArray(url.AbsoluteUri);
        _app?.Emit("deep-link://new-url", payload);
    }

    private JsonElement Current()
    {
        lock (_gate)
        {
            if (_current is null) return JsonValue.Null;
            return UrlArray(_current);
        }
    }

    private static JsonElement UrlArray(string url)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray(); writer.WriteStringValue(url); writer.WriteEndArray();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private void ReadDesktop(JsonElement desktop)
    {
        if (desktop.ValueKind != JsonValueKind.Object || !desktop.TryGetProperty("schemes", out var schemes) ||
            schemes.ValueKind != JsonValueKind.Array)
            throw new InvokeException("Deep link desktop configuration requires a schemes array.");
        foreach (var scheme in schemes.EnumerateArray())
        {
            if (scheme.ValueKind != JsonValueKind.String) throw new InvokeException("A deep link scheme must be a string.");
            _schemes.Add(ValidateProtocol(scheme.GetString()));
        }
    }

    private static string ReadProtocol(JsonElement args)
    {
        if (!args.TryGetProperty("protocol", out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvokeException("The protocol argument must be a string.");
        return ValidateProtocol(value.GetString());
    }

    private static string ValidateProtocol(string? protocol)
    {
        if (string.IsNullOrEmpty(protocol) || !char.IsAsciiLetter(protocol[0]) ||
            protocol.Skip(1).Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.')))
            throw new InvokeException("Invalid deep link protocol.");
        return protocol.ToLowerInvariant();
    }

    private void Register(string protocol)
    {
        if (OperatingSystem.IsWindows())
        {
            var executable = Executable();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + protocol, true);
            key.SetValue("", "URL:" + (_app?.Metadata.Identifier ?? "NetWiv") + " protocol");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon", true);
            icon.SetValue("", executable + ",0");
            using var command = key.CreateSubKey(@"shell\open\command", true);
            command.SetValue("", "\"" + executable + "\" \"%1\"");
            return;
        }
        EnsureLinux();
        var applications = ApplicationsPath();
        Directory.CreateDirectory(applications);
        var file = Path.Combine(applications, HandlerName());
        var mime = "x-scheme-handler/" + protocol;
        var existing = File.Exists(file) ? File.ReadAllLines(file) : [];
        var mimes = existing.FirstOrDefault(line => line.StartsWith("MimeType=", StringComparison.Ordinal))?
            .Substring("MimeType=".Length).Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        mimes.Add(mime);
        var launch = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : Executable();
        var lines = new[]
        {
            "[Desktop Entry]", "Type=Application", "Name=" + (_app?.Metadata.Name ?? "NetWiv"),
            "Exec=\"" + launch.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" %u",
            "Terminal=false", "MimeType=" + string.Join(';', mimes.Order(StringComparer.Ordinal)) + ";"
        };
        File.WriteAllLines(file, lines, new UTF8Encoding(false));
        RunCommand("update-desktop-database", applications);
        RunCommand("xdg-mime", "default", HandlerName(), mime);
    }

    private void Unregister(string protocol)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = @"Software\Classes\" + protocol;
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null) return;
            var command = key.OpenSubKey(@"shell\open\command")?.GetValue("") as string;
            if (command != "\"" + Executable() + "\" \"%1\"")
                throw new InvokeException("Refusing to unregister a protocol owned by another application.");
            Registry.CurrentUser.DeleteSubKeyTree(path);
            return;
        }
        EnsureLinux();
        var file = Path.Combine(ApplicationsPath(), HandlerName());
        if (!File.Exists(file)) return;
        var mime = "x-scheme-handler/" + protocol;
        var lines = File.ReadAllLines(file).Select(line => line.StartsWith("MimeType=", StringComparison.Ordinal)
            ? "MimeType=" + string.Join(';', line["MimeType=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(item => item != mime)) + ";" : line).ToArray();
        File.WriteAllLines(file, lines, new UTF8Encoding(false));
        RunCommand("update-desktop-database", ApplicationsPath());
    }

    private bool IsRegistered(string protocol)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.ClassesRoot.OpenSubKey(protocol + @"\shell\open\command");
            return key?.GetValue("") as string == "\"" + Executable() + "\" \"%1\"";
        }
        EnsureLinux();
        var result = RunCommand("xdg-mime", "query", "default", "x-scheme-handler/" + protocol);
        return result.Trim() == HandlerName();
    }

    private static string ApplicationsPath()
    {
        var root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "applications");
    }

    private static string HandlerName() => Path.GetFileName(Executable()) + "-handler.desktop";
    private static string Executable() => Environment.ProcessPath ?? throw new InvokeException("The app executable path is unavailable.");
    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Deep links support Windows and Linux desktop.");
    }

    private static string RunCommand(string name, params string[] arguments)
    {
        var start = new ProcessStartInfo(name) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvokeException("Could not start " + name + ".");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvokeException(name + " failed: " + stderr);
        return stdout;
    }
}
