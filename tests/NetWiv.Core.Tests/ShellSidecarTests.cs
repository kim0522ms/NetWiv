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
        var failure = windows ? new[] { "/d", "/c", "echo sidecar-out & echo sidecar-err 1>&2 & exit /b 17" }
            : ["-c", "printf sidecar-out; printf sidecar-err >&2; exit 17"];
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
            if (windows) File.Copy(source, target);
            else
            {
                // Execute the installed interpreter without relocating or re-signing an OS binary.
                // exec preserves the spawned PID and stdin ownership used by the lifetime checks below.
                await File.WriteAllTextAsync(target, "#!/bin/sh\nexec /bin/sh \"$@\"\n");
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            using var app = new Builder().UseRuntime(runtime).Plugin(new ShellPlugin(_ => { }, appDirectory))
                .Window("main", "Main", "index.html")
                .Window("other", "Other", "index.html")
                .Capability(new("main", ["shell:allow-execute", "shell:allow-spawn", "shell:allow-kill"], Windows: ["main", "other"],
                    PermissionScopes: [new("shell:allow-execute", [broad, normal], [denied]), new("shell:allow-spawn", [waiting], [])])).Build();
            app.Run();
            var result = await GuestIpc.Invoke(runtime, "plugin:shell|execute", Payload("helpers/tool", echo));
            Assert.Equal("ok", result.Headers!["Tauri-Response"]);
            var executed = JsonValue.Parse(GuestIpc.Body(result));
            Assert.True(executed.GetProperty("code").GetInt32() == 0 && executed.GetProperty("stderr").GetString()!.Length == 0,
                "Sidecar echo failed: " + Outcome(executed));
            Assert.True(executed.GetProperty("stdout").GetString()!.Trim() == "sidecar", "Sidecar echo did not forward expected stdout.");
            var failed = await GuestIpc.Invoke(runtime, "plugin:shell|execute", Payload("helpers/tool", failure));
            Assert.Equal("ok", failed.Headers!["Tauri-Response"]);
            var failureResult = JsonValue.Parse(GuestIpc.Body(failed));
            Assert.True(failureResult.GetProperty("code").GetInt32() == 17, "Sidecar exit status was not forwarded: " + Outcome(failureResult));
            Assert.True(failureResult.GetProperty("stdout").GetString()!.Trim() == "sidecar-out", "Nonzero sidecar did not forward expected stdout.");
            Assert.True(failureResult.GetProperty("stderr").GetString()!.Trim() == "sidecar-err", "Nonzero sidecar did not forward expected stderr: " + Outcome(failureResult));
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

    private static string Outcome(System.Text.Json.JsonElement result) =>
        "exit " + result.GetProperty("code").GetInt32() + "; stderr " +
        (result.GetProperty("stderr").GetString()!.Length == 0 ? "empty" : "non-empty");
}
