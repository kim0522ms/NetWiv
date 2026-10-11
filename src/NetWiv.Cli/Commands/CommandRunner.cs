using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class CommandRunner
{
    internal static ProcessSpec Create(ConfigDocument config, string? commandOverride, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment, int verbLength = 1)
    {
        var runner = commandOverride is not null ? RunnerConfig.Parse(commandOverride)
            : config.Node("build", "runner") is { } value ? RunnerConfig.FromJson(value) : RunnerConfig.Default;
        // Tauri inserts runner args after the run/build verb, before normal args.
        // For .NET watch the verb also includes the restart/project options.
        var combined = arguments.Take(verbLength).Concat(runner.Args ?? []).Concat(arguments.Skip(verbLength)).ToArray();
        var cwd = runner.Cwd is null ? config.ProjectDirectory : Path.GetFullPath(runner.Cwd, config.ConfigDirectory);
        return new(runner.Command, combined, cwd, environment);
    }
}
