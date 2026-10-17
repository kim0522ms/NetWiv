using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class BundleCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        if (args.Contains("--help")) { await output.WriteLineAsync("Usage: netwiv bundle [--debug] [-b BUNDLES] [-t TARGET] [-c CONFIG] [--no-sign]"); return 0; }
        var arguments = new CommandArguments(args);
        var debug = arguments.TakeFlag("--debug", "-d");
        var noSign = arguments.TakeFlag("--no-sign");
        var bundles = arguments.TakeOption("--bundles", "-b");
        var features = arguments.TakeOption("--features", "-f");
        arguments.TakeFlag("--ci");
        var (config, target) = CommandConfig.Load(arguments, workingDirectory);
        arguments.EnsureEmpty();
        if (features is not null)
            throw new NotSupportedException("--features affects compilation and cannot be applied by `netwiv bundle`; pass it to `netwiv build`.");
        target.EnsureHostOperatingSystem();
        NativeBundler.ValidateConfiguration(config, target, bundles, noSign);
        var project = CommandConfig.Project(config);
        var publishDirectory = Path.Combine(config.ProjectDirectory, "target", target.RequestedTarget, debug ? "debug" : "release");
        BuildCommand.EnsureBinary(config, project, publishDirectory);
        await HookRunner.RunAsync(config, "beforeBundleCommand", HookEnvironment.Create(target, debug, config), cancellationToken);
        return await NativeBundler.BundleAsync(config, target, publishDirectory, bundles, noSign, output, cancellationToken);
    }
}
