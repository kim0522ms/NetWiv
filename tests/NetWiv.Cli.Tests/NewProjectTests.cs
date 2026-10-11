using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Cli;

internal static class NewProjectTests
{
    internal static async Task CreateReact()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-new-test-").FullName;
        try
        {
            var feed = Path.Combine(root, "feed & packages");
            Directory.CreateDirectory(feed);
            var destination = Path.Combine(root, "output with spaces");
            Directory.CreateDirectory(destination);
            var (code, output, error) = await Run(["new", "My.App", "-t", "react", "-o", "output with spaces",
                "--identifier", "com.example.custom", "--source", "feed & packages"], root);
            Check(code == 0, output + error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "src-netwiv/netwiv.conf.json")))!;
            Check(config["productName"]!.GetValue<string>() == "My.App" && config["identifier"]!.GetValue<string>() == "com.example.custom", "Application metadata was not personalized");
            Check(config["build"]!["dotnetProject"]!.GetValue<string>() == "My.App.csproj", "Project config retained the template name");
            var project = XDocument.Load(Path.Combine(destination, "src-netwiv/My.App.csproj"));
            Check(project.Descendants("NetWivVersion").Single().Value == CliApp.Version, "SDK version differs from CLI");
            var frontend = project.Descendants("ProjectReference").Single();
            Check(frontend.Attribute("Include")!.Value == "../My.App.Frontend.esproj" &&
                frontend.Attribute("ReferenceOutputAssembly")!.Value == "false", "Frontend must be a build-only project reference");
            var solution = File.ReadAllText(Path.Combine(destination, "My.App.sln"));
            Check(solution.Contains("src-netwiv\\My.App.csproj", StringComparison.Ordinal) &&
                solution.Contains("My.App.Frontend.esproj", StringComparison.Ordinal) &&
                !solution.Contains("ReactExample", StringComparison.Ordinal), "Solution paths were not personalized");
            Check(!project.Descendants("PublishAot").Any() && !project.Descendants("IsAotCompatible").Any(), "Consumer template must not opt into AOT");
            Check(project.Descendants("TargetFramework").Single().Value == "net8.0", "Backend must support .NET 8");
            Check(project.Descendants("RollForward").Single().Value == "Major", "Managed app must start when only .NET 9/10 is installed");
            Check(File.Exists(Path.Combine(destination, "My.App.Frontend.esproj")), "Frontend project is missing");
            var program = File.ReadAllText(Path.Combine(destination, "src-netwiv/Program.cs"));
            Check(program.Contains("\"com.example.custom\"", StringComparison.Ordinal) && program.Contains("#if !DEBUG", StringComparison.Ordinal), "Template engine lost metadata or the development guard");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, ".config/dotnet-tools.json")))!;
            Check(manifest["tools"]!["netwiv.cli"]!["version"]!.GetValue<string>() == CliApp.Version, "Local tool version differs from SDK");
            var source = XDocument.Load(Path.Combine(destination, "NuGet.Config")).Descendants("add").First();
            Check(source.Attribute("value")!.Value == feed, "Local source path was not resolved or XML escaped");
            foreach (var file in new[] { "package.json", "package-lock.json" })
            {
                var json = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, file)))!;
                Check(json["name"]!.GetValue<string>() == "my.app", "npm project name was not updated");
                if (file == "package-lock.json") Check(json["packages"]![""]!["name"]!.GetValue<string>() == "my.app", "npm lock root differs");
            }
            Check(File.Exists(Path.Combine(destination, ".gitignore")), "Hidden template files were not copied");
            Check(!Directory.Exists(Path.Combine(destination, ".template.config")), "Template engine metadata leaked into the app");
            Check(!Directory.EnumerateDirectories(root, ".netwiv-new-*").Any(), "Temporary template hive remains");
            Check(output.Contains("npm run desktop:dev", StringComparison.Ordinal), "Missing startup instructions");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Defaults()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-new-test-").FullName;
        try
        {
            var (code, output, error) = await Run(["new", "HelloWorld"], root);
            Check(code == 0, output + error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "HelloWorld/src-netwiv/netwiv.conf.json")))!;
            Check(config["identifier"]!.GetValue<string>() == "dev.netwiv.helloworld", "Incorrect default identifier");
            Check(!File.Exists(Path.Combine(root, "HelloWorld/NuGet.Config")), "Unrequested package sources were installed");
            Check(output.Contains("NuGet.Config", StringComparison.Ordinal), "Private feed setup was not explained");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task TemplateMatrix()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-template-matrix-").FullName;
        try
        {
            var cases = new (string Name, string Evidence)[]
            {
                ("vanilla", "src/main.js"), ("vanilla-ts", "src/main.ts"),
                ("vue", "src/App.vue"), ("vue-ts", "src/App.vue"),
                ("svelte", "src/App.svelte"), ("svelte-ts", "src/App.svelte"),
                ("react", "src/App.jsx"), ("react-ts", "src/App.tsx"),
                ("solid", "src/App.jsx"), ("solid-ts", "src/App.tsx"),
                ("angular", "angular.json"), ("preact", "src/App.jsx"), ("preact-ts", "src/App.tsx"),
                ("yew", "src/main.rs"), ("leptos", "src/main.rs"),
                ("sycamore", "src/main.rs"), ("dioxus", "src/main.rs"),
                ("blazor", "frontend/NetWivUi.csproj"),
            };
            var listed = await Run(["new", "--list"], root);
            Check(listed.Code == 0, listed.Error);
            foreach (var (template, evidence) in cases)
            {
                Check(listed.Output.Contains(template, StringComparison.Ordinal), "The upstream template is missing from --list: " + template);
                var destination = Path.Combine(root, template);
                var result = await Run(["new", "Example", "--template", template, "--output", destination], root);
                Check(result.Code == 0, result.Error);
                Check(File.Exists(Path.Combine(destination, evidence)), "Missing frontend for " + template);
                var config = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "src-netwiv/netwiv.conf.json")))!;
                Check(config["build"]!["dotnetProject"]!.GetValue<string>() == "Example.csproj", "Backend project mismatch: " + template);
                Check(File.Exists(Path.Combine(destination, "Example.sln")), "Solution missing: " + template);
                var backend = XDocument.Load(Path.Combine(destination, "src-netwiv/Example.csproj"));
                Check(!backend.Descendants("PublishAot").Any() && !backend.Descendants("IsAotCompatible").Any(), "Template forces AOT: " + template);
                Check(backend.Descendants("PackageReference").Any(item => item.Attribute("Include")?.Value == "NetWiv.Platform.Desktop"), "Desktop package missing: " + template);
                Check(!backend.ToString().Contains("NetWiv.Platform.Wry", StringComparison.Ordinal), "Former package name leaked into " + template);
                var readme = File.ReadAllText(Path.Combine(destination, "README.md"));
                Check(readme.Contains("TAURI®", StringComparison.Ordinal) && readme.Contains("The Tauri Programme within the Commons Conservancy", StringComparison.Ordinal), "Trademark notice missing: " + template);
                Check(readme.Contains("not affiliated with, endorsed by, or officially approved by", StringComparison.Ordinal), "Independent template notice missing: " + template);
                if (template is "yew" or "leptos" or "sycamore" or "dioxus")
                {
                    Check(File.Exists(Path.Combine(destination, "vendor/@tauri-apps/api/core.js")), "Official JS API module missing: " + template);
                    Check(!File.Exists(Path.Combine(destination, "package.json")), "Rust frontend unexpectedly requires npm: " + template);
                }
                else if (template == "blazor")
                {
                    Check(config["build"]!["frontendDist"]!.GetValue<string>() == "../dist/wwwroot", "Blazor asset root mismatch");
                    Check(File.Exists(Path.Combine(destination, "frontend/wwwroot/vendor/@tauri-apps/api/core.js")), "Blazor JS API module missing");
                }
                else
                {
                    Check(File.Exists(Path.Combine(destination, "package-lock.json")), "npm lock missing: " + template);
                    Check(!File.Exists(Path.Combine(destination, "src/App.jsx")) || template == "react" || template is "solid" or "preact", "React source leaked into " + template);
                }
            }
            foreach (var (manager, template) in new[] { ("pnpm", "vue"), ("yarn", "svelte"), ("bun", "react"), ("deno", "vanilla") })
            {
                var destination = Path.Combine(root, manager);
                var result = await Run(["new", "Example", "-t", template, "-m", manager, "-o", destination], root);
                Check(result.Code == 0, result.Error);
                Check(!File.Exists(Path.Combine(destination, "package-lock.json")), "npm lock leaked into " + manager);
                if (manager == "yarn") Check(File.Exists(Path.Combine(destination, "yarn.lock")) &&
                    File.ReadAllText(Path.Combine(destination, ".yarnrc.yml")).Contains("nodeLinker: node-modules", StringComparison.Ordinal), "Yarn project boundary or node_modules mode missing");
                if (manager == "deno") Check(File.Exists(Path.Combine(destination, "deno.json")), "Deno node_modules setting missing");
                var config = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "src-netwiv/netwiv.conf.json")))!;
                Check(config["build"]!["beforeDevCommand"]!.GetValue<string>() == (manager == "deno" ? "deno task dev" : manager + " run dev"), "Incorrect dev manager: " + manager);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task InvalidInput()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-new-test-").FullName;
        try
        {
            foreach (var args in new[] {
                new[] { "new" }, new[] { "new", "../Escape" }, new[] { "new", "CON" }, new[] { "new", "COM1.Test" },
                new[] { "new", "Trailing." }, new[] { "new", "Bad Name" }, new[] { "new", "App", "--template", "unknown" },
                new[] { "new", "App", "--identifier", "bad identifier" }, new[] { "new", "App", "--source", "missing" },
                new[] { "new", "App", "--bogus" }, new[] { "new", "App", "Extra" }, new[] { "new", "App", "-o" },
                new[] { "new", "App", "--template", "blazor", "--manager", "npm" },
                new[] { "new", "App", "--template", "yew", "--manager", "pnpm" } })
            {
                var (code, _, error) = await Run(args, root);
                Check(code == 1 && error.Length > 0, "Invalid arguments succeeded: " + string.Join(' ', args));
                Check(!Directory.EnumerateFileSystemEntries(root).Any(), "Rejected arguments changed the destination");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task PreserveAndCancel()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-new-test-").FullName;
        try
        {
            var target = Path.Combine(root, "App");
            Directory.CreateDirectory(target);
            var sentinel = Path.Combine(target, "keep.txt");
            File.WriteAllText(sentinel, "keep");
            var (code, _, _) = await Run(["new", "App"], root);
            Check(code == 1 && File.ReadAllText(sentinel) == "keep", "Existing project was overwritten");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var result = await Run(["new", "Cancelled"], root, cancelled.Token);
            Check(result.Code == 130 && !Directory.Exists(Path.Combine(root, "Cancelled")), "Cancelled creation wrote files");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Discovery()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-new-test-").FullName;
        try
        {
            foreach (var args in new[] { new[] { "new", "--help" }, new[] { "new", "-h" }, new[] { "new", "--list" } })
            {
                var (code, output, _) = await Run(args, root);
                Check(code == 0 && output.Contains("react", StringComparison.Ordinal), "Template help/list missing");
            }
            var help = await Run(["--help"], root);
            Check(help.Output.Contains("new", StringComparison.Ordinal), "Root help does not list new");
            foreach (var shell in new[] { "bash", "zsh", "fish", "powershell", "elvish" })
            {
                var result = await Run(["completions", "--shell", shell], root);
                Check(result.Code == 0 && result.Output.Contains("new", StringComparison.Ordinal), "Completion omitted new");
            }
            Check(!Directory.EnumerateFileSystemEntries(root).Any(), "Discovery modified the destination");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<(int Code, string Output, string Error)> Run(string[] args, string cwd, CancellationToken token = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApp.RunAsync(args, output, error, cwd, token);
        return (code, output.ToString(), error.ToString());
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
