using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class NewCommand
{
    private const string TemplateNotice = "\n---\n\nTAURI® is a trademark of The Tauri Programme within the Commons Conservancy.\n\nNetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.\n";
    private const string Usage = "Usage: netwiv new NAME [-t|--template TEMPLATE] [-m|--manager MANAGER] [-o|--output DIRECTORY] [--identifier ID] [--source NUGET_SOURCE]\n       netwiv new --list\nDefault template: react. Run `netwiv new --list` for all choices.";
    // TypeScript starters. React is the base template; the others replace its frontend.
    private static readonly string[] JavaScriptTemplates = ["react", "vue", "svelte", "angular"];

    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help", "-h")) { await output.WriteLineAsync(Usage); return 0; }
        if (arguments.TakeFlag("--list"))
        {
            arguments.EnsureEmpty();
            await output.WriteLineAsync("TypeScript: " + string.Join(", ", JavaScriptTemplates.Select(name => name == "react" ? "react (default)" : name)));
            await output.WriteLineAsync(".NET frontend: blazor, blazor-hybrid (managed desktop Hybrid; optional WebAssembly UI)");
            await output.WriteLineAsync("Managers: npm, pnpm, yarn, bun, deno (TypeScript); dotnet (Blazor)");
            return 0;
        }
        var template = arguments.TakeOption("--template", "-t") ?? "react";
        var dotnetFrontend = template is "blazor" or "blazor-hybrid";
        var manager = arguments.TakeOption("--manager", "-m") ?? (dotnetFrontend ? "dotnet" : "npm");
        var outputOption = arguments.TakeOption("--output", "-o");
        var identifierOption = arguments.TakeOption("--identifier");
        var sourceOption = arguments.TakeOption("--source");
        var name = arguments.TakePositional() ?? throw new ArgumentException("A project name is required. " + Usage);
        arguments.EnsureEmpty();
        if (!JavaScriptTemplates.Contains(template, StringComparer.Ordinal) && !dotnetFrontend)
            throw new ArgumentException($"Unknown template '{template}'. Run `netwiv new --list`.");
        if (dotnetFrontend ? manager != "dotnet" : manager is not ("npm" or "pnpm" or "yarn" or "bun" or "deno"))
            throw new ArgumentException($"Package manager '{manager}' is not supported for template '{template}'. Run `netwiv new --list`.");
        if (string.IsNullOrWhiteSpace(name) || !char.IsAsciiLetter(name[0]) || name.EndsWith('.') ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')) ||
            IsReservedName(name.Split('.')[0]))
            throw new ArgumentException("Project names must begin with an ASCII letter, contain only letters, digits, '.', '_' or '-', and be valid Windows/Linux file names.");
        var identifier = identifierOption ?? "dev.netwiv." + name.ToLowerInvariant().Replace('_', '-');
        if (string.IsNullOrEmpty(identifier) || identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-')))
            throw new ArgumentException("The application identifier may contain only ASCII letters, digits, '.' and '-'.");
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputOption ?? name, workingDirectory));
        EnsureEmptyDestination(destination);
        var source = sourceOption is null ? null : ResolveSource(sourceOption, workingDirectory);
        var templatePath = Path.Combine(AppContext.BaseDirectory, "templates", "react-app");
        if (!File.Exists(Path.Combine(templatePath, ".template.config", "template.json")))
            throw new DirectoryNotFoundException("The React template is missing from this NetWiv CLI installation.");
        token.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Cannot create a project at a filesystem root.");
        Directory.CreateDirectory(parent);
        // A sibling staging directory keeps the final move on the destination volume.
        var staging = Path.Combine(parent, ".netwiv-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var hive = Path.Combine(staging, "hive");
            var project = Path.Combine(staging, "app");
            if (template == "blazor-hybrid")
                await BlazorHybridTemplate.CreateAsync(project, name, CliApp.Version, token);
            else
            {
                await RunDotnet(["new", "install", templatePath, "--debug:custom-hive", hive], workingDirectory, token);
                await RunDotnet(["new", "netwiv-react", "--debug:custom-hive", hive, "--name", name, "--output", project,
                    "--NetWivVersion", CliApp.Version], workingDirectory, token);
                if (template == "blazor") await ApplyBlazorFrontendAsync(project, token);
                else if (template != "react") ApplyFrontend(project, template);
                ConfigureFrontend(project, template, manager);
            }
            var configPath = Path.Combine(project, "src-netwiv", "netwiv.conf.json");
            var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath, token))!.AsObject();
            var originalIdentifier = config["identifier"]!.GetValue<string>();
            config["productName"] = name;
            config["identifier"] = identifier;
            await File.WriteAllTextAsync(configPath, config.ToJsonString(), token);
            var programPath = Path.Combine(project, "src-netwiv", "Program.cs");
            var program = await File.ReadAllTextAsync(programPath, token);
            program = program.Replace("\"NetWiv React example\"", JsonValue.Create(name)!.ToJsonString(), StringComparison.Ordinal)
                .Replace(JsonValue.Create(originalIdentifier)!.ToJsonString(), JsonValue.Create(identifier)!.ToJsonString(), StringComparison.Ordinal)
                .Replace("\"NetWiv + React\"", JsonValue.Create(name)!.ToJsonString(), StringComparison.Ordinal);
            await File.WriteAllTextAsync(programPath, program, token);
            // The starter screen shows the app name as a literal; project names need no escaping.
            foreach (var screen in new[] { "src/App.tsx", "src/App.vue", "src/App.svelte", "src/app.ts", "frontend/Pages/Home.razor" }
                .Select(file => Path.Combine(project, file)).Where(File.Exists))
                await File.WriteAllTextAsync(screen, (await File.ReadAllTextAsync(screen, token)).Replace("NetWiv app", name, StringComparison.Ordinal), token);
            var npmName = name.ToLowerInvariant();
            foreach (var file in new[] { "package.json", "package-lock.json" }.Where(file => File.Exists(Path.Combine(project, file))))
            {
                var filePath = Path.Combine(project, file);
                var json = JsonNode.Parse(await File.ReadAllTextAsync(filePath, token))!.AsObject();
                json["name"] = npmName;
                if (file == "package-lock.json") json["packages"]![""]!["name"] = npmName;
                await File.WriteAllTextAsync(filePath, json.ToJsonString(), token);
            }
            if (source is not null)
            {
                var sources = new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "netwiv"), new XAttribute("value", source)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json")));
                await File.WriteAllTextAsync(Path.Combine(project, "NuGet.Config"), new XDocument(new XElement("configuration", sources)).ToString(), token);
            }
            token.ThrowIfCancellationRequested();
            EnsureEmptyDestination(destination);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: false);
            Directory.Move(project, destination);
        }
        finally
        {
            // Only remove the unique staging directory created by this invocation.
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { await output.WriteLineAsync($"Temporary template cache could not be removed: {staging}"); }
        }
        await output.WriteLineAsync($"Created {name} in {destination} ({template}, NetWiv {CliApp.Version}).");
        await output.WriteLineAsync("Next steps:");
        await output.WriteLineAsync($"  cd \"{destination}\"");
        if (source is null) await output.WriteLineAsync("  Add your NetWiv NuGet feed to NuGet.Config if these packages are not on NuGet.org.");
        await output.WriteLineAsync(manager switch
        {
            "npm" => "  npm ci\n  npm run desktop:dev",
            "dotnet" when template == "blazor-hybrid" => "  dotnet run --project src-netwiv",
            "dotnet" => "  dotnet tool restore\n  dotnet tool run netwiv -- dev -- --dev",
            _ => $"  {manager} install\n  {RunCommand(manager, "desktop:dev")}",
        });
        await output.WriteLineAsync($"IDE: open {name}.sln and run the {name} C# project.");
        await output.WriteLineAsync("Build: dotnet tool restore && dotnet tool run netwiv -- build --no-bundle (add --aot for NativeAOT)");
        return 0;
    }

    private static async Task RunDotnet(string[] args, string cwd, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec("dotnet", args, cwd, CaptureOutput: true), token);
        if (result.ExitCode != 0) throw new InvalidOperationException($"dotnet new failed (exit {result.ExitCode}): {result.StandardOutput.Trim()} {result.StandardError.Trim()}");
    }

    private static void ApplyFrontend(string project, string template)
    {
        var overlay = Path.Combine(AppContext.BaseDirectory, "templates", "frontends", template);
        if (!File.Exists(Path.Combine(overlay, "package.json"))) throw new DirectoryNotFoundException($"The {template} frontend template is missing from this NetWiv CLI installation.");
        ClearReactFrontend(project);
        foreach (var source in Directory.EnumerateFiles(overlay, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(project, Path.GetRelativePath(overlay, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        File.WriteAllText(Path.Combine(project, "README.md"), $"# NetWiv + {template}\n\nRun `npm ci` and `npm run desktop:dev`. Edit `src-netwiv/Program.cs` for C# commands and `src` for the frontend.\n" + TemplateNotice);
    }

    private static async Task ApplyBlazorFrontendAsync(string project, CancellationToken token)
    {
        ClearReactFrontend(project);
        var frontend = Path.Combine(project, "frontend");
        await RunDotnet(["new", "blazorwasm", "--no-restore", "--name", "NetWivUi", "--output", frontend], project, token);
        // Keep only the starter screen: drop the Blazor template's sample pages, navigation and Bootstrap.
        foreach (var sample in new[] { "Pages/Counter.razor", "Pages/Weather.razor", "Layout/NavMenu.razor", "Layout/NavMenu.razor.css", "Layout/MainLayout.razor.css" })
            File.Delete(Path.Combine(frontend, sample));
        // .NET 8 keeps Bootstrap in wwwroot/css/bootstrap; .NET 9 and later in wwwroot/lib.
        foreach (var sample in new[] { "wwwroot/sample-data", "wwwroot/lib", "wwwroot/css/bootstrap" })
            if (Directory.Exists(Path.Combine(frontend, sample))) Directory.Delete(Path.Combine(frontend, sample), recursive: true);
        // The overlay holds frontend/, contracts/ and src-netwiv/ files relative to the project root.
        var overlay = Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "blazor");
        foreach (var source in Directory.EnumerateFiles(overlay, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(project, Path.GetRelativePath(overlay, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        await UseTypedBlazorClientAsync(project, frontend, token);
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "shared", "vendor"), Path.Combine(frontend, "wwwroot", "vendor"));
        var indexPath = Path.Combine(frontend, "wwwroot", "index.html");
        // Bootstrap is gone, and with no scoped .razor.css files left there is no NetWivUi.styles.css bundle.
        var index = File.ReadAllText(indexPath).Replace("<base href=\"/\" />", "<base href=\"./\" />", StringComparison.Ordinal);
        index = string.Join('\n', index.Split('\n').Where(line => !line.Contains("bootstrap", StringComparison.OrdinalIgnoreCase) && !line.Contains("NetWivUi.styles.css", StringComparison.Ordinal)));
        File.WriteAllText(indexPath, index);
        Directory.Delete(Path.Combine(project, "scripts"), recursive: true);
        Directory.Delete(Path.Combine(project, "vendor"), recursive: true);
        File.WriteAllText(Path.Combine(project, "README.md"), "# NetWiv + Blazor\n\nThe frontend is a Blazor WebAssembly project targeting the selected .NET SDK in `frontend/`. Run `dotnet tool restore` then `dotnet tool run netwiv -- dev -- --dev`. The desktop backend is in `src-netwiv/`.\n\n" +
            "Types the app and the frontend share live in `contracts/`. Each app build generates `frontend/obj/netwiv/<Configuration>/NetWivCommands.g.cs`, a typed C# client for the `[Command]` methods whose parameters and results use those shared types; inject `NetWivCommands` and call, for example, `GreetAsync(name)`.\n" + TemplateNotice);
    }

    // Moves the starter's DTO and JSON context into contracts/, which the app and the frontend
    // both reference, and points the app's binding export at the frontend's typed client.
    private static async Task UseTypedBlazorClientAsync(string project, string frontend, CancellationToken token)
    {
        var programPath = Path.Combine(project, "src-netwiv", "Program.cs");
        var program = File.ReadAllText(programPath);
        foreach (var shared in new[]
        {
            "public sealed record Greeting(string Message, int Count);\n",
            "[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]\n[JsonSerializable(typeof(string))]\n[JsonSerializable(typeof(Greeting))]\npublic partial class AppJsonContext : JsonSerializerContext;\n",
        })
        {
            var normalized = program.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!normalized.Contains(shared, StringComparison.Ordinal)) throw new InvalidOperationException("The Blazor template could not move the starter types into contracts/.");
            program = normalized.Replace(shared, "", StringComparison.Ordinal);
        }
        File.WriteAllText(programPath, program.TrimEnd() + "\n");

        var backendPath = Directory.EnumerateFiles(Path.Combine(project, "src-netwiv"), "*.csproj").Single();
        var backend = XDocument.Load(backendPath);
        var bindings = backend.Descendants("NetWivFrontendBindingsDirectory").Single();
        bindings.ReplaceWith(new XElement("NetWivCSharpBindingsDirectory", "../frontend/obj/netwiv/$(Configuration)"));
        backend.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../contracts/Contracts.csproj"))));
        backend.Save(backendPath);

        var frontendProject = Path.Combine(frontend, "NetWivUi.csproj");
        var ui = XDocument.Load(frontendProject);
        ui.Root!.Add(new XElement("Import", new XAttribute("Project", "NetWiv.targets")));
        ui.Save(frontendProject);
        var startupPath = Path.Combine(frontend, "Program.cs");
        var startup = File.ReadAllText(startupPath);
        const string run = "await builder.Build().RunAsync();";
        if (!startup.Contains(run, StringComparison.Ordinal)) throw new InvalidOperationException("The Blazor template's Program.cs has an unexpected shape.");
        File.WriteAllText(startupPath, startup.Replace(run, "builder.Services.AddScoped<NetWiv.Generated.NetWivCommands>();\n\n" + run, StringComparison.Ordinal));

        var solution = Directory.EnumerateFiles(project, "*.sln").Single();
        await RunDotnet(["sln", solution, "add", Path.Combine(project, "contracts", "Contracts.csproj")], project, token);
    }

    private static void ClearReactFrontend(string project)
    {
        Directory.Delete(Path.Combine(project, "src"), recursive: true);
        foreach (var name in new[] { "index.html", "vite.config.js", "tsconfig.json", "package.json", "package-lock.json" }) File.Delete(Path.Combine(project, name));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void ConfigureFrontend(string project, string template, string manager)
    {
        if (manager == "npm") return;
        if (manager == "dotnet")
        {
            const string command = "dotnet watch --project frontend/NetWivUi.csproj run --urls http://127.0.0.1:5173";
            const string build = "dotnet publish frontend/NetWivUi.csproj -c \"$(Configuration)\" -o dist";
            var frontProject = Directory.EnumerateFiles(project, "*.Frontend.esproj").Single();
            var document = XDocument.Load(frontProject);
            document.Descendants("StartupCommand").Single().Value = command;
            document.Descendants("BuildCommand").Single().Value = build;
            document.Descendants("BuildOutputFolder").Single().Value = "$(MSBuildProjectDirectory)/dist/wwwroot";
            document.Descendants("Target").Single(target => (string?)target.Attribute("Name") == "RestoreFrontendDependencies").Remove();
            document.Descendants("Target").Single(target => (string?)target.Attribute("Name") == "BuildFrontendAssets").Attribute("DependsOnTargets")?.Remove();
            document.Save(frontProject);
            var appConfigPath = Path.Combine(project, "src-netwiv", "netwiv.conf.json");
            var appConfig = JsonNode.Parse(File.ReadAllText(appConfigPath))!.AsObject();
            appConfig["build"]!["beforeDevCommand"] = command;
            appConfig["build"]!["frontendDist"] = "../dist/wwwroot";
            var backendProject = Directory.EnumerateFiles(Path.Combine(project, "src-netwiv"), "*.csproj").Single();
            File.WriteAllText(backendProject, File.ReadAllText(backendProject).Replace("/../dist</NetWivFrontendDist>", "/../dist/wwwroot</NetWivFrontendDist>", StringComparison.Ordinal));
            File.WriteAllText(appConfigPath, appConfig.ToJsonString());
            return;
        }
        File.Delete(Path.Combine(project, "package-lock.json"));
        var manifestPath = Path.Combine(project, "package.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        if (manifest["scripts"] is JsonObject scripts)
        {
            if (scripts["predev"]?.GetValue<string>() == "npm run bindings") scripts["predev"] = "node scripts/generate-bindings.mjs";
            if (scripts["build"]?.GetValue<string>() is { } build && build.StartsWith("npm run bindings && ", StringComparison.Ordinal))
                scripts["build"] = "node scripts/generate-bindings.mjs && " + build["npm run bindings && ".Length..];
            if (manager == "deno")
            {
                foreach (var key in new[] { "bindings", "predev", "dev", "build" })
                    if (scripts[key] is JsonValue value && value.TryGetValue<string>(out var script))
                        scripts[key] = script.Replace("node scripts/generate-bindings.mjs", "deno run -A scripts/generate-bindings.mjs", StringComparison.Ordinal);
                if (scripts["dev"]?.GetValue<string>() is { } dev && !dev.Contains("generate-bindings.mjs", StringComparison.Ordinal))
                    scripts["dev"] = "deno run -A scripts/generate-bindings.mjs && " + dev;
            }
        }
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        if (manager == "yarn")
        {
            File.WriteAllText(Path.Combine(project, "yarn.lock"), string.Empty);
            File.WriteAllText(Path.Combine(project, ".yarnrc.yml"), "nodeLinker: node-modules\n");
        }
        if (manager == "deno") File.WriteAllText(Path.Combine(project, "deno.json"), "{\"nodeModulesDir\":\"manual\"}\n");
        var frontendProject = Directory.EnumerateFiles(project, "*.Frontend.esproj").Single();
        var xml = File.ReadAllText(frontendProject);
        xml = xml.Replace("npm run dev", RunCommand(manager, "dev"), StringComparison.Ordinal)
            .Replace("npm run build", RunCommand(manager, "build"), StringComparison.Ordinal)
            .Replace("npm ci --no-audit --no-fund", manager + " install", StringComparison.Ordinal)
            .Replace("$(MSBuildProjectDirectory)/package.json;$(MSBuildProjectDirectory)/package-lock.json", "$(MSBuildProjectDirectory)/package.json", StringComparison.Ordinal);
        File.WriteAllText(frontendProject, xml);
        var configPath = Path.Combine(project, "src-netwiv", "netwiv.conf.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        config["build"]!["beforeDevCommand"] = RunCommand(manager, "dev");
        File.WriteAllText(configPath, config.ToJsonString());
        var readmePath = Path.Combine(project, "README.md");
        File.WriteAllText(readmePath, File.ReadAllText(readmePath)
            .Replace("npm ci", manager + " install", StringComparison.Ordinal)
            .Replace("npm run desktop:dev", RunCommand(manager, "desktop:dev"), StringComparison.Ordinal));
    }

    private static string RunCommand(string manager, string script) => manager == "deno" ? $"deno task {script}" : $"{manager} run {script}";

    private static void EnsureEmptyDestination(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) &&
            ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(path).Any()))
            throw new IOException($"Refusing to overwrite '{path}'. Choose a new or empty directory.");
    }

    private static string ResolveSource(string value, string cwd)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--source cannot be empty.");
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") return uri.AbsoluteUri;
        var path = Path.GetFullPath(value, cwd);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"NuGet source directory does not exist: {path}");
        return path;
    }

    private static bool IsReservedName(string value) => value.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" ||
        value.Length == 4 && value[3] is >= '1' and <= '9' &&
            (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
}
