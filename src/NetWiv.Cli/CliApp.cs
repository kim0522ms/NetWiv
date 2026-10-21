using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

public static class CliApp
{
    public static string Version { get; } = System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(CliApp).Assembly)!
        .InformationalVersion.Split('+')[0];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, string workingDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                await output.WriteLineAsync(HelpText.Root);
                return 0;
            }
            if (args[0] is "--version" or "-V")
            {
                await output.WriteLineAsync($"netwiv-cli {Version}");
                return 0;
            }

            var tail = args[1..];
            return args[0] switch
            {
                "new" => await NewCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "init" => await InitCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "dev" => await DevCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "build" => await BuildCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "bindings" => await BindingsCommand.RunAsync(tail, output, workingDirectory),
                "bundle" => await BundleCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "info" => await InfoCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "icon" => await IconCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "completions" => await CompletionsCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "signer" => await SignerCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "permission" => await PermissionCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "capability" => await CapabilityCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "inspect" => await InspectCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "mcp" => await McpCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "plugin" => await PluginCommand.RunAsync(tail, output, workingDirectory, cancellationToken),
                "add" => await PluginCommand.AddAsync(tail, output, workingDirectory, cancellationToken),
                "remove" => await PluginCommand.RemoveAsync(tail, output, workingDirectory, cancellationToken),
                "migrate" => throw new NotSupportedException("`netwiv migrate` targets Tauri v1 Rust projects and has no safe automatic .NET translation. Migrate configuration and commands explicitly."),
                "android" or "ios" => throw new NotSupportedException($"`netwiv {args[0]}` is outside the Windows/Linux desktop milestone."),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'. Run `netwiv --help`.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("netwiv: cancelled");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or NetWivConfigException or NotSupportedException or TimeoutException or BadImageFormatException)
        {
            await error.WriteLineAsync($"netwiv: {exception.Message}");
            return 1;
        }
    }
}
