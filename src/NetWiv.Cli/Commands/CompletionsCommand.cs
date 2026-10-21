namespace NetWiv.Cli;

internal static class CompletionsCommand
{
    private const string Commands = "new init dev build bindings bundle info add remove plugin icon signer completions permission capability inspect mcp";
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help")) { await output.WriteLineAsync("Usage: netwiv completions --shell <bash|zsh|fish|powershell|elvish> [-o OUTPUT]"); return 0; }
        var shell = arguments.TakeOption("--shell", "-s") ?? throw new ArgumentException("--shell is required.");
        var destination = arguments.TakeOption("--output", "-o");
        arguments.EnsureEmpty();
        var script = shell switch
        {
            "bash" => $"complete -W \"{Commands}\" netwiv\n",
            "zsh" => $"#compdef netwiv\n_arguments '1:command:({Commands})'\n",
            "fish" => string.Join('\n', Commands.Split(' ').Select(command => $"complete -c netwiv -f -a {command}")) + "\n",
            "powershell" => $"Register-ArgumentCompleter -Native -CommandName netwiv -ScriptBlock {{ param($wordToComplete) '{Commands.Replace(" ", "','", StringComparison.Ordinal)}' | Where-Object {{ $_ -like \"$wordToComplete*\" }} }}\n",
            "elvish" => $"set edit:completion:arg-completer[netwiv] = [@words]{{ put {Commands} }}\n",
            _ => throw new ArgumentException($"Unsupported shell '{shell}'.")
        };
        if (destination is null) await output.WriteAsync(script);
        else { var path = Path.GetFullPath(destination, workingDirectory); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, script, cancellationToken); }
        return 0;
    }
}
