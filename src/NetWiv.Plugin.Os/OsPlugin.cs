using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NetWiv.Plugin.Os;

/// <summary>Desktop information exposed by the official @tauri-apps/plugin-os guest.</summary>
public sealed class OsPlugin : IPlugin
{
    public string Name => "os";

    public string InitializationScript
    {
        get
        {
            var platform = OperatingSystem.IsWindows() ? "windows" : "linux";
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("eol", OperatingSystem.IsWindows() ? "\r\n" : "\n");
                writer.WriteString("platform", platform);
                writer.WriteString("version", Environment.OSVersion.Version.ToString());
                writer.WriteString("family", OperatingSystem.IsWindows() ? "windows" : "unix");
                writer.WriteString("os_type", platform);
                writer.WriteString("arch", RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "x86_64", Architecture.X86 => "x86", Architecture.Arm64 => "aarch64",
                    Architecture.Arm => "arm", _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
                });
                writer.WriteString("exe_extension", OperatingSystem.IsWindows() ? "exe" : "");
                writer.WriteEndObject();
            }
            return "window.__TAURI_OS_PLUGIN_INTERNALS__=" + System.Text.Encoding.UTF8.GetString(stream.ToArray()) + ";";
        }
    }

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("locale", static (_, _, _) => ValueTask.FromResult(JsonValue.From(CultureInfo.CurrentCulture.Name)));
        context.RegisterCommand("hostname", static (_, _, _) => ValueTask.FromResult(JsonValue.From(Environment.MachineName)));
        foreach (var command in new[] { "locale", "hostname" })
        {
            context.Permission(new("allow-" + command, [command], []));
            context.Permission(new("deny-" + command, [], [command]));
        }
        foreach (var name in new[] { "arch", "exe-extension", "family", "os-type", "platform", "version" })
        {
            context.Permission(new("allow-" + name, [], []));
            context.Permission(new("deny-" + name, [], []));
        }
        context.PermissionSet("default", "allow-arch", "allow-exe-extension", "allow-family", "allow-locale",
            "allow-os-type", "allow-platform", "allow-version");
    }
}
