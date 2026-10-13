using System.ComponentModel;
using System.Diagnostics;
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
            var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
            var architecture = OperatingSystem.IsMacOS() ? RuntimeInformation.ProcessArchitecture : RuntimeInformation.OSArchitecture;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("eol", OperatingSystem.IsWindows() ? "\r\n" : "\n");
                writer.WriteString("platform", platform);
                writer.WriteString("version", OperatingSystem.IsMacOS()
                    ? ReadMacOSVersion(new("/usr/bin/sw_vers", "-productVersion") { UseShellExecute = false, RedirectStandardOutput = true })
                    : Environment.OSVersion.Version.ToString());
                writer.WriteString("family", OperatingSystem.IsWindows() ? "windows" : "unix");
                writer.WriteString("os_type", platform);
                writer.WriteString("arch", architecture switch
                {
                    Architecture.X64 => "x86_64", Architecture.X86 => "x86", Architecture.Arm64 => "aarch64",
                    Architecture.Arm => "arm", _ => architecture.ToString().ToLowerInvariant()
                });
                writer.WriteString("exe_extension", OperatingSystem.IsWindows() ? "exe" : "");
                writer.WriteEndObject();
            }
            return "window.__TAURI_OS_PLUGIN_INTERNALS__=" + System.Text.Encoding.UTF8.GetString(stream.ToArray()) + ";";
        }
    }

    internal static string ReadMacOSVersion(ProcessStartInfo startInfo, int timeoutMilliseconds = 5000)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return "Unknown";
            var output = process.StandardOutput.ReadToEndAsync();
            try
            {
                Task.WhenAll(output, process.WaitForExitAsync())
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                return "Unknown";
            }
            if (process.ExitCode != 0) return "Unknown";
            var version = output.GetAwaiter().GetResult().Trim();
            if (version.Length == 0) return "Unknown";
            // os_info formats macOS product versions with three components, not the Darwin kernel version.
            return Version.TryParse(version, out var parsed) && parsed.Revision < 0
                ? FormattableString.Invariant($"{parsed.Major}.{parsed.Minor}.{Math.Max(0, parsed.Build)}")
                : version;
        }
        catch (Win32Exception) { return "Unknown"; }
        catch (IOException) { return "Unknown"; }
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
