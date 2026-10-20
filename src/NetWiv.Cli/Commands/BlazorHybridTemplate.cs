using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace NetWiv.Cli;

internal static class BlazorHybridTemplate
{
    internal static async Task CreateAsync(string project, string name, string version, CancellationToken token)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "templates", "blazor-hybrid");
        if (!File.Exists(Path.Combine(source, "Desktop", "Desktop.csproj")))
            throw new DirectoryNotFoundException("The Blazor Hybrid template is missing from this NetWiv CLI installation.");
        CopyDirectory(source, project, token);
        Directory.Move(Path.Combine(project, "Desktop"), Path.Combine(project, "src-netwiv"));
        var desktopProject = Path.Combine(project, "src-netwiv", name + ".csproj");
        File.Move(Path.Combine(project, "src-netwiv", "Desktop.csproj"), desktopProject);

        var propsPath = Path.Combine(project, "Directory.Build.props");
        var props = XDocument.Load(propsPath);
        props.Root!.Elements("Import").Remove();
        props.Root.AddFirst(new XElement("PropertyGroup",
            new XElement("TargetFramework", "net8.0"),
            new XElement("LangVersion", "12.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", "enable"),
            new XElement("TreatWarningsAsErrors", "true")));
        props.Save(propsPath);

        foreach (var path in Directory.EnumerateFiles(project, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            foreach (var reference in document.Descendants("ProjectReference").ToArray())
            {
                var include = (string?)reference.Attribute("Include") ?? "";
                if (!include.StartsWith("../../../src/NetWiv.", StringComparison.Ordinal)) continue;
                var package = Path.GetFileNameWithoutExtension(include);
                if (package == "NetWiv.Build.Tasks") package = "NetWiv.Build";
                var replacement = new XElement("PackageReference", new XAttribute("Include", package), new XAttribute("Version", version));
                if (package is "NetWiv.Build" or "NetWiv.Generators") replacement.Add(new XAttribute("PrivateAssets", "all"));
                reference.ReplaceWith(replacement);
            }
            document.Descendants("Import").Where(element => ((string?)element.Attribute("Project"))?.Contains("../../../src/", StringComparison.Ordinal) == true).Remove();
            document.Descendants("Content").Where(element => ((string?)element.Attribute("Include"))?.StartsWith("../../../templates/", StringComparison.Ordinal) == true).Remove();
            foreach (var command in document.Descendants("Exec"))
            {
                var attribute = command.Attribute("Command");
                if (attribute is not null)
                    attribute.Value = attribute.Value.Replace("../Desktop/Desktop.csproj", "../src-netwiv/" + name + ".csproj", StringComparison.Ordinal);
            }
            if (path == desktopProject) document.Descendants("AssemblyName").Single().Value = name;
            document.Save(path);
        }

        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "templates", "frontends", "shared", "vendor"),
            Path.Combine(project, "Shared", "Assets", "vendor"), token);
        var config = new JsonObject
        {
            ["productName"] = name,
            ["version"] = "0.1.0",
            ["identifier"] = "dev.netwiv.blazorsample",
            ["build"] = new JsonObject { ["dotnetProject"] = name + ".csproj" },
            ["bundle"] = new JsonObject { ["active"] = false },
        };
        await File.WriteAllTextAsync(Path.Combine(project, "src-netwiv", "netwiv.conf.json"), config.ToJsonString(), token);
        var tools = new JsonObject
        {
            ["version"] = 1,
            ["isRoot"] = true,
            ["tools"] = new JsonObject
            {
                ["netwiv.cli"] = new JsonObject { ["version"] = version, ["commands"] = new JsonArray("netwiv") },
            },
        };
        Directory.CreateDirectory(Path.Combine(project, ".config"));
        await File.WriteAllTextAsync(Path.Combine(project, ".config", "dotnet-tools.json"), tools.ToJsonString(), token);
        await File.WriteAllTextAsync(Path.Combine(project, ".gitignore"), "**/bin/\n**/obj/\n**/target/\n**/dist/\n.vs/\n", token);
        var programPath = Path.Combine(project, "src-netwiv", "Program.cs");
        var program = await File.ReadAllTextAsync(programPath, token);
        await File.WriteAllTextAsync(programPath, program.Replace("\"NetWiv shared Blazor sample\"", JsonValue.Create(name)!.ToJsonString(), StringComparison.Ordinal), token);
        await File.WriteAllTextAsync(Path.Combine(project, name + ".sln"), Solution(name), token);
        await File.WriteAllTextAsync(Path.Combine(project, "README.md"), Readme(name), token);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken token)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "dist" or "target" or ".vs")) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string Solution(string name)
    {
        var projects = new[]
        {
            (Name: name, Path: "src-netwiv\\" + name + ".csproj"),
            (Name: "Contracts", Path: "Contracts\\Contracts.csproj"),
            (Name: "Shared", Path: "Shared\\Shared.csproj"),
            (Name: "WebAssembly", Path: "WebAssembly\\WebAssembly.csproj"),
        }.Select(project => (project.Name, project.Path, Id: Guid.NewGuid().ToString("B").ToUpperInvariant())).ToArray();
        var text = new StringBuilder("Microsoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version 17\n");
        foreach (var project in projects)
            text.Append("Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"").Append(project.Name).Append("\", \"").Append(project.Path).Append("\", \"").Append(project.Id).Append("\"\nEndProject\n");
        text.Append("Global\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n\t\tDebug|Any CPU = Debug|Any CPU\n\t\tRelease|Any CPU = Release|Any CPU\n\tEndGlobalSection\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n");
        foreach (var project in projects)
            foreach (var configuration in new[] { "Debug", "Release" })
                text.Append("\t\t").Append(project.Id).Append('.').Append(configuration).Append("|Any CPU.ActiveCfg = ").Append(configuration).Append("|Any CPU\n")
                    .Append("\t\t").Append(project.Id).Append('.').Append(configuration).Append("|Any CPU.Build.0 = ").Append(configuration).Append("|Any CPU\n");
        return text.Append("\tEndGlobalSection\nEndGlobal\n").ToString();
    }

    private static string Readme(string name) => $$"""
        # {{name}}

        Open `{{name}}.sln` and select the `{{name}}` desktop project as the startup
        project, or run `dotnet run --project src-netwiv`. Debug defaults to managed
        Blazor Hybrid on Windows x64/WebView2. Set breakpoints in `Shared/App.razor`,
        `Shared/App.razor.cs` and `src-netwiv/GreetingCommands.cs` to follow a Razor
        event, generated typed IPC call and desktop command in the same process.

        The Hybrid host references `NetWiv.BlazorHybrid` and registers it with
        `AddNetWivBlazorHybridAot`. Its explicit registrations are shared by
        managed development and NativeAOT publishing.

        The same shared Razor library runs as WebAssembly when building Release:

        ```powershell
        dotnet run --project src-netwiv -c Debug -p:NetWivBlazorMode=WebAssembly
        dotnet publish src-netwiv -c Release -r win-x64 -p:PublishAot=true
        ```

        NativeAOT applies to the desktop backend, not the WebAssembly UI. The release
        dependency graph excludes the optional Hybrid package. Explicitly select
        Hybrid with `-p:NetWivBlazorMode=Hybrid`. The same statically registered
        Hybrid renderer can also be published as NativeAOT:

        ```powershell
        dotnet publish src-netwiv -c Release -r win-x64 -p:NetWivBlazorMode=Hybrid -p:PublishAot=true
        dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode Hybrid
        ```

        Hybrid registers its components, JSON metadata and instance callbacks in
        `src-netwiv/HybridUiRegistration.cs`. Register each added concrete component
        and closed generic child there. There is no registration source generator.
        The sample uses manual form validation and statically selected navigation
        views. Router assembly discovery, NavigationLock and location-changing
        guards are unsupported by this Hybrid host. Copy additional Razor library
        `_content` assets explicitly; Debug static-web-asset manifests are not discovered.
        WebAssembly keeps its separate dependency graph.
        Both modes call commands through the same `@netwiv/api` opaque JSON bridge.

        Increment, edit the name and call Greet. Try invalid and valid form submissions,
        navigate to Details and Home, and roundtrip the nested DTO through its instance
        callback. Read the document title, reload and close the window.
        Reload resets the UI count and scope ID while the backend call count continues.
        Hybrid page-scoped services are disposed on reattachment and shutdown.
        Interactive rendering, disposal and debugger acceptance must be validated in
        the target desktop session; a successful build alone does not verify them.

        `dotnet tool restore` then `dotnet tool run netwiv -- build --no-bundle --aot`
        also publishes the default WebAssembly release. No frontend server or npm
        installation is required. Use your NetWiv NuGet feed for package restore.
        `netwiv dev --blazor-mode Hybrid` selects Hybrid explicitly; add `--aot` to
        publish and run its native development executable.

        TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
        NetWiv is independent and is not affiliated with, endorsed by, or officially
        approved by The Tauri Programme within the Commons Conservancy.
        """;
}
