using System.Diagnostics;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class HookRunner
{
    public static async Task<Process?> RunAsync(ConfigDocument config, string name, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        var node = config.Node("build", name);
        if (node is null) return null;
        string script;
        string directory = config.ProjectDirectory;
        var wait = name != "beforeDevCommand";
        if (node is JsonValue value)
        {
            script = value.GetValue<string>();
            if (script.Length == 0) return null;
        }
        else if (node is JsonObject command)
        {
            script = command["script"]?.GetValue<string>() ?? throw new NetWivConfigException($"build.{name}.script is required.");
            if (name == "beforeDevCommand") wait = command["wait"]?.GetValue<bool>() ?? false;
            if (command["cwd"] is JsonValue cwd) directory = Path.GetFullPath(cwd.GetValue<string>(), config.ConfigDirectory);
        }
        else throw new NetWivConfigException($"build.{name} must be a string or command object.");

        cancellationToken.ThrowIfCancellationRequested();
        var process = ProcessRunner.StartShell(script, directory, environment);
        if (!wait) return process;
        var exitCode = await ProcessRunner.WaitOwnedAsync(process, cancellationToken);
        if (exitCode != 0) throw new InvalidOperationException($"build.{name} exited with code {exitCode}.");
        return null;
    }
}
