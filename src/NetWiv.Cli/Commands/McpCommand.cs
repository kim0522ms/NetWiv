using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Frontend;

namespace NetWiv.Cli;

/// <summary>
/// `netwiv mcp`: a local Model Context Protocol server over stdio for coding agents. It exposes
/// read-only tools: the guides shipped with this CLI version and facts about the current
/// project. It never builds, runs or edits anything; agents use the CLI for that.
/// </summary>
internal static class McpCommand
{
    private const int MaxText = 60_000;

    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help", "-h"))
        {
            await output.WriteLineAsync("""
                Usage: netwiv mcp

                Runs a Model Context Protocol server on stdin/stdout for coding agents.
                Register it in the agent, for example in .mcp.json:
                  { "mcpServers": { "netwiv": { "command": "dotnet", "args": ["tool", "run", "netwiv", "--", "mcp"] } } }
                Tools are read-only: NetWiv guides for this CLI version and the current project's configuration,
                capabilities and generated commands.
                """);
            return 0;
        }
        arguments.EnsureEmpty();
        // stdout carries protocol messages only; nothing else may write to it from here on.
        await using var server = McpServer.Create(new StdioServerTransport("netwiv"), CreateOptions(workingDirectory, McpDocs.Load(DocsDirectory)));
        await server.RunAsync(token);
        return 0;
    }

    internal static string DocsDirectory => Path.Combine(AppContext.BaseDirectory, "docs");

    internal static McpServerOptions CreateOptions(string workingDirectory, McpDocs docs) => new()
    {
        ServerInfo = new Implementation { Name = "netwiv", Version = CliApp.Version },
        ServerInstructions =
            $"NetWiv {CliApp.Version}: a .NET desktop framework with C# commands and a web frontend on the OS WebView. " +
            "Search the bundled guides (netwiv_docs_search, then netwiv_docs_read) before answering NetWiv API, configuration " +
            "or permission questions; they match this CLI version. Use netwiv_project_info first in a project to see its " +
            "configuration and NetWiv version. These tools only read; build, run and edit with the netwiv CLI and the files themselves.",
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = Tools }),
            CallToolHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var name = request.Params?.Name ?? "";
                var args = request.Params?.Arguments;
                try
                {
                    var text = name switch
                    {
                        "netwiv_docs_search" => DocsSearch(docs, args),
                        "netwiv_docs_read" => DocsRead(docs, args),
                        "netwiv_project_info" => ProjectInfo(Directory(workingDirectory, args)),
                        "netwiv_capabilities" => Capabilities(Directory(workingDirectory, args)),
                        "netwiv_commands" => Commands(Directory(workingDirectory, args)),
                        _ => throw new ArgumentException($"Unknown tool '{name}'.")
                    };
                    return ValueTask.FromResult(Result(text, false));
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or NetWivConfigException or BadImageFormatException or NotSupportedException or JsonException or System.Xml.XmlException)
                {
                    return ValueTask.FromResult(Result(exception.Message, true));
                }
            }
        }
    };

    private static readonly Tool[] Tools =
    [
        Tool("netwiv_docs_search",
            "Search the NetWiv guides shipped with this CLI version. Returns matching sections with the guide name and heading to pass to netwiv_docs_read.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Words to look for, such as 'capability permission' or 'NativeAOT Blazor Hybrid'."},"limit":{"type":"integer","minimum":1,"maximum":20,"description":"Maximum sections to return (default 8)."}},"required":["query"]}"""),
        Tool("netwiv_docs_read",
            "Read a NetWiv guide, or one section of it with its subsections. Call without a known name to get the list of guides.",
            """{"type":"object","properties":{"name":{"type":"string","description":"Guide name, such as 'plugin-configuration' or 'blazor-hybrid'. Omit to list guides."},"section":{"type":"string","description":"Optional heading text of the section to read."}}}"""),
        Tool("netwiv_project_info",
            "Describe the NetWiv project in the working directory: CLI version, configuration file, identifiers, application project, NetWiv package version and frontend settings.",
            DirectorySchema),
        Tool("netwiv_capabilities",
            "Show the project's capability files, inline capabilities and permission files, which decide which commands each window may call.",
            DirectorySchema),
        Tool("netwiv_commands",
            "List the C# commands of the last built application with their argument and result types, read from the built assembly without running it. Build the app first.",
            DirectorySchema),
    ];

    private const string DirectorySchema =
        """{"type":"object","properties":{"directory":{"type":"string","description":"Project directory, relative to the server's working directory. Defaults to the working directory."}}}""";

    private static Tool Tool(string name, string description, string schema) => new()
    {
        Name = name,
        Description = description,
        InputSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false },
    };

    private static CallToolResult Result(string text, bool error) => new()
    {
        Content = [new TextContentBlock { Text = text.Length > MaxText ? text[..MaxText] + "\n\n[Truncated. Narrow the request.]" : text }],
        IsError = error,
    };

    private static string? Argument(IDictionary<string, JsonElement>? args, string name)
    {
        if (args is null || !args.TryGetValue(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()
            : throw new ArgumentException($"{name} must be a string.");
    }

    private static string Directory(string workingDirectory, IDictionary<string, JsonElement>? args)
    {
        var path = Path.GetFullPath(Argument(args, "directory") ?? ".", workingDirectory);
        if (!System.IO.Directory.Exists(path)) throw new ArgumentException($"Directory does not exist: {path}");
        return path;
    }

    private static string DocsSearch(McpDocs docs, IDictionary<string, JsonElement>? args)
    {
        var query = Argument(args, "query") ?? throw new ArgumentException("query is required.");
        var limit = args is not null && args.TryGetValue("limit", out var value) && value.TryGetInt32(out var number) ? Math.Clamp(number, 1, 20) : 8;
        if (docs.Names.Count == 0) throw new InvalidOperationException("This CLI build has no bundled guides.");
        var hits = docs.Search(query, limit);
        if (hits.Count == 0) return $"No guide sections match '{query}'. Try other words, or netwiv_docs_read with one of: {string.Join(", ", docs.Names.Order(StringComparer.Ordinal))}.";
        var builder = new StringBuilder($"Guides for NetWiv {CliApp.Version}. Read a section with netwiv_docs_read {{ name, section }}.\n");
        foreach (var (section, snippet) in hits)
            builder.Append("\n- ").Append(section.Document).Append(section.Level > 0 ? " › " + section.Heading : "").Append('\n').Append("  ").Append(snippet).Append('\n');
        return builder.ToString();
    }

    private static string DocsRead(McpDocs docs, IDictionary<string, JsonElement>? args)
    {
        if (docs.Names.Count == 0) throw new InvalidOperationException("This CLI build has no bundled guides.");
        var name = Argument(args, "name");
        if (string.IsNullOrWhiteSpace(name))
            return "Available guides: " + string.Join(", ", docs.Names.Order(StringComparer.Ordinal));
        return docs.Read(name, Argument(args, "section"), out var error) ?? throw new ArgumentException(error);
    }

    private static string ProjectInfo(string directory)
    {
        var builder = new StringBuilder();
        builder.Append("NetWiv CLI: ").Append(CliApp.Version).Append('\n');
        var config = ConfigLoader.Load(directory, BuildTarget.Resolve(null).Platform);
        builder.Append("Config: ").Append(config.BasePath).Append('\n');
        foreach (var source in config.Sources.Skip(1)) builder.Append("Merged: ").Append(source).Append('\n');
        foreach (var key in new[] { "productName", "version", "identifier", "mainBinaryName" })
            if (config.String(key) is { } value) builder.Append(key).Append(": ").Append(value).Append('\n');
        foreach (var key in new[] { "frontendDist", "devUrl", "beforeDevCommand", "beforeBuildCommand" })
            if (config.Node("build", key) is { } value) builder.Append("build.").Append(key).Append(": ").Append(value.ToJsonString()).Append('\n');
        try
        {
            var project = CommandConfig.Project(config);
            builder.Append("Application project: ").Append(project).Append('\n');
            var packages = NetWivPackages(project);
            foreach (var (package, version) in packages) builder.Append("Package ").Append(package).Append(": ").Append(version).Append('\n');
            var versions = packages.Select(static item => item.Version).Where(static v => !string.IsNullOrWhiteSpace(v) && !v.Contains('$')).Distinct().ToArray();
            if (versions.Length == 1 && versions[0] != CliApp.Version)
                builder.Append("Note: the project uses NetWiv ").Append(versions[0]).Append(" but this CLI is ").Append(CliApp.Version)
                    .Append(". Run the project's own CLI (`dotnet tool run netwiv -- mcp`) so the guides match.\n");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NetWivConfigException or System.Xml.XmlException)
        {
            builder.Append("Application project: not resolved (").Append(exception.Message).Append(")\n");
        }
        return builder.ToString();
    }

    // NetWiv package references with their versions, resolving a NetWivVersion property when used.
    private static List<(string Package, string Version)> NetWivPackages(string project)
    {
        var document = XDocument.Load(project);
        var property = document.Descendants("NetWivVersion").Select(static element => element.Value.Trim()).FirstOrDefault();
        return document.Descendants("PackageReference")
            .Select(static element => ((string?)element.Attribute("Include") ?? "", (string?)element.Attribute("Version") ?? element.Element("Version")?.Value ?? ""))
            .Where(static item => item.Item1.StartsWith("NetWiv.", StringComparison.Ordinal))
            .Select(item => (item.Item1, item.Item2 == "$(NetWivVersion)" && property is not null ? property : item.Item2))
            .ToList();
    }

    private static string Capabilities(string directory)
    {
        var config = ConfigLoader.Load(directory, BuildTarget.Resolve(null).Platform);
        var builder = new StringBuilder("Capabilities decide which commands each window or webview may call. ")
            .Append("See netwiv_docs_search \"capability permission\" for the rules.\n");
        if (config.Node("app", "security", "capabilities") is { } inline)
            builder.Append("\napp.security.capabilities in ").Append(Path.GetFileName(config.BasePath)).Append(":\n").Append(Indented(inline)).Append('\n');
        AppendFiles(builder, Path.Combine(config.ConfigDirectory, "capabilities"), "Capability files", recursive: false);
        AppendFiles(builder, Path.Combine(config.ConfigDirectory, "permissions"), "Application permission files", recursive: true);
        return builder.ToString();
    }

    private static void AppendFiles(StringBuilder builder, string directory, string title, bool recursive)
    {
        var files = System.IO.Directory.Exists(directory)
            ? System.IO.Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = recursive, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
                .Where(static path => Path.GetExtension(path).ToLowerInvariant() is ".json" or ".json5" or ".toml").Order(StringComparer.Ordinal).ToArray()
            : [];
        builder.Append('\n').Append(title).Append(" (").Append(directory).Append("): ").Append(files.Length == 0 ? "none\n" : "\n");
        foreach (var file in files)
        {
            builder.Append("\n### ").Append(Path.GetRelativePath(directory, file)).Append('\n');
            builder.Append(File.ReadAllText(file).TrimEnd()).Append('\n');
        }
    }

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
    private static string Indented(JsonNode node) => node.ToJsonString(IndentedOptions);

    private static string Commands(string directory)
    {
        var config = ConfigLoader.Load(directory, BuildTarget.Resolve(null).Platform);
        var project = CommandConfig.Project(config);
        var assembly = BuiltAssembly(project, config.ProjectDirectory, config.String("mainBinaryName"))
            ?? throw new InvalidOperationException($"No built assembly found for {Path.GetFileName(project)}. Build the app first (for example `dotnet build {Path.GetFileName(project)}`).");
        FrontendBindings bindings;
        try { bindings = FrontendBindings.Read(assembly); }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{exception.Message} (read {assembly})");
        }
        var builder = new StringBuilder("Commands read from ").Append(assembly).Append(" (")
            .Append(File.GetLastWriteTimeUtc(assembly).ToString("u", System.Globalization.CultureInfo.InvariantCulture)).Append("). Rebuild after changing commands.\n");
        if (bindings.HasScript)
            builder.Append("\nTypeScript declarations (commands.d.ts):\n```ts\n").Append(bindings.TypeScript.TrimEnd()).Append("\n```\n");
        if (bindings.CSharp is { } client)
            builder.Append("\nTyped C# client for .NET frontends (NetWivCommands.g.cs):\n```csharp\n").Append(client.TrimEnd()).Append("\n```\n");
        return builder.ToString();
    }

    // The newest managed build of the application: bin/ of the project, then the CLI's target/ outputs.
    private static string? BuiltAssembly(string project, string projectRoot, string? mainBinaryName)
    {
        var document = XDocument.Load(project);
        // Conditions are not evaluated: retain every literal override and the SDK default.
        var names = document.Descendants("AssemblyName").Select(static element => element.Value.Trim())
            .Where(static value => value.Length > 0 && !value.Contains('$'))
            .Append(Path.GetFileNameWithoutExtension(project)).Append(mainBinaryName)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var projectDirectory = Path.GetDirectoryName(project)!;
        var roots = new[] { Path.Combine(projectDirectory, "bin"), Path.Combine(projectRoot, "target") };
        return roots.Where(System.IO.Directory.Exists)
            .SelectMany(root => names.SelectMany(candidate => System.IO.Directory.EnumerateFiles(root, candidate + ".dll", SearchOption.AllDirectories)))
            // Reference assemblies and intermediate copies are not the application build.
            .Where(static path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static part => part is "obj" or "ref" or "refint"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
