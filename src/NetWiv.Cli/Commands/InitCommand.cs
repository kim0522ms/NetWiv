using System.Text.Json.Nodes;

namespace NetWiv.Cli;

internal static class InitCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        if (args.Contains("--help")) { await output.WriteLineAsync("Usage: netwiv init [--ci] [--force] [-d DIRECTORY] [-A APP_NAME] [-W WINDOW_TITLE] [-D FRONTEND_DIST] [-P DEV_URL] [--netwiv-source PATH]"); return 0; }
        var arguments = new CommandArguments(args);
        var force = arguments.TakeFlag("--force", "-f");
        arguments.TakeFlag("--ci");
        var destination = Path.GetFullPath(arguments.TakeOption("--directory", "-d") ?? workingDirectory);
        var appName = arguments.TakeOption("--app-name", "-A") ?? new DirectoryInfo(destination).Name;
        var title = arguments.TakeOption("--window-title", "-W") ?? appName;
        var frontendDist = arguments.TakeOption("--frontend-dist", "-D") ?? "../dist";
        var devUrl = arguments.TakeOption("--dev-url", "-P");
        var beforeDev = arguments.TakeOption("--before-dev-command");
        var beforeBuild = arguments.TakeOption("--before-build-command");
        var sourceOption = arguments.TakeOption("--netwiv-source");
        var sourceRoot = sourceOption is null ? null : Path.GetFullPath(sourceOption, workingDirectory);
        var tauriPath = arguments.TakeOption("--tauri-path", "-t");
        var verbose = arguments.TakeFlag("--verbose", "-v");
        var log = arguments.TakeFlag("--log", "-l");
        arguments.EnsureEmpty();
        if (tauriPath is not null) throw new NotSupportedException("--tauri-path selects Rust crates and has no .NET project equivalent; use --netwiv-source for local NetWiv projects.");
        if (log) throw new NotSupportedException("--log configures Rust tracing and has no .NET initialization equivalent.");
        ValidateName(appName, "application name");
        if (sourceRoot is not null && !File.Exists(Path.Combine(sourceRoot, "src", "NetWiv.Platform.Wry", "NetWiv.Platform.Wry.csproj")))
            throw new DirectoryNotFoundException($"--netwiv-source is not a NetWiv source checkout: {sourceRoot}");

        var target = Path.Combine(destination, "src-netwiv");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (!force) throw new IOException($"'{target}' already exists and is not empty. Pass --force to replace NetWiv-owned template files.");
        }
        Directory.CreateDirectory(target);
        var template = FindTemplateDirectory();
        var references = sourceRoot is null
            ? """
                  <PackageReference Include="NetWiv.Platform.Wry" Version="$(NetWivVersion)" />
                  <PackageReference Include="NetWiv.Build" Version="$(NetWivVersion)" PrivateAssets="all" />
                  <PackageReference Include="NetWiv.Generators" Version="$(NetWivVersion)" PrivateAssets="all" />
              """
            : string.Join(Environment.NewLine, new[] { "NetWiv.Core", "NetWiv.Platform.Wry", "NetWiv.Build", "NetWiv.Generators" }.Select(name =>
                $"    <ProjectReference Include=\"{XmlEscape(Relative(target, Path.Combine(sourceRoot, "src", name, name + ".csproj")))}\""
                + (name == "NetWiv.Generators" ? " OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\"" : name == "NetWiv.Build" ? " ReferenceOutputAssembly=\"false\"" : "") + " />"));
        var buildImport = sourceRoot is null ? string.Empty
            : $"  <Import Project=\"{XmlEscape(Relative(target, Path.Combine(sourceRoot, "src", "NetWiv.Build", "buildTransitive", "NetWiv.Build.targets")))}\" />";
        var replacements = new Dictionary<string, string>
        {
            ["__APP_NAME__"] = JsonEscape(appName), ["__WINDOW_TITLE__"] = JsonEscape(title),
            ["__IDENTIFIER__"] = Identifier(appName), ["__DEV_URL__"] = JsonLiteral(devUrl),
            ["__FRONTEND_DIST__"] = JsonLiteral(frontendDist), ["__BEFORE_DEV_COMMAND__"] = JsonLiteral(beforeDev),
            ["__BEFORE_BUILD_COMMAND__"] = JsonLiteral(beforeBuild), ["__NETWIV_VERSION__"] = CliApp.Version,
            ["__NETWIV_REFERENCES__"] = references, ["__NETWIV_BUILD_IMPORT__"] = buildImport
        };
        foreach (var file in Directory.EnumerateFiles(template).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationFile = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(destinationFile) && !force) throw new IOException($"Refusing to overwrite {destinationFile}.");
            var contents = File.ReadAllText(file);
            foreach (var replacement in replacements) contents = contents.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            File.WriteAllText(destinationFile, contents);
        }
        await output.WriteLineAsync($"Initialized NetWiv application in {target}");
        if (verbose) await output.WriteLineAsync($"Configuration: {Path.Combine(target, "netwiv.conf.json")}");
        return 0;
    }

    private static string FindTemplateDirectory()
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "templates", "app"), Path.Combine(Directory.GetCurrentDirectory(), "templates", "app"), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "templates", "app")) };
        return candidates.FirstOrDefault(Directory.Exists) ?? throw new DirectoryNotFoundException("NetWiv app templates were not installed with the CLI.");
    }
    private static string Relative(string from, string to) => Path.GetRelativePath(from, to).Replace('/', '\\');
    private static string Identifier(string value) => string.Concat(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
    private static string JsonLiteral(string? value) => value is null ? "null" : JsonValue.Create(value)!.ToJsonString();
    private static string JsonEscape(string value) => JsonLiteral(value)[1..^1];
    private static string XmlEscape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static void ValidateName(string value, string label) { if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException($"Invalid {label}: '{value}'."); }
}
