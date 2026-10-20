using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Cli;

internal static class NewProjectTests
{
    internal static async Task BlazorHybrid()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-hybrid-template-").FullName;
        try
        {
            var destination = Path.Combine(root, "output with spaces");
            var result = await Run(["new", "Example", "-t", "blazor-hybrid", "-o", destination,
                "--identifier", "com.example.hybrid"], root);
            Check(result.Code == 0, result.Output + result.Error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "src-netwiv/netwiv.conf.json")))!;
            Check(config["identifier"]!.GetValue<string>() == "com.example.hybrid" &&
                config["build"]!["dotnetProject"]!.GetValue<string>() == "Example.csproj",
                "Hybrid application metadata was not personalized.");
            var backend = XDocument.Load(Path.Combine(destination, "src-netwiv/Example.csproj"));
            var hybrid = backend.Descendants("PackageReference").Single(item => (string?)item.Attribute("Include") == "NetWiv.BlazorHybrid");
            Check(hybrid.Parent!.Attribute("Condition")!.Value.Contains("NetWivBlazorMode", StringComparison.Ordinal),
                "Hybrid dependency is unconditional.");
            Check(backend.Descendants("PackageReference").All(item => (string?)item.Attribute("Version") == CliApp.Version),
                "Generated NetWiv package versions differ from the CLI.");
            Check(!backend.Descendants("Import").Any() && !backend.ToString().Contains("../../../src/", StringComparison.Ordinal),
                "Generated application depends on framework source paths.");
            var program = File.ReadAllText(Path.Combine(destination, "src-netwiv/Program.cs"));
            Check(program.Contains("\"com.example.hybrid\"", StringComparison.Ordinal), "Backend identifier differs from configuration.");
            Check(program.Contains("options.AppBaseUri.AbsoluteUri", StringComparison.Ordinal) &&
                !program.Contains("http://netwiv-blazor.localhost/", StringComparison.Ordinal),
                "Hybrid starter hardcodes a platform-specific origin.");
            Check(program.Contains("#if NETWIV_BLAZOR_EXPERIMENTAL_AOT", StringComparison.Ordinal) &&
                program.Contains("AddNetWivBlazorHybrid();", StringComparison.Ordinal) &&
                program.Contains("IUiInteropReferenceFactory, BrowserUiInteropReferenceFactory", StringComparison.Ordinal) &&
                File.ReadAllText(Path.Combine(destination, "src-netwiv/HybridUiRegistration.cs")).StartsWith("#if NETWIV_BLAZOR_EXPERIMENTAL_AOT", StringComparison.Ordinal),
                "Managed Hybrid is not the default registration/reference path.");
            Check(backend.Descendants("Error").Any(item => ((string?)item.Attribute("Condition"))?.Contains("PublishAot", StringComparison.Ordinal) == true),
                "Hybrid NativeAOT is not guarded by an explicit experimental opt-in.");
            Check(File.ReadAllText(Path.Combine(destination, "README.md")).Contains("--experimental-blazor-aot", StringComparison.Ordinal),
                "Experimental Hybrid NativeAOT publishing guidance is missing.");
            var props = XDocument.Load(Path.Combine(destination, "Directory.Build.props"));
            Check(!props.Descendants("Import").Any() && props.Descendants("NetWivBlazorMode").Single().Value == "Hybrid",
                "Generated mode configuration is not standalone or does not default to Hybrid.");
            var shared = XDocument.Load(Path.Combine(destination, "Shared/Shared.csproj"));
            Check(shared.Descendants("Exec").Single().Attribute("Command")!.Value.Contains("../src-netwiv/Example.csproj", StringComparison.Ordinal),
                "Shared client generation still points at the source sample.");
            Check(File.Exists(Path.Combine(destination, "Shared/Assets/vendor/@netwiv/api/core.js")) &&
                File.Exists(Path.Combine(destination, "WebAssembly/wwwroot/index.html")) &&
                File.Exists(Path.Combine(destination, "src-netwiv/wwwroot/index.html")),
                "A frontend mode is missing its boot assets.");
            var solution = File.ReadAllText(Path.Combine(destination, "Example.sln"));
            Check(solution.Contains("src-netwiv\\Example.csproj", StringComparison.Ordinal) &&
                solution.Contains("Shared\\Shared.csproj", StringComparison.Ordinal), "Hybrid solution is incomplete.");
            Check(!Directory.EnumerateDirectories(destination, "*", SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path) is "bin" or "obj"), "Template copied build output.");
            Check(result.Output.Contains("dotnet run --project src-netwiv", StringComparison.Ordinal), "Hybrid startup guidance is missing.");
            var hostPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (configuration, mode, aot, experimental, expectedMode, expectedFlavor, succeeds) in new[]
            {
                ("Debug", "", false, false, "Hybrid", "Hybrid", true),
                ("Release", "", false, false, "Hybrid", "Hybrid", true),
                ("Release", "Hybrid", true, false, "Hybrid", "Hybrid", false),
                ("Release", "Hybrid", true, true, "Hybrid", "HybridExperimentalAot", true),
                ("Release", "WebAssembly", true, false, "WebAssembly", "WebAssembly", true),
            })
            {
                var arguments = new List<string>
                {
                    "msbuild", Path.Combine(destination, "src-netwiv/Example.csproj"), "-nologo", "-t:ValidateBlazorMode",
                    "-getProperty:NetWivBlazorMode,BaseOutputPath,BaseIntermediateOutputPath,OutputPath,MSBuildProjectExtensionsPath,ArtifactsProjectName,JsonSerializerIsReflectionEnabledByDefault",
                    "-p:Configuration=" + configuration, "-p:PublishAot=" + aot.ToString().ToLowerInvariant(),
                    "-p:NetWivBlazorExperimentalAot=" + experimental.ToString().ToLowerInvariant(),
                };
                if (mode.Length != 0) arguments.Add("-p:NetWivBlazorMode=" + mode);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var validation = await NetWiv.Build.Execution.ProcessRunner.RunAsync(new(
                    Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", arguments, destination, CaptureOutput: true), timeout.Token);
                Check((validation.ExitCode == 0) == succeeds, "Generated mode validation did not enforce the experimental boundary: " + validation.StandardOutput + validation.StandardError);
                if (!succeeds)
                {
                    Check((validation.StandardOutput + validation.StandardError).Contains("NetWivBlazorExperimentalAot=true", StringComparison.Ordinal),
                        "AOT rejection did not explain the opt-in: " + validation.StandardOutput + validation.StandardError);
                    continue;
                }
                var values = JsonNode.Parse(validation.StandardOutput)!["Properties"]!;
                Check(values["NetWivBlazorMode"]!.GetValue<string>() == expectedMode, "MSBuild selected a different default mode.");
                Check(values["BaseOutputPath"]!.GetValue<string>().Replace('\\', '/').Contains("/" + expectedFlavor + "/", StringComparison.Ordinal) &&
                    values["MSBuildProjectExtensionsPath"]!.GetValue<string>().Replace('\\', '/').Contains("/" + expectedFlavor + "/", StringComparison.Ordinal) &&
                    values["ArtifactsProjectName"]!.GetValue<string>() == "Example-" + expectedFlavor,
                    "Generated host modes share output, restore or CLI artifacts directories.");
                Check(aot && !experimental || (values["JsonSerializerIsReflectionEnabledByDefault"]!.GetValue<string>() == "false") == experimental,
                    "JSON reflection was disabled outside the experimental host or left enabled inside it.");
                if (configuration == "Release") Check(hostPaths.Add(values["BaseOutputPath"]!.GetValue<string>()), "Release host modes reused an output root.");
                var artifactsRoot = Path.Combine(destination, "target", "msbuild-artifacts");
                var artifactArguments = arguments.Append("-p:ArtifactsPath=" + artifactsRoot).ToArray();
                var artifactValidation = await NetWiv.Build.Execution.ProcessRunner.RunAsync(new(
                    Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", artifactArguments, destination, CaptureOutput: true), timeout.Token);
                Check(artifactValidation.ExitCode == 0, "Artifacts mode evaluation failed: " + artifactValidation.StandardOutput + artifactValidation.StandardError);
                var artifactValues = JsonNode.Parse(artifactValidation.StandardOutput)!["Properties"]!;
                var normalizedRoot = Path.GetFullPath(artifactsRoot) + Path.DirectorySeparatorChar;
                foreach (var property in new[] { "BaseOutputPath", "BaseIntermediateOutputPath", "OutputPath", "MSBuildProjectExtensionsPath" })
                {
                    var evaluatedPath = artifactValues[property]!.GetValue<string>()
                        .Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    var actualPath = Path.GetFullPath(evaluatedPath, Path.Combine(destination, "src-netwiv"));
                    Check(actualPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                        actualPath.Replace('\\', '/').Contains("/Example-" + expectedFlavor + "/", StringComparison.Ordinal),
                        "Artifacts mode did not relocate and separate the generated host's " + property + ".");
                }
            }
            var bindings = await NetWiv.Build.Execution.ProcessRunner.RunAsync(new(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                ["msbuild", Path.Combine(destination, "src-netwiv/Example.csproj"), "-nologo", "-t:ValidateBlazorMode",
                    "-p:Configuration=Release", "-p:NetWivBindingsOnly=true", "-p:NetWivBlazorMode=WebAssembly",
                    "-p:ArtifactsPath=" + Path.Combine(destination, "target", "msbuild-artifacts"),
                    "-getProperty:OutputPath,MSBuildProjectExtensionsPath"], destination, CaptureOutput: true));
            Check(bindings.ExitCode == 0, "Bindings-only evaluation failed: " + bindings.StandardOutput + bindings.StandardError);
            var bindingValues = JsonNode.Parse(bindings.StandardOutput)!["Properties"]!;
            foreach (var property in new[] { "OutputPath", "MSBuildProjectExtensionsPath" })
                Check(bindingValues[property]!.GetValue<string>().Replace('\\', '/').Contains("/bindings/Release/", StringComparison.Ordinal),
                    "Artifacts mode changed the isolated bindings-only " + property + ".");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task BlazorConfigurationBuilds()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-blazor-build-").FullName;
        try
        {
            var app = Path.Combine(root, "app");
            var created = await Run(["new", "Example", "-t", "blazor", "-o", app], root);
            Check(created.Code == 0, created.Error);
            var backend = Path.Combine(app, "src-netwiv/Example.csproj");
            var bindingDirectory = XDocument.Load(backend).Descendants("NetWivCSharpBindingsDirectory").Single().Value;
            Check(bindingDirectory == "../frontend/obj/netwiv/$(Configuration)", "Generated clients share an output across configurations.");
            var frontendBuild = XDocument.Load(Path.Combine(app, "Example.Frontend.esproj")).Descendants("BuildCommand").Single().Value;
            Check(frontendBuild.Contains("-c \"$(Configuration)\"", StringComparison.Ordinal), "App build does not pass its configuration to the frontend publish.");
            Check(File.ReadAllLines(Path.Combine(app, ".gitignore")).Contains("**/obj/"), "Generated client directory is not ignored.");
            // Exercise the actual template targets with a deterministic exporter. Generator
            // semantics are tested separately; this fixture isolates MSBuild scheduling,
            // incremental Compile items and concurrent configuration output ownership.
            File.WriteAllText(backend, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <NetWivCSharpBindingsDirectory>{{bindingDirectory}}</NetWivCSharpBindingsDirectory>
                  </PropertyGroup>
                  <Target Name="ExportClient" AfterTargets="CoreCompile">
                    <MakeDir Directories="$(NetWivCSharpBindingsDirectory)" />
                    <WriteLinesToFile File="$(NetWivCSharpBindingsDirectory)/NetWivCommands.g.cs"
                      Lines="public class $(Configuration)$(FixtureSuffix)Client { }" Overwrite="true" WriteOnlyWhenDifferent="true" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(app, "contracts/Contracts.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                <TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                </PropertyGroup></Project>
                """);
            File.WriteAllText(Path.Combine(app, "frontend/NetWivUi.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                  <ItemGroup><Compile Include="Consumer.cs" /></ItemGroup>
                  <Import Project="NetWiv.targets" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(app, "frontend/Consumer.cs"), """
                #if DEBUG
                #if IMPORTED_CLIENT
                public class Consumer : DebugImportedClient { }
                #else
                public class Consumer : DebugClient { }
                #endif
                #else
                #if IMPORTED_CLIENT
                public class Consumer : ReleaseImportedClient { }
                #else
                public class Consumer : ReleaseClient { }
                #endif
                #endif
                """);
            var runner = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            async Task<string> Execute(params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(runner)
                {
                    WorkingDirectory = app, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (var argument in arguments)
                    start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
                Check(process.ExitCode == 0, await output + await error);
                return await output;
            }
            Task<string> Build(string configuration, string project = "frontend/NetWivUi.csproj", bool bindingsOnly = false) =>
                Execute("build", project, "-c", configuration, "--disable-build-servers", "-v:q", "-nologo",
                    "-p:NetWivBindingsOnly=" + bindingsOnly.ToString().ToLowerInvariant(), "--no-restore");
            // Debug and Release share NuGet's frontend/contracts obj files. Restore them
            // before testing parallel compilation; bindings-only restores are isolated by configuration.
            await Execute("restore", "frontend/NetWivUi.csproj", "--disable-build-servers", "-v:q", "-nologo");
            await Task.WhenAll(Build("Debug"), Build("Release"));
            // A competing backend export cannot replace the other configuration's client,
            // even when that frontend's incremental generation target is skipped.
            await Build("Debug", "src-netwiv/Example.csproj", true);
            await Build("Release");
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var client = Path.Combine(app, "frontend/obj/netwiv", configuration, "NetWivCommands.g.cs");
                Check(File.ReadAllText(client).Contains(configuration + "Client", StringComparison.Ordinal), "Wrong configuration client.");
                File.Delete(client);
                await Build(configuration);
                Check(File.Exists(client), "Deleted generated client was not regenerated.");
            }
            // This imported file is outside both the app and contracts source globs.
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project><PropertyGroup>
                  <FixtureSuffix>Imported</FixtureSuffix>
                  <DefineConstants>$(DefineConstants);IMPORTED_CLIENT</DefineConstants>
                </PropertyGroup></Project>
                """);
            await Build("Release");
            Check(File.ReadAllText(Path.Combine(app, "frontend/obj/netwiv/Release/NetWivCommands.g.cs"))
                .Contains("ReleaseImportedClient", StringComparison.Ordinal), "Imported properties did not refresh the client.");
            var designTime = JsonNode.Parse(await Execute("msbuild", "frontend/NetWivUi.csproj",
                "-p:Configuration=Release", "-p:DesignTimeBuild=true", "-getItem:Compile", "-nologo"))!;
            Check(designTime["Items"]!["Compile"]!.AsArray().Any(item =>
                item!["Identity"]!.GetValue<string>().Replace('\\', '/') == "obj/netwiv/Release/NetWivCommands.g.cs"),
                "IDE design-time compilation omitted the existing client.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
                ("react", "src/App.tsx"), ("vue", "src/App.vue"), ("svelte", "src/App.svelte"),
                ("angular", "src/app.ts"), ("blazor", "frontend/Pages/Home.razor"),
            };
            var listed = await Run(["new", "--list"], root);
            Check(listed.Code == 0, listed.Error);
            foreach (var (template, _) in cases)
                Check(listed.Output.Contains(template, StringComparison.Ordinal), "The template is missing from --list: " + template);
            foreach (var removed in new[] { "vanilla", "solid", "preact", "yew", "leptos", "sycamore", "dioxus", "-ts" })
                Check(!listed.Output.Contains(removed, StringComparison.Ordinal), "A retired template is still listed: " + removed);
            var matrix = Environment.GetEnvironmentVariable("NETWIV_TEST_TEMPLATE_MATRIX") ?? "full";
            if (matrix is not ("full" or "representative")) throw new InvalidOperationException("Unknown template test matrix: " + matrix);
            // Linux's baseline job covers every overlay. Other OS/SDK jobs still
            // exercise the base template, an overlay, Blazor and every manager below.
            var selected = matrix == "full" ? cases : cases.Where(item => item.Name is "react" or "angular" or "blazor").ToArray();
            Console.WriteLine($"Template matrix: {matrix} ({selected.Length}/{cases.Length} templates; all managers)");
            foreach (var (template, evidence) in selected)
            {
                var destination = Path.Combine(root, template);
                var result = await Run(["new", "Example", "--template", template, "--output", destination], root);
                Check(result.Code == 0, result.Error);
                Check(File.Exists(Path.Combine(destination, evidence)), "Missing frontend for " + template);
                var screen = File.ReadAllText(Path.Combine(destination, evidence));
                Check(screen.Contains("Example", StringComparison.Ordinal) && !screen.Contains("NetWiv app", StringComparison.Ordinal), "Starter screen does not show the project name: " + template);
                Check(!screen.Contains("getName", StringComparison.Ordinal), "Starter screen calls an API that needs a capability: " + template);
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
                if (template == "blazor")
                {
                    Check(config["build"]!["frontendDist"]!.GetValue<string>() == "../dist/wwwroot", "Blazor asset root mismatch");
                    Check(File.Exists(Path.Combine(destination, "frontend/wwwroot/vendor/@netwiv/api/upstream/core.js")), "Blazor JS API module missing");
                    Check(File.Exists(Path.Combine(destination, "frontend/wwwroot/vendor/@netwiv/api/core.js")), "Blazor NetWiv API module missing");
                    Check(File.ReadAllText(Path.Combine(destination, "frontend/wwwroot/netwiv.js")).Contains("./vendor/@netwiv/api/core.js", StringComparison.Ordinal), "Blazor frontend bypasses NetWiv API");
                    foreach (var sample in new[] { "frontend/Pages/Counter.razor", "frontend/Pages/Weather.razor", "frontend/Layout/NavMenu.razor", "frontend/wwwroot/lib", "frontend/wwwroot/css/bootstrap", "frontend/wwwroot/sample-data", "vendor", "scripts" })
                        Check(!File.Exists(Path.Combine(destination, sample)) && !Directory.Exists(Path.Combine(destination, sample)), "Blazor sample or leftover remains: " + sample);
                    Check(!File.ReadAllText(Path.Combine(destination, "frontend/wwwroot/index.html")).Contains("bootstrap", StringComparison.Ordinal), "Blazor still links Bootstrap");
                    // The typed C# client: shared contracts, the export target and the client registration.
                    Check(File.ReadAllText(Path.Combine(destination, "contracts/Contracts.csproj")).Contains("TreatAsLocalProperty=\"AssemblyName\"", StringComparison.Ordinal) && File.ReadAllText(Path.Combine(destination, "contracts/Contracts.cs")).Contains("record Greeting", StringComparison.Ordinal), "Blazor contracts project missing");
                    Check(File.ReadAllText(Path.Combine(destination, "Example.sln")).Contains("Contracts.csproj", StringComparison.Ordinal), "Contracts project missing from the solution");
                    var program = File.ReadAllText(Path.Combine(destination, "src-netwiv/Program.cs"));
                    Check(!program.Contains("record Greeting", StringComparison.Ordinal) && !program.Contains("class AppJsonContext", StringComparison.Ordinal), "Shared types were not moved out of Program.cs");
                    var backendProject = File.ReadAllText(Path.Combine(destination, "src-netwiv/Example.csproj"));
                    Check(backendProject.Contains("<NetWivCSharpBindingsDirectory>../frontend/obj/netwiv/$(Configuration)</NetWivCSharpBindingsDirectory>", StringComparison.Ordinal) &&
                        !backendProject.Contains("NetWivFrontendBindingsDirectory", StringComparison.Ordinal) && backendProject.Contains("../contracts/Contracts.csproj", StringComparison.Ordinal), "Blazor app does not export the C# client");
                    Check(File.Exists(Path.Combine(destination, "Directory.Build.props")) && !File.Exists(Path.Combine(destination, "src-netwiv/Directory.Build.props")), "Bindings-only build isolation missing");
                    Check(File.ReadAllText(Path.Combine(destination, "frontend/NetWivUi.csproj")).Contains("NetWiv.targets", StringComparison.Ordinal), "Frontend does not import NetWiv.targets");
                    Check(File.ReadAllText(Path.Combine(destination, "frontend/Program.cs")).Contains("AddScoped<NetWiv.Generated.NetWivCommands>()", StringComparison.Ordinal), "Typed client is not registered");
                    Check(File.ReadAllText(Path.Combine(destination, "frontend/Pages/Home.razor")).Contains("Commands.GreetAsync(Name)", StringComparison.Ordinal), "Blazor screen does not use the typed client");
                }
                else
                {
                    Check(File.Exists(Path.Combine(destination, "package-lock.json")), "npm lock missing: " + template);
                    var package = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "package.json")))!;
                    Check(package["dependencies"]!["@netwiv/api"]!.GetValue<string>() == "file:vendor/netwiv-api", "NetWiv API dependency is not portable: " + template);
                    Check(package["dependencies"]!["@tauri-apps/api"] is null, "Frontend directly depends on the compatibility API: " + template);
                    var api = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "vendor/netwiv-api/package.json")))!;
                    Check(api["name"]!.GetValue<string>() == "@netwiv/api" && File.Exists(Path.Combine(destination, "vendor/netwiv-api/lib/core.js")), "Local NetWiv API package is incomplete: " + template);
                    Check(api["dependencies"] is null, "The frontend facade must be self-contained: " + template);
                    Check(!File.Exists(Path.Combine(destination, "src/App.tsx")) || template == "react", "React source leaked into " + template);
                    Check(File.Exists(Path.Combine(destination, template == "angular" ? "tsconfig.app.json" : "tsconfig.json")), "TypeScript configuration missing: " + template);
                }
            }
            foreach (var (manager, template) in new[] { ("pnpm", "vue"), ("yarn", "svelte"), ("bun", "react"), ("deno", "angular") })
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
                new[] { "new", "App", "--template", "blazor-hybrid", "--manager", "npm" },
                new[] { "new", "App", "--template", "react", "--manager", "cargo" },
                new[] { "new", "App", "--template", "yew" }, new[] { "new", "App", "--template", "react-ts" } })
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
