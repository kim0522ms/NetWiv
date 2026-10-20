using System.Text.Json.Nodes;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class BlazorModeTests
{
    internal static async Task Forwarding()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-blazor-mode-").FullName;
        var config = Path.Combine(root, "src-netwiv");
        Directory.CreateDirectory(config);
        var observedArgs = Path.Combine(root, "args.json");
        var prior = Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT");
        try
        {
            File.WriteAllText(Path.Combine(config, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(config, "netwiv.conf.json"), new JsonObject
            {
                ["identifier"] = "test.netwiv.blazormode",
                ["build"] = new JsonObject { ["dotnetProject"] = "Probe.csproj", ["runner"] = Environment.ProcessPath }
            }.ToJsonString());
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", observedArgs);
            var developmentOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (command, mode, outputName) in new (string[], string?, string?)[]
            {
                (["build", "--no-bundle"], null, null),
                (["build", "--no-bundle", "--aot", "--blazor-mode", "hybrid"], "Hybrid", null),
                (["dev", "--no-watch", "--no-dev-server", "--blazor-mode", "WebAssembly"], "WebAssembly", null),
                (["dev", "--no-watch", "--no-dev-server", "--aot"], null, "dev-aot"),
                (["dev", "--no-watch", "--no-dev-server", "--aot", "--blazor-mode", "Hybrid"], "Hybrid", "dev-aot-hybrid"),
                (["dev", "--no-watch", "--no-dev-server", "--aot", "--blazor-mode", "WebAssembly"], "WebAssembly", "dev-aot-webassembly"),
            })
            {
                var result = Environment.GetEnvironmentVariable("NETWIV_CLI_UNDER_TEST") is { } cli
                    ? (await ProcessRunner.RunAsync(new ProcessSpec(cli, command, root))).ExitCode
                    : await CliApp.RunAsync(command, TextWriter.Null, TextWriter.Null, root);
                if (result != 17) throw new InvalidOperationException("The selected build runner was not reached.");
                var arguments = JsonNode.Parse(File.ReadAllText(observedArgs))!["args"]!.AsArray()
                    .Select(value => value!.GetValue<string>()).ToArray();
                var properties = arguments.Where(value => value.Contains("NetWivBlazorMode=", StringComparison.Ordinal)).ToArray();
                if (mode is null && properties.Length != 0 || mode is not null &&
                    (properties.Length != 1 || !properties[0].EndsWith("NetWivBlazorMode=" + mode, StringComparison.Ordinal)))
                    throw new InvalidOperationException("The explicit Blazor mode was not passed to MSBuild, or the default was overridden.");
                if (command.Contains("--aot") && !arguments.Contains("-p:PublishAot=true"))
                    throw new InvalidOperationException("Selecting a Blazor mode changed the requested NativeAOT deployment.");
                if (outputName is not null)
                {
                    var outputPath = arguments[Array.IndexOf(arguments, "-o") + 1];
                    if (Path.GetFileName(outputPath) != outputName || !developmentOutputs.Add(outputPath))
                        throw new InvalidOperationException("AOT development reused another Blazor mode's publish directory or changed the default path.");
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT", prior);
            Directory.Delete(root, true);
        }
    }

    internal static async Task Validation()
    {
        foreach (var command in new[] { "build", "dev" })
        {
            var errors = new StringWriter();
            var result = await CliApp.RunAsync([command, "--blazor-mode", "invalid"], TextWriter.Null, errors, Path.GetTempPath());
            if (result == 0 || !errors.ToString().Contains("--blazor-mode must be Hybrid or WebAssembly", StringComparison.Ordinal))
                throw new InvalidOperationException("An invalid Blazor mode reached project or runner processing.");
        }
    }
}
