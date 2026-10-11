using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class NewCommand
{
    private const string TemplateNotice = "\n---\n\nTAURI® is a trademark of The Tauri Programme within the Commons Conservancy.\n\nNetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.\n";
    private const string Usage = "Usage: netwiv new NAME [-t|--template TEMPLATE] [-m|--manager MANAGER] [-o|--output DIRECTORY] [--identifier ID] [--source NUGET_SOURCE]\n       netwiv new --list\nDefault template: react. Run `netwiv new --list` for all choices.";
    private static readonly string[] JavaScriptTemplates = ["vanilla", "vanilla-ts", "vue", "vue-ts", "svelte", "svelte-ts", "react", "react-ts", "solid", "solid-ts", "angular", "preact", "preact-ts"];
    private static readonly string[] RustTemplates = ["yew", "leptos", "sycamore", "dioxus"];

    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help", "-h")) { await output.WriteLineAsync(Usage); return 0; }
        if (arguments.TakeFlag("--list"))
        {
            arguments.EnsureEmpty();
            await output.WriteLineAsync("JavaScript/TypeScript: " + string.Join(", ", JavaScriptTemplates.Select(name => name == "react" ? "react (default)" : name)));
            await output.WriteLineAsync("Rust/WASM frontends: " + string.Join(", ", RustTemplates));
            await output.WriteLineAsync(".NET frontend: blazor");
            await output.WriteLineAsync("Managers: npm, pnpm, yarn, bun, deno (JS/TS); cargo (Rust/WASM); dotnet (Blazor)");
            return 0;
        }
        var template = arguments.TakeOption("--template", "-t") ?? "react";
        var manager = arguments.TakeOption("--manager", "-m") ?? (RustTemplates.Contains(template, StringComparer.Ordinal) ? "cargo" : template == "blazor" ? "dotnet" : "npm");
        var outputOption = arguments.TakeOption("--output", "-o");
        var identifierOption = arguments.TakeOption("--identifier");
        var sourceOption = arguments.TakeOption("--source");
        var name = arguments.TakePositional() ?? throw new ArgumentException("A project name is required. " + Usage);
        arguments.EnsureEmpty();
        if (!JavaScriptTemplates.Contains(template, StringComparer.Ordinal) && !RustTemplates.Contains(template, StringComparer.Ordinal) && template != "blazor")
            throw new ArgumentException($"Unknown template '{template}'. Run `netwiv new --list`.");
        if (RustTemplates.Contains(template, StringComparer.Ordinal) ? manager != "cargo" : template == "blazor" ? manager != "dotnet" : manager is not ("npm" or "pnpm" or "yarn" or "bun" or "deno"))
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
            await RunDotnet(["new", "install", templatePath, "--debug:custom-hive", hive], workingDirectory, token);
            await RunDotnet(["new", "netwiv-react", "--debug:custom-hive", hive, "--name", name, "--output", project,
                "--NetWivVersion", CliApp.Version], workingDirectory, token);
            if (template == "blazor") await ApplyBlazorFrontendAsync(project, token);
            else if (template != "react") ApplyFrontend(project, template);
            ConfigureFrontend(project, template, manager);
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
            "cargo" => "  rustup target add wasm32-unknown-unknown\n  cargo install trunk --locked\n  dotnet tool restore\n  dotnet tool run netwiv -- dev -- --dev",
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
        var rust = RustTemplates.Contains(template, StringComparer.Ordinal);
        if (!File.Exists(Path.Combine(overlay, rust ? "Cargo.toml" : "package.json"))) throw new DirectoryNotFoundException($"The {template} frontend template is missing from this NetWiv CLI installation.");
        ClearReactFrontend(project);
        foreach (var source in Directory.EnumerateFiles(overlay, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(project, Path.GetRelativePath(overlay, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        if (rust)
        {
            var shared = Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "shared");
            foreach (var name in new[] { "index.html", "Trunk.toml" }) File.Copy(Path.Combine(shared, "rust", name), Path.Combine(project, name));
            File.Copy(Path.Combine(shared, "rust", "netwiv.rs"), Path.Combine(project, "src", "netwiv.rs"));
            CopyDirectory(Path.Combine(shared, "vendor"), Path.Combine(project, "vendor"));
            File.Delete(Path.Combine(project, "scripts", "generate-bindings.mjs"));
        }
        File.WriteAllText(Path.Combine(project, "README.md"), (rust
            ? $"# NetWiv + {template}\n\nThe C# backend needs no Cargo. This optional Rust/WASM frontend needs `rustup target add wasm32-unknown-unknown` and `cargo install trunk --locked`. Run `dotnet tool restore` then `dotnet tool run netwiv -- dev -- --dev`.\n"
            : $"# NetWiv + {template}\n\nRun `npm ci` and `npm run desktop:dev`. Edit `src-netwiv/Program.cs` for C# commands and `src` for the frontend.\n") + TemplateNotice);
    }

    private static async Task ApplyBlazorFrontendAsync(string project, CancellationToken token)
    {
        ClearReactFrontend(project);
        var frontend = Path.Combine(project, "frontend");
        await RunDotnet(["new", "blazorwasm", "--no-restore", "--name", "NetWivUi", "--output", frontend], project, token);
        var overlay = Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "blazor");
        File.Copy(Path.Combine(overlay, "Pages", "Home.razor"), Path.Combine(frontend, "Pages", "Home.razor"), overwrite: true);
        File.Copy(Path.Combine(overlay, "netwiv.js"), Path.Combine(frontend, "wwwroot", "netwiv.js"));
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "shared", "vendor"), Path.Combine(frontend, "wwwroot", "vendor"));
        var indexPath = Path.Combine(frontend, "wwwroot", "index.html");
        var index = File.ReadAllText(indexPath).Replace("<base href=\"/\" />", "<base href=\"./\" />", StringComparison.Ordinal)
            .Replace("</body>", "<script type=\"module\" src=\"netwiv.js\"></script>\n</body>", StringComparison.Ordinal);
        File.WriteAllText(indexPath, index);
        File.Delete(Path.Combine(project, "scripts", "generate-bindings.mjs"));
        File.WriteAllText(Path.Combine(project, "README.md"), "# NetWiv + Blazor\n\nThe frontend is a Blazor WebAssembly project targeting the selected .NET SDK in `frontend/`. Run `dotnet tool restore` then `dotnet tool run netwiv -- dev -- --dev`. The desktop backend is in `src-netwiv/`.\n" + TemplateNotice);
    }

    private static void ClearReactFrontend(string project)
    {
        Directory.Delete(Path.Combine(project, "src"), recursive: true);
        foreach (var name in new[] { "index.html", "vite.config.js", "package.json", "package-lock.json" }) File.Delete(Path.Combine(project, name));
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
        if (manager is "cargo" or "dotnet")
        {
            var command = manager == "cargo" ? "trunk serve --address 127.0.0.1 --port 5173" : "dotnet watch --project frontend/NetWivUi.csproj run --urls http://127.0.0.1:5173";
            var build = manager == "cargo" ? "trunk build --release" : "dotnet publish frontend/NetWivUi.csproj -c Release -o dist";
            var frontProject = Directory.EnumerateFiles(project, "*.Frontend.esproj").Single();
            var document = XDocument.Load(frontProject);
            document.Descendants("StartupCommand").Single().Value = command;
            document.Descendants("BuildCommand").Single().Value = build;
            document.Descendants("BuildOutputFolder").Single().Value = manager == "dotnet" ? "$(MSBuildProjectDirectory)/dist/wwwroot" : "$(MSBuildProjectDirectory)/dist";
            document.Descendants("Target").Single(target => (string?)target.Attribute("Name") == "RestoreFrontendDependencies").Remove();
            document.Descendants("Target").Single(target => (string?)target.Attribute("Name") == "BuildFrontendAssets").Attribute("DependsOnTargets")?.Remove();
            document.Save(frontProject);
            var appConfigPath = Path.Combine(project, "src-netwiv", "netwiv.conf.json");
            var appConfig = JsonNode.Parse(File.ReadAllText(appConfigPath))!.AsObject();
            appConfig["build"]!["beforeDevCommand"] = command;
            if (manager == "dotnet")
            {
                appConfig["build"]!["frontendDist"] = "../dist/wwwroot";
                var backendProject = Directory.EnumerateFiles(Path.Combine(project, "src-netwiv"), "*.csproj").Single();
                File.WriteAllText(backendProject, File.ReadAllText(backendProject).Replace("/../dist</NetWivFrontendDist>", "/../dist/wwwroot</NetWivFrontendDist>", StringComparison.Ordinal));
            }
            File.WriteAllText(appConfigPath, appConfig.ToJsonString());
            return;
        }
        File.Delete(Path.Combine(project, "package-lock.json"));
        var manifestPath = Path.Combine(project, "package.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        if (manifest["scripts"] is JsonObject scripts)
        {
            if (scripts["predev"]?.GetValue<string>() == "npm run bindings") scripts["predev"] = "node scripts/generate-bindings.mjs";
            if (scripts["build"]?.GetValue<string>() == "npm run bindings && vite build") scripts["build"] = "node scripts/generate-bindings.mjs && vite build";
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
