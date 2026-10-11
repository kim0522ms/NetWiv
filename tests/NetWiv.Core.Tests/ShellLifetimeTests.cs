using System.Diagnostics;
using NetWiv;
using NetWiv.Plugin.Shell;

internal static class ShellLifetimeTests
{
    internal static async Task<int> Probe(string root, string role)
    {
        if (role == "parent")
        {
            var info = StartInfo(root, "leaf");
            using var child = Process.Start(info)!;
            File.WriteAllText(Path.Combine(root, "parent"), Environment.ProcessId.ToString());
            await child.WaitForExitAsync();
        }
        else
        {
            File.WriteAllText(Path.Combine(root, "leaf"), Environment.ProcessId.ToString());
            await Task.Delay(Timeout.Infinite);
        }
        return 0;
    }

    internal static async Task CancellationStopsProcessTree()
    {
        foreach (var scenario in new[] { "reload", "close", "dispose", "caller" })
        {
            var root = Directory.CreateTempSubdirectory("netwiv-shell-lifetime-").FullName;
            Process? parent = null, leaf = null;
            try
            {
                var info = StartInfo(root, "parent");
                var arguments = "[" + string.Join(',', info.ArgumentList.Select(value => JsonValue.From(value).GetRawText())) + "]";
                var scope = JsonValue.Parse("{\"name\":\"probe\",\"cmd\":" + JsonValue.From(info.FileName).GetRawText() + ",\"args\":" + arguments + "}");
                var runtime = new FakeRuntime();
                using var app = new Builder().UseRuntime(runtime).Plugin(new ShellPlugin())
                    .Capability(new("main", ["shell:allow-execute"], Windows: ["main"],
                        PermissionScopes: [new("shell:allow-execute", [scope], [])])).Build();
                app.Run(); runtime.Context!.OnDocumentChanged!(new("main", 1, false));
                using var cancellation = new CancellationTokenSource();
                var payload = "{\"program\":" + JsonValue.From(scenario == "caller" ? info.FileName : "probe").GetRawText() + ",\"args\":" + arguments + "}";
                var call = scenario == "caller"
                    ? app.Handle.InvokeAsync("plugin:shell|execute", JsonValue.Parse(payload), cancellation.Token).AsTask()
                    : Guest(runtime, payload);
                parent = await WaitForProcess(Path.Combine(root, "parent"));
                leaf = await WaitForProcess(Path.Combine(root, "leaf"));
                if (scenario == "caller") cancellation.Cancel();
                else if (scenario == "dispose") app.Dispose();
                else runtime.Context.OnDocumentChanged(new("main", scenario == "reload" ? 2UL : 1UL, scenario == "close"));
                try { await call.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("Cancelled shell execution succeeded."); }
                catch (OperationCanceledException) { }
                catch (InvokeException) when (scenario != "caller") { }
                await Task.Delay(100);
                Assert.True(!IsRunning(parent), $"Shell parent survived {scenario}.");
                Assert.True(!IsRunning(leaf), $"Shell descendant survived {scenario}.");
            }
            finally
            {
                foreach (var process in new[] { leaf, parent })
                {
                    if (process is null) continue;
                    if (IsRunning(process)) { process.Kill(true); await process.WaitForExitAsync(); }
                    process.Dispose();
                }
                Directory.Delete(root, true);
            }
        }
    }

    private static async Task<System.Text.Json.JsonElement> Guest(FakeRuntime runtime, string payload)
    {
        var response = await GuestIpc.Invoke(runtime, "plugin:shell|execute", payload, 1);
        if (response.Headers!["Tauri-Response"] == "error") throw new InvokeException(GuestIpc.Body(response));
        return JsonValue.Parse(GuestIpc.Body(response));
    }

    private static ProcessStartInfo StartInfo(string root, string role)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        info.ArgumentList.Add("--shell-execution-probe"); info.ArgumentList.Add(root); info.ArgumentList.Add(role);
        return info;
    }

    private static async Task<Process> WaitForProcess(string path)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path, stop.Token), out var id)) return Process.GetProcessById(id);
            }
            // The probe may still be writing the file; Windows rejects a concurrent read with a sharing violation.
            catch (IOException) { }
            await Task.Delay(20, stop.Token);
        }
    }

    private static bool IsRunning(Process process)
    {
        process.Refresh();
        if (process.HasExited) return false;
        // An orphaned Linux descendant may await init reaping after it was killed.
        return !OperatingSystem.IsLinux() || !File.ReadAllText($"/proc/{process.Id}/stat").Split(") ")[1].StartsWith("Z ", StringComparison.Ordinal);
    }
}
