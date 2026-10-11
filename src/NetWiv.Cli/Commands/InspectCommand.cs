using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static class InspectCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help") || args.Length == 0) { await output.WriteLineAsync("Usage: netwiv inspect wix-upgrade-code [-c CONFIG]"); return 0; }
        if (arguments.TakePositional() != "wix-upgrade-code") throw new ArgumentException("Unknown inspect command.");
        var overlays = arguments.TakeOptions("--config", "-c"); arguments.EnsureEmpty();
        var config = ConfigLoader.Load(workingDirectory, "windows", overlays);
        var product = config.String("productName") ?? throw new NetWivConfigException("productName is required.");
        await output.WriteLineAsync(NativeBundler.Uuid5(product + ".exe.app.x64")); return 0;
    }
}
