using NetWiv.Build.Frontend;

namespace NetWiv.Cli;

internal static class BindingsCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory)
    {
        if (args is ["--help"] or ["-h"])
        {
            await output.WriteLineAsync("Usage: netwiv bindings <managed-assembly.dll> <output-directory>");
            return 0;
        }
        if (args.Length != 2) throw new ArgumentException("Usage: netwiv bindings <managed-assembly.dll> <output-directory>");
        var source = Path.GetFullPath(args[0], workingDirectory);
        var destination = Path.GetFullPath(args[1], workingDirectory);
        var bindings = FrontendBindings.Read(source);
        bindings.WriteTo(destination);
        await output.WriteLineAsync(bindings.FactoryJavaScript is null
            ? $"Generated commands.js and commands.d.ts in {destination}"
            : $"Generated commands.js, commands.d.ts, commands.factory.js and commands.factory.d.ts in {destination}");
        return 0;
    }
}
