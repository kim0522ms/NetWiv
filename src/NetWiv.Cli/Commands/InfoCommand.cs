using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class InfoCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help")) { await output.WriteLineAsync("Usage: netwiv info [--interactive]"); return 0; }
        var interactive = arguments.TakeFlag("--interactive");
        var verbose = arguments.TakeFlag("--verbose", "-v");
        arguments.EnsureEmpty();
        var dotnet = await ProcessRunner.RunAsync(new ProcessSpec("dotnet", ["--version"], workingDirectory, CaptureOutput: true), cancellationToken);
        await output.WriteLineAsync($"netwiv CLI: {CliApp.Version}");
        await output.WriteLineAsync($".NET SDK: {dotnet.StandardOutput.Trim()}");
        await output.WriteLineAsync($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        await output.WriteLineAsync($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        if (verbose)
        {
            await output.WriteLineAsync($"Process: {Environment.ProcessPath}");
            await output.WriteLineAsync($"Working directory: {Path.GetFullPath(workingDirectory)}");
        }
        try
        {
            var platform = BuildTarget.Resolve(null).Platform;
            var config = ConfigLoader.Load(workingDirectory, platform);
            await output.WriteLineAsync($"Config: {config.BasePath}");
            foreach (var source in config.Sources.Skip(1)) await output.WriteLineAsync($"Merged: {source}");
            await output.WriteLineAsync($"Identifier: {config.String("identifier") ?? "<missing>"}");
            _ = CommandConfig.Project(config);
            await output.WriteLineAsync("Project: valid");
        }
        catch (Exception exception) when (exception is NetWivConfigException or IOException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Project: invalid ({exception.Message})");
            if (interactive) throw new NotSupportedException("Interactive automatic fixes are not implemented; no project files were changed.");
        }
        return 0;
    }
}
