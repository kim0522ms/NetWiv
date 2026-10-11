using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class PluginCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help") || args.Length == 0) { await output.WriteLineAsync("Usage: netwiv plugin <new|init> [PLUGIN_NAME] [OPTIONS]"); return 0; }
        return arguments.TakePositional() switch
        {
            "new" => await Scaffold(arguments, output, workingDirectory, createDirectory: true, token),
            "init" => await Scaffold(arguments, output, workingDirectory, createDirectory: false, token),
            "android" or "ios" => throw new NotSupportedException("Mobile plugin projects are outside the Windows/Linux milestone."),
            var command => throw new ArgumentException($"Unknown plugin command '{command}'.")
        };
    }

    public static Task<int> AddAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token) => ChangeReference(args, output, workingDirectory, add: true, token);
    public static Task<int> RemoveAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token) => ChangeReference(args, output, workingDirectory, add: false, token);

    private static async Task<int> Scaffold(CommandArguments args, TextWriter output, string cwd, bool createDirectory, CancellationToken token)
    {
        var name = args.TakePositional();
        var directoryOption = args.TakeOption("--directory", "-d");
        var author = args.TakeOption("--author", "-a") ?? Environment.UserName;
        var sourceRoot = args.TakeOption("--netwiv-source");
        var noApi = args.TakeFlag("--no-api");
        var noExample = args.TakeFlag("--no-example");
        var githubWorkflows = args.TakeFlag("--github-workflows");
        if (args.TakeFlag("--android") || args.TakeFlag("--ios") || args.TakeFlag("--mobile") || args.TakeOption("--ios-framework") is not null)
            throw new NotSupportedException("Mobile plugin scaffolding is outside the Windows/Linux milestone.");
        var tauriPath = args.TakeOption("--tauri-path", "-t"); args.EnsureEmpty();
        if (githubWorkflows) throw new NotSupportedException("--github-workflows is not implemented for .NET plugin scaffolds.");
        if (tauriPath is not null) throw new NotSupportedException("--tauri-path selects Rust crates and has no .NET plugin equivalent; use --netwiv-source.");
        var directory = Path.GetFullPath(directoryOption ?? (createDirectory && name is not null ? $"netwiv-plugin-{name}" : cwd), cwd);
        name ??= new DirectoryInfo(directory).Name.Replace("netwiv-plugin-", string.Empty, StringComparison.OrdinalIgnoreCase);
        PermissionCommand.ValidateId(name);
        if (createDirectory && Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException($"Plugin directory is not empty: {directory}");
        Directory.CreateDirectory(directory);
        var pascal = string.Concat(name.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        var packageSuffix = pascal;
        if (char.IsDigit(pascal[0])) pascal = "_" + pascal;
        static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
        string references;
        if (sourceRoot is null)
            references = $"<PackageReference Include=\"NetWiv.Core\" Version=\"{Xml(CliApp.Version)}\" />\n    <PackageReference Include=\"NetWiv.Generators\" Version=\"{Xml(CliApp.Version)}\" PrivateAssets=\"all\" />";
        else
        {
            sourceRoot = Path.GetFullPath(sourceRoot, cwd);
            var core = Path.Combine(sourceRoot, "src", "NetWiv.Core", "NetWiv.Core.csproj");
            var generators = Path.Combine(sourceRoot, "src", "NetWiv.Generators", "NetWiv.Generators.csproj");
            foreach (var project in new[] { core, generators })
                if (!File.Exists(project)) throw new FileNotFoundException("--netwiv-source does not contain the required NetWiv project.", project);
            references = $"<ProjectReference Include=\"{Xml(Path.GetRelativePath(directory, core))}\" />\n    <ProjectReference Include=\"{Xml(Path.GetRelativePath(directory, generators))}\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />";
        }
        var template = FindTemplateDirectory();
        var files = Directory.EnumerateFiles(template, "*", SearchOption.AllDirectories)
            .Select(source => (Source: source, Destination: Path.Combine(directory, Path.GetRelativePath(template, source)))).ToArray();
        foreach (var file in files)
            if (File.Exists(file.Destination)) throw new IOException($"Refusing to overwrite {file.Destination}.");
        foreach (var (source, destination) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var text = File.ReadAllText(source).Replace("__PLUGIN_NAME__", name, StringComparison.Ordinal).Replace("__PLUGIN_PASCAL__", pascal, StringComparison.Ordinal).Replace("__PLUGIN_PACKAGE__", packageSuffix, StringComparison.Ordinal).Replace("__AUTHOR__", Xml(author), StringComparison.Ordinal).Replace("__NETWIV_REFERENCES__", references, StringComparison.Ordinal);
            await File.WriteAllTextAsync(destination, text, token);
        }
        await output.WriteLineAsync($"Initialized .NET NetWiv plugin in {directory}");
        if (noApi || noExample) await output.WriteLineAsync("The desktop .NET plugin scaffold contains no frontend API or example project.");
        return 0;
    }

    private static async Task<int> ChangeReference(string[] input, TextWriter output, string cwd, bool add, CancellationToken token)
    {
        var args = new CommandArguments(input); var plugin = args.TakePositional() ?? throw new ArgumentException("Plugin name or project path is required.");
        if (args.TakeOption("--tag", "-t") is not null || args.TakeOption("--rev", "-r") is not null || args.TakeOption("--branch", "-b") is not null)
            throw new NotSupportedException("Git tag/revision/branch plugin dependencies are Cargo-specific. Use a local .csproj path or a published NuGet version.");
        args.TakeFlag("--no-fmt"); var version = args.TakeOption("--version"); args.EnsureEmpty();
        var config = ConfigLoader.Load(cwd, BuildTarget.Resolve(null).Platform); var project = CommandConfig.Project(config);
        var local = File.Exists(Path.GetFullPath(plugin, cwd)) && Path.GetExtension(plugin).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        var command = new List<string> { add ? "add" : "remove", project, local ? "reference" : "package", local ? Path.GetFullPath(plugin, cwd) : PackageName(plugin) };
        if (add && !local && version is not null) command.AddRange(["--version", version]);
        var result = await ProcessRunner.RunAsync(new ProcessSpec("dotnet", command, config.ProjectDirectory, CaptureOutput: true), token);
        if (result.ExitCode != 0) throw new InvalidOperationException($"dotnet {command[0]} failed: {(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim()}");
        await output.WriteLineAsync($"{(add ? "Added" : "Removed")} plugin {plugin}"); return 0;
    }

    private static string PackageName(string plugin) => plugin.StartsWith("NetWiv.Plugin.", StringComparison.Ordinal) ? plugin : "NetWiv.Plugin." + string.Concat(plugin.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private static string FindTemplateDirectory() { var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "templates", "plugin"), Path.Combine(Directory.GetCurrentDirectory(), "templates", "plugin"), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "templates", "plugin")) }; return candidates.FirstOrDefault(Directory.Exists) ?? throw new DirectoryNotFoundException("NetWiv plugin templates were not installed with the CLI."); }
}
