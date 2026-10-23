using System.Diagnostics;
using NetWiv;
using NetWiv.Plugin.Shell;

internal static class ShellSidecarTests
{
    internal static async Task ScopedSidecars()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-sidecar-").FullName;
        var directory = root;
        var appDirectory = root;
        if (OperatingSystem.IsMacOS())
        {
            directory = Directory.CreateDirectory(Path.Combine(root, "Sidecar.app", "Contents", "MacOS")).FullName;
            appDirectory = Directory.CreateDirectory(Path.Combine(root, "Sidecar.app", "Contents", "Resources")).FullName;
        }
        var windows = OperatingSystem.IsWindows();
        var filename = windows ? "tool.exe" : "tool";
        var target = Path.Combine(directory, filename);
        var source = windows ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/sh";
        var echo = windows ? new[] { "/d", "/c", "echo sidecar" } : ["-c", "printf sidecar"];
        var wait = windows ? new[] { "/d", "/c", "set /p value=" } : ["-c", "read value"];
        static string Array(string[] values) => "[" + string.Join(',', values.Select(value => JsonValue.From(value).GetRawText())) + "]";
        static string Payload(string name, string[] values, bool sidecar = true) =>
            "{\"program\":" + JsonValue.From(name).GetRawText() + ",\"args\":" + Array(values) +
            ",\"options\":{\"sidecar\":" + (sidecar ? "true" : "false") + "}}";
        var waiting = JsonValue.Parse("{\"name\":\"helpers/tool\",\"sidecar\":true,\"args\":" + Array(wait) + "}");
        var denied = JsonValue.Parse("{\"name\":\"helpers/tool\",\"sidecar\":true,\"args\":[\"denied\"]}");
        var broad = JsonValue.Parse("{\"name\":\"helpers/tool\",\"sidecar\":true,\"args\":true}");
        var normal = JsonValue.Parse("{\"name\":\"normal\",\"cmd\":" + JsonValue.From(source).GetRawText() + ",\"args\":true}");
        var runtime = new FakeRuntime();
        Process? child = null;
        try
        {
            File.Copy(source, target);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(source));
            if (OperatingSystem.IsMacOS())
            {
                var signing = new ProcessStartInfo("/usr/bin/codesign")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    ArgumentList = { "--force", "--sign", "-", target }
                };
                using var signer = Process.Start(signing) ?? throw new Exception("Could not sign the macOS sidecar fixture.");
                var output = signer.StandardOutput.ReadToEndAsync();
                var error = signer.StandardError.ReadToEndAsync();
                await signer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(output, error);
                Assert.Equal(0, signer.ExitCode);
            }
            using var app = new Builder().UseRuntime(runtime).Plugin(new ShellPlugin(_ => { }, appDirectory))
                .Window("main", "Main", "index.html")
                .Window("other", "Other", "index.html")
                .Capability(new("main", ["shell:allow-execute", "shell:allow-spawn", "shell:allow-kill"], Windows: ["main", "other"],
                    PermissionScopes: [new("shell:allow-execute", [broad, normal], [denied]), new("shell:allow-spawn", [waiting], [])])).Build();
            app.Run();
            var result = await GuestIpc.Invoke(runtime, "plugin:shell|execute", Payload("helpers/tool", echo));
            Assert.Equal("ok", result.Headers!["Tauri-Response"]);
            Assert.Contains("sidecar", JsonValue.Parse(GuestIpc.Body(result)).GetProperty("stdout").GetString()!);
            foreach (var payload in new[] { Payload("helpers/tool", ["denied"]), Payload("helpers/tool", echo, false), Payload("normal", echo),
                Payload("../tool", echo), Payload(target, echo) })
            {
                var rejected = await GuestIpc.Invoke(runtime, "plugin:shell|execute", payload);
                Assert.Equal("error", rejected.Headers!["Tauri-Response"]);
            }
            // Trusted backend calls bypass ACL but never bypass bundled-path validation.
            foreach (var name in new[] { "../tool", "/tool", "C:\\tool", "helpers/./tool", "helpers//tool", "missing" })
            {
                try { await app.Handle.InvokeAsync("plugin:shell|execute", JsonValue.Parse(Payload(name, echo))); throw new Exception("Invalid sidecar was accepted."); }
                catch (InvokeException) { }
            }
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(directory, "escape");
                File.CreateSymbolicLink(link, source);
                try { await app.Handle.InvokeAsync("plugin:shell|execute", JsonValue.Parse(Payload("escape", echo))); throw new Exception("Escaping sidecar symlink was accepted."); }
                catch (InvokeException) { }
            }
            var spawn = await GuestIpc.Invoke(runtime, "plugin:shell|spawn", Payload("helpers/tool", wait)[..^1] + ",\"onEvent\":\"__CHANNEL__:71\"}");
            Assert.Equal("ok", spawn.Headers!["Tauri-Response"]);
            var pid = JsonValue.Parse(GuestIpc.Body(spawn)).GetInt32();
            child = Process.GetProcessById(pid);
            var kill = "{\"pid\":" + pid + "}";
            var wrongOwner = await GuestIpc.Invoke(runtime, "plugin:shell|kill", kill, webviewLabel: "other");
            Assert.Equal("error", wrongOwner.Headers!["Tauri-Response"]);
            Assert.Contains("another document", GuestIpc.Body(wrongOwner));
            var killed = await GuestIpc.Invoke(runtime, "plugin:shell|kill", kill);
            Assert.Equal("ok", killed.Headers!["Tauri-Response"]);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            child.Dispose();
            child = null;
            foreach (var shutdown in new[] { false, true })
            {
                var spawned = await GuestIpc.Invoke(runtime, "plugin:shell|spawn",
                    Payload("helpers/tool", wait)[..^1] + ",\"onEvent\":\"__CHANNEL__:72\"}", shutdown ? 1UL : 0UL);
                Assert.Equal("ok", spawned.Headers!["Tauri-Response"]);
                child = Process.GetProcessById(JsonValue.Parse(GuestIpc.Body(spawned)).GetInt32());
                if (shutdown) app.Dispose();
                else runtime.Context!.OnDocumentChanged!(new("main", 1, false));
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                child.Dispose();
                child = null;
            }
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            Directory.Delete(root, true);
        }
    }
}
