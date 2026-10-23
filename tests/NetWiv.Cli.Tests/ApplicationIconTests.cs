using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Execution;

internal static class ApplicationIconTests
{
    internal static async Task BuildSelection()
    {
        var repository = Directory.GetCurrentDirectory();
        var configuration = typeof(ApplicationIconTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = Directory.CreateTempSubdirectory("netwiv-icon-build-").FullName;
        try
        {
            var icons = Directory.CreateDirectory(Path.Combine(root, "icons")).FullName;
            foreach (var name in new[] { "icon.png", "icon.ico", "icon.icns" })
                File.Copy(Path.Combine(repository, "templates/react-app/src-netwiv/icons", name), Path.Combine(icons, name));
            var config = Path.Combine(root, "effective.json");
            var project = Path.Combine(root, "IconProbe.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net8.0"),
                    new XElement("NetWivConfigPath", config), new XElement("NetWivResourceBaseDirectory", icons),
                    new XElement("_NetWivBindingsTaskAssembly", Path.Combine(repository, "src/NetWiv.Build.Tasks/bin", configuration, "net8.0/NetWiv.Build.Tasks.dll"))),
                new XElement("Import", new XAttribute("Project", Path.Combine(repository, "src/NetWiv.Build/buildTransitive/NetWiv.Build.targets")))))
                .Save(project);
            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("netwiv.default-icon");
                System.Console.Write(stream is null ? "none" : System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
                """);
            foreach (var (json, runtime, windows) in new[]
            {
                ("""{"bundle":{"icon":["icon.icns","icon.ico","icon.png"]}}""", "icon.png", "icon.ico"),
                ("""{"bundle":{"icon":["icon.ico"]}}""", "icon.ico", "icon.ico"),
                ("""{"bundle":{"icon":"icon.png"}}""", "icon.png", ""),
                ("""{"bundle":{"icon":["icon.icns"]}}""", "", ""),
                ("""{"bundle":{"icon":[]}}""", "", ""),
                ("{}", "", ""),
            })
            {
                File.WriteAllText(config, json);
                var result = await Run("msbuild", project, "-nologo", "-t:NetWivResolveApplicationIcon", "-getProperty:ApplicationIcon", "-getItem:EmbeddedResource");
                var values = JsonNode.Parse(result.StandardOutput)!;
                var embedded = values["Items"]!["EmbeddedResource"]!.AsArray()
                    .Where(item => item?["LogicalName"]?.GetValue<string>() == "netwiv.default-icon").ToArray();
                Check(runtime.Length == 0 ? embedded.Length == 0 : embedded.Length == 1 &&
                    Path.GetFullPath(embedded[0]!["Identity"]!.GetValue<string>()) == Path.GetFullPath(Path.Combine(icons, runtime)), "Wrong embedded icon selection: " + result.StandardOutput);
                Check(values["Properties"]!["ApplicationIcon"]!.GetValue<string>() == (windows.Length == 0 ? "" : Path.GetFullPath(Path.Combine(icons, windows))), "Wrong Windows icon selection.");
            }
            File.WriteAllText(config, """{"bundle":{"icon":["icon.png","icon.ico"]}}""");
            var overridden = await Run("msbuild", project, "-nologo", "-t:NetWivResolveApplicationIcon", "-p:ApplicationIcon=custom.ico", "-getProperty:ApplicationIcon");
            Check(overridden.StandardOutput.Trim() == "custom.ico", "Explicit ApplicationIcon was overwritten.");
            await Run("build", project, "-nologo", "-v:q");
            var executed = await Run(Path.Combine(root, "bin/Debug/net8.0/IconProbe.dll"));
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(icons, "icon.png"))));
            Check(executed.StandardOutput.Trim() == expected, "Compiled resource differs from the configured icon.");
            File.WriteAllText(config, "{}");
            await Run("build", project, "-nologo", "-v:q");
            Check((await Run(Path.Combine(root, "bin/Debug/net8.0/IconProbe.dll"))).StandardOutput.Trim() == "none", "Incremental build retained a removed icon.");
            File.WriteAllText(config, """{"bundle":{"icon":["missing.png"]}}""");
            var missing = await ProcessRunner.RunAsync(new("dotnet", ["msbuild", project, "-nologo", "-t:NetWivResolveApplicationIcon"], root, CaptureOutput: true));
            Check(missing.ExitCode != 0 && missing.StandardOutput.Contains("Configured application icon is missing.", StringComparison.Ordinal), "Missing icon was silently ignored.");

            async Task<ProcessResult> Run(params string[] args)
            {
                var result = await ProcessRunner.RunAsync(new("dotnet", args, root, CaptureOutput: true));
                Check(result.ExitCode == 0, result.StandardOutput + result.StandardError);
                return result;
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
