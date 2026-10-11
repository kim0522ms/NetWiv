using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class HookTests
{
    internal static async Task<int> Probe(string report, string code, string prerequisite)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (prerequisite != "-" && !File.Exists(prerequisite)) await Task.Delay(20, timeout.Token);
        await Task.Delay(150, timeout.Token);
        File.WriteAllText(report, new JsonObject { ["cwd"] = Environment.CurrentDirectory,
            ["patch"] = Environment.GetEnvironmentVariable("TAURI_CONFIG") }.ToJsonString());
        return int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task Completion()
    {
        var root = CreateRoot();
        try
        {
            var cwd = Path.Combine(root, "src-netwiv", "hook cwd"); Directory.CreateDirectory(cwd);
            foreach (var name in new[] { "beforeBuildCommand", "beforeBundleCommand", "beforeDevCommand" })
            foreach (var exit in new[] { 0, 23 })
            {
                var report = Path.Combine(root, "hook.json"); File.Delete(report);
                var command = new JsonObject { ["script"] = Script(report, exit), ["cwd"] = "hook cwd" };
                if (name == "beforeDevCommand") command["wait"] = true;
                var config = Config(root, new JsonObject { [name] = command });
                System.Diagnostics.Process? process = null;
                try
                {
                    process = await HookRunner.RunAsync(config, name, new Dictionary<string, string?>(), CancellationToken.None);
                    if (exit != 0) throw new Exception("Failing hook was accepted.");
                    if (process is not null || !File.Exists(report)) throw new Exception("Hook returned before completion.");
                    Equal(CanonicalPath(cwd), JsonNode.Parse(File.ReadAllText(report))!["cwd"]!.GetValue<string>());
                }
                catch (InvalidOperationException error) when (exit != 0 && error.Message.Contains("23", StringComparison.Ordinal)) { }
                finally
                {
                    if (process is not null) { await ProcessRunner.StopAsync(process); process.Dispose(); }
                }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task BuildEnvironment()
    {
        var root = CreateRoot();
        var report = Path.Combine(root, "hook.json"); var runnerReport = Path.Combine(root, "runner.json");
        var prior = Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT");
        try
        {
            var config = Config(root, new JsonObject {
                ["beforeBuildCommand"] = new JsonObject { ["script"] = Script(report, 0) },
                ["runner"] = Environment.ProcessPath,
                ["dotnetProject"] = "Probe.csproj" });
            File.WriteAllText(config.BasePath, config.Root.ToJsonString());
            File.WriteAllText(Path.Combine(config.ConfigDirectory, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", runnerReport);
            string[] arguments = ["build", "--no-bundle", "-c", """{"plugins":{"keep":1,"remove":1}}""",
                "-c", """{"plugins":{"remove":null}}"""];
            Equal(17, await RunCli(arguments, root, CancellationToken.None));
            if (!File.Exists(report)) throw new Exception("Build runner started before its hook finished.");
            var hook = JsonNode.Parse(File.ReadAllText(report))!;
            var runner = JsonNode.Parse(File.ReadAllText(runnerReport))!;
            var patch = JsonNode.Parse("""{"plugins":{"keep":1,"remove":null}}""");
            foreach (var observed in new[] { hook["patch"]?.GetValue<string>(), runner["patch"]?.GetValue<string>() })
                if (observed is null || !JsonNode.DeepEquals(patch, JsonNode.Parse(observed)))
                    throw new Exception("Hook/runner did not receive the composed TAURI_CONFIG patch: " + observed);
            Equal(CanonicalPath(root), hook["cwd"]!.GetValue<string>());
        }
        finally { Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", prior); Directory.Delete(root, true); }
    }

    internal static async Task DevFailure()
    {
        var root = CreateRoot();
        var report = Path.Combine(root, "hook.json"); var runnerReport = Path.Combine(root, "runner.json");
        var priorOutput = Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT");
        var priorWait = Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_WAIT");
        try
        {
            var config = Config(root, new JsonObject {
                ["beforeDevCommand"] = new JsonObject { ["script"] = Script(report, 23, runnerReport) },
                ["runner"] = Environment.ProcessPath, ["dotnetProject"] = "Probe.csproj" });
            File.WriteAllText(config.BasePath, config.Root.ToJsonString());
            File.WriteAllText(Path.Combine(config.ConfigDirectory, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", runnerReport);
            foreach (var aot in new[] { false, true })
            foreach (var exit in new[] { 0, 23 })
            {
                config.Root["build"]!["beforeDevCommand"]!["script"] = Script(report, exit, runnerReport);
                File.WriteAllText(config.BasePath, config.Root.ToJsonString());
                Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_WAIT", exit == 0 ? report : "1");
                File.Delete(report); File.Delete(runnerReport);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var args = new List<string> { "dev", "--no-watch", "--no-dev-server" };
                if (aot) args.Add("--aot");
                Equal(exit == 0 ? 17 : 23, await RunCli(args.ToArray(), root, timeout.Token));
                if (!File.Exists(report) || !File.Exists(runnerReport)) throw new Exception("Hook failure did not follow runner startup.");
                var pid = JsonNode.Parse(File.ReadAllText(runnerReport))!["pid"]!.GetValue<int>();
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (!process.HasExited) throw new Exception("Development runner survived the failing hook.");
                }
                catch (ArgumentException) { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", priorOutput);
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_WAIT", priorWait);
            Directory.Delete(root, true);
        }
    }

    internal static Task Configuration()
    {
        var root = CreateRoot();
        try
        {
            var file = Path.Combine(root, "src-netwiv", "netwiv.conf.json");
            foreach (var name in new[] { "beforeBuildCommand", "beforeBundleCommand", "beforeDevCommand" })
            foreach (var value in new[] { "[]", "true", "{}", "{\"script\":null}", "{\"script\":42}", "{\"script\":\"x\",\"cwd\":42}" }
                .Concat(name == "beforeDevCommand" ? ["{\"script\":\"x\",\"wait\":null}", "{\"script\":\"x\",\"wait\":\"true\"}"] : Array.Empty<string>()))
            {
                File.WriteAllText(file, new JsonObject { ["build"] = new JsonObject { [name] = JsonNode.Parse(value) } }.ToJsonString());
                try { ConfigLoader.Load(root, "linux"); throw new Exception("Invalid hook accepted: " + value); }
                catch (NetWivConfigException) { }
            }
            File.WriteAllText(file, """{"build":{"beforeBuildCommand":{"script":"","cwd":null,"wait":false},"beforeDevCommand":{"script":"","wait":false}}}""");
            ConfigLoader.Load(root, "linux");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static async Task<int> RunCli(string[] arguments, string root, CancellationToken token) =>
        Environment.GetEnvironmentVariable("NETWIV_CLI_UNDER_TEST") is { } cli
            ? (await ProcessRunner.RunAsync(new ProcessSpec(cli, arguments, root), token)).ExitCode
            : await CliApp.RunAsync(arguments, TextWriter.Null, TextWriter.Null, root, token);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src-netwiv")); return root;
    }
    private static ConfigDocument Config(string root, JsonObject build) => new(
        Path.Combine(root, "src-netwiv", "netwiv.conf.json"), Path.Combine(root, "src-netwiv"), root,
        new JsonObject { ["identifier"] = "com.test.hook", ["build"] = build }, []);
    private static string Script(string report, int exit, string prerequisite = "-") =>
        string.Join(' ', new[] { Environment.ProcessPath!, "--hook-probe", report, exit.ToString(System.Globalization.CultureInfo.InvariantCulture), prerequisite }
            .Select(value => OperatingSystem.IsWindows() ? "\"" + value.Replace("\"", "\"\"") + "\"" : "'" + value.Replace("'", "'\"'\"'") + "'"));
    private static string CanonicalPath(string path)
    {
        if (!OperatingSystem.IsMacOS()) return path;
        var current = Environment.CurrentDirectory;
        try { Environment.CurrentDirectory = path; return Environment.CurrentDirectory; }
        finally { Environment.CurrentDirectory = current; }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
    }
}
