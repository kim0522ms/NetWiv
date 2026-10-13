using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Os;

internal static class OsPluginTests
{
    internal static async Task GuestContract()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new OsPlugin()).Build();
        app.Run();
        var script = runtime.Context!.InitializationScript;
        const string marker = "window.__TAURI_OS_PLUGIN_INTERNALS__=";
        Assert.Contains(marker, script);
        var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        using var payload = JsonDocument.Parse(script[start..script.IndexOf(';', start)]);
        var values = payload.RootElement;
        Assert.Equal(7, values.EnumerateObject().Count());
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        Assert.Equal(platform, values.GetProperty("platform").GetString());
        Assert.Equal(platform, values.GetProperty("os_type").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? "windows" : "unix", values.GetProperty("family").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? "\r\n" : "\n", values.GetProperty("eol").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? "exe" : "", values.GetProperty("exe_extension").GetString());
        var architecture = OperatingSystem.IsMacOS() ? RuntimeInformation.ProcessArchitecture : RuntimeInformation.OSArchitecture;
        var expectedArch = architecture switch
        {
            Architecture.X64 => "x86_64", Architecture.X86 => "x86", Architecture.Arm64 => "aarch64",
            Architecture.Arm => "arm", _ => architecture.ToString().ToLowerInvariant()
        };
        Assert.Equal(expectedArch, values.GetProperty("arch").GetString());
        if (OperatingSystem.IsMacOS())
        {
            // Independent OS query: .NET Environment.OSVersion is the Darwin kernel version.
            using var versionQuery = Process.Start(new ProcessStartInfo("/usr/bin/sw_vers", "-productVersion")
                { UseShellExecute = false, RedirectStandardOutput = true })!;
            var output = versionQuery.StandardOutput.ReadToEndAsync();
            await Task.WhenAll(output, versionQuery.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            var productVersion = (await output).Trim().Split('.');
            Assert.Equal(0, versionQuery.ExitCode);
            Assert.Equal(string.Join('.', productVersion.Length == 2 ? [.. productVersion, "0"] : productVersion),
                values.GetProperty("version").GetString());
        }
        else Assert.Equal(Environment.OSVersion.Version.ToString(), values.GetProperty("version").GetString());
        Assert.Equal(System.Globalization.CultureInfo.CurrentCulture.Name,
            (await app.Handle.InvokeAsync("plugin:os|locale", JsonValue.EmptyObject)).GetString());
        Assert.Equal(Environment.MachineName,
            (await app.Handle.InvokeAsync("plugin:os|hostname", JsonValue.EmptyObject)).GetString());

        foreach (var policy in new[] { "default", "hostname", "deny", "none" })
        {
            var guest = new FakeRuntime();
            string[] permissions = policy switch
            {
                "default" => ["os:default"], "hostname" => ["os:default", "os:allow-hostname"],
                "deny" => ["os:default", "os:allow-hostname", "os:deny-hostname", "os:deny-locale"], _ => []
            };
            using var guarded = new Builder().UseRuntime(guest).Plugin(new OsPlugin())
                .Capability(new("main", permissions, Windows: ["main"])).Build();
            guarded.Run();
            foreach (var command in new[] { "locale", "hostname" })
            {
                var response = await GuestIpc.Invoke(guest, "plugin:os|" + command, "{}");
                var allowed = command == "locale" ? policy is "default" or "hostname" : policy == "hostname";
                Assert.Equal(allowed ? "ok" : "error", response.Headers!["Tauri-Response"]);
                if (allowed) Assert.Equal(JsonValue.From(command == "locale" ? System.Globalization.CultureInfo.CurrentCulture.Name
                    : Environment.MachineName).GetRawText(), GuestIpc.Body(response));
                else Assert.Contains("not allowed", GuestIpc.Body(response));
            }
        }

        MacOSVersionFailures();
    }

    private static void MacOSVersionFailures()
    {
        Assert.Equal("Unknown", OsPlugin.ReadMacOSVersion(new("netwiv-missing-sw-vers-" + Guid.NewGuid().ToString("N"))
            { UseShellExecute = false, RedirectStandardOutput = true }));
        foreach (var (output, expected) in new[] { ("26.0", "26.0.0"), ("15.5.1", "15.5.1"), ("26.0 beta", "26.0 beta"), ("", "Unknown") })
            Assert.Equal(expected, OsPlugin.ReadMacOSVersion(Shell(OperatingSystem.IsWindows()
                ? output.Length == 0 ? "exit /b 0" : "echo " + output : "printf '%s' '" + output + "'")));
        Assert.Equal("Unknown", OsPlugin.ReadMacOSVersion(Shell(OperatingSystem.IsWindows()
            ? "echo 26.0 & exit /b 7" : "printf '26.0'; exit 7")));
        var elapsed = Stopwatch.StartNew();
        Assert.Equal("Unknown", OsPlugin.ReadMacOSVersion(Shell(OperatingSystem.IsWindows()
            ? "ping -n 10 127.0.0.1 > nul" : "sleep 10"), timeoutMilliseconds: 100));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "OS version query exceeded its startup deadline.");
    }

    private static ProcessStartInfo Shell(string command)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) start.ArgumentList.Add("/d");
        start.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        start.ArgumentList.Add(command);
        return start;
    }
}
