// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class RunnerConfigTests
{
    internal static Task StringFormat()
    {
        var runner = Read("\"cargo\""); Check(runner, "cargo", null, null);
        Equal("\"cargo\"", runner.ToJson().ToJsonString()); return Task.CompletedTask;
    }
    internal static Task ObjectFull()
    {
        var runner = Read("""{"cmd":"my_runner","cwd":"/tmp/build","args":["--quiet","--verbose"]}""");
        Check(runner, "my_runner", "/tmp/build", ["--quiet", "--verbose"]);
        Equal(runner, RunnerConfig.FromJson(runner.ToJson())); return Task.CompletedTask;
    }
    internal static Task ObjectMinimal() { Check(Read("""{"cmd":"cross"}"""), "cross", null, null); return Task.CompletedTask; }
    internal static Task Default() { Check(RunnerConfig.Default, "dotnet", null, null); return Task.CompletedTask; }
    internal static Task FromStr() { Check(new("my_runner"), "my_runner", null, null); return Task.CompletedTask; }
    internal static Task FromString() { Check(new(string.Concat("another_", "runner")), "another_runner", null, null); return Task.CompletedTask; }
    internal static Task FromStrParse() { Check(RunnerConfig.Parse("parsed_runner"), "parsed_runner", null, null); return Task.CompletedTask; }
    internal static Task InBuildConfig()
    {
        var config = JsonNode.Parse("""{"runner":"cargo"}""")!;
        Check(RunnerConfig.FromJson(config["runner"]), "cargo", null, null); return Task.CompletedTask;
    }
    internal static Task InBuildConfigObject()
    {
        var config = JsonNode.Parse("""{"runner":{"cmd":"cross","cwd":"/workspace","args":["--target","x86_64-unknown-linux-gnu"]}}""")!;
        Check(RunnerConfig.FromJson(config["runner"]), "cross", "/workspace", ["--target", "x86_64-unknown-linux-gnu"]); return Task.CompletedTask;
    }
    internal static Task InFullConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-runner-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"productName":"Test App","version":"1.0.0","identifier":"com.test.app",
                 "build":{"runner":{"cmd":"my_custom_cargo","cwd":"/tmp/build","args":["--quiet","--verbose"]}}}
                """);
            var config = ConfigLoader.Load(root, OperatingSystem.IsWindows() ? "windows" : "linux");
            Check(RunnerConfig.FromJson(config.Node("build", "runner")), "my_custom_cargo", "/tmp/build", ["--quiet", "--verbose"]);
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
    internal static Task Equality()
    {
        var one = new RunnerConfig("cargo"); var two = new RunnerConfig("cargo"); var three = new RunnerConfig("cross");
        Equal(one, two); Equal(false, one.Equals(three));
        var four = new RunnerConfig("cargo", "/tmp", ["--quiet"]); var five = new RunnerConfig("cargo", "/tmp", ["--quiet"]);
        Equal(four, five); Equal(four.GetHashCode(), five.GetHashCode()); Equal(false, one.Equals(four)); return Task.CompletedTask;
    }
    internal static Task UntaggedSerialization()
    {
        Equal("\"cargo\"", new RunnerConfig("cargo").ToJson().ToJsonString());
        var value = new RunnerConfig("cross", null, null).ToJson().AsObject();
        Equal("cross", value["cmd"]!.GetValue<string>()); Equal(false, value.ContainsKey("cwd")); Equal(false, value.ContainsKey("args")); return Task.CompletedTask;
    }
    internal static Task Boundaries()
    {
        foreach (var value in new[] { "null", "1", "[]", "{}", "{\"cmd\":null}", "{\"cmd\":1}", "{\"cmd\":\"x\",\"cwd\":[]}",
            "{\"cmd\":\"x\",\"args\":\"a\"}", "{\"cmd\":\"x\",\"args\":[null]}", "{\"cmd\":\"x\",\"args\":[1]}" })
        {
            try { _ = Read(value); } catch (NetWivConfigException) { continue; }
            throw new Exception("Invalid runner accepted: " + value);
        }
        Check(Read("""{"cmd":"x","cwd":null,"args":null,"extension":true}"""), "x", null, null);
        var config = new ConfigDocument("config", Environment.CurrentDirectory, Environment.CurrentDirectory,
            JsonNode.Parse("""{"build":{"runner":{"cmd":"configured","cwd":"nested","args":["first","two words"]}}}""")!.AsObject(), []);
        var overridden = CommandRunner.Create(config, "override", ["publish", "project"], new Dictionary<string, string?>());
        Equal("override", overridden.FileName); Equal("publish|project", string.Join('|', overridden.Arguments));
        Equal(config.ProjectDirectory, overridden.WorkingDirectory);
        var configured = CommandRunner.Create(config, null, ["watch", "--project", "project", "run", "--", "app arg"], new Dictionary<string, string?>(), 4);
        Equal("watch|--project|project|run|first|two words|--|app arg", string.Join('|', configured.Arguments));
        Equal(Path.Combine(config.ConfigDirectory, "nested"), configured.WorkingDirectory);
        return Task.CompletedTask;
    }

    internal static async Task ChildProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-runner-child-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(configDir);
        var cwd = Path.Combine(configDir, "runner cwd"); Directory.CreateDirectory(cwd);
        var report = Path.Combine(root, "probe.json");
        var prior = Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT");
        try
        {
            var runner = new RunnerConfig(Environment.ProcessPath!, "runner cwd", ["--runner-flag", "two words"]);
            var config = new JsonObject { ["identifier"] = "test.netwiv.runner", ["build"] = new JsonObject { ["dotnetProject"] = "Probe.csproj", ["runner"] = runner.ToJson() } };
            File.WriteAllText(Path.Combine(configDir, "netwiv.conf.json"), config.ToJsonString());
            File.WriteAllText(Path.Combine(configDir, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", report);
            foreach (var mode in new[] { "build", "managed", "aot" })
            {
                var result = Environment.GetEnvironmentVariable("NETWIV_CLI_UNDER_TEST") is { } cli
                    ? (await ProcessRunner.RunAsync(new ProcessSpec(cli,
                        mode == "build" ? ["build", "--no-bundle"] : mode == "aot"
                            ? ["dev", "--no-watch", "--no-dev-server", "--aot"] : ["dev", "--no-watch", "--no-dev-server"], root))).ExitCode
                    : mode == "build"
                    ? await BuildCommand.RunAsync(["--no-bundle"], TextWriter.Null, root, CancellationToken.None)
                    : await DevCommand.RunAsync(mode == "aot" ? ["--no-watch", "--no-dev-server", "--aot"] : ["--no-watch", "--no-dev-server"], TextWriter.Null, root, CancellationToken.None);
                Equal(17, result);
                var observed = JsonNode.Parse(File.ReadAllText(report))!;
                Equal(CanonicalPath(cwd), observed["cwd"]!.GetValue<string>());
                var args = observed["args"]!.AsArray();
                Equal(mode == "managed" ? "run" : "publish", args[0]!.GetValue<string>());
                Equal("--runner-flag", args[1]!.GetValue<string>()); Equal("two words", args[2]!.GetValue<string>());
            }
        }
        finally { Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", prior); Directory.Delete(root, true); }
    }

    private static RunnerConfig Read(string json) => RunnerConfig.FromJson(JsonNode.Parse(json));
    private static void Check(RunnerConfig runner, string command, string? cwd, string[]? args)
    {
        Equal(command, runner.Command); Equal(cwd, runner.Cwd);
        Equal(args is null, runner.Args is null);
        if (args is not null) Equal(string.Join('|', args), string.Join('|', runner.Args!));
    }
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
