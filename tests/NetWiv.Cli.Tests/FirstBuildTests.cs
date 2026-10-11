using NetWiv.Cli;

internal static class FirstBuildTests
{
    internal static async Task MissingDist()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p2-first-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"identifier":"dev.netwiv.firstbuild","build":{"frontendDist":"dist","dotnetProject":"Probe.csproj"}}
                """);
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Target Name="Frontend" BeforeTargets="AssignTargetPaths">
                    <MakeDir Directories="$(NetWivFrontendDist)" />
                    <WriteLinesToFile File="$(NetWivFrontendDist)/index.html" Lines="first build" Overwrite="true" />
                    <ItemGroup><EmbeddedResource Include="$(NetWivFrontendDist)/index.html" LogicalName="frontend" /></ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using System;
                using System.IO;
                using System.Reflection;
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("frontend");
                Console.Write(new StreamReader(stream!).ReadToEnd());
                """);
            var error = new StringWriter();
            var result = await CliApp.RunAsync(["build", "--no-bundle"], TextWriter.Null, error, root);
            if (result != 0) throw new Exception(error.ToString());
            var binary = Directory.EnumerateFiles(Path.Combine(root, "target"), OperatingSystem.IsWindows() ? "Probe.exe" : "Probe", SearchOption.AllDirectories)
                .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == "release");
            var info = new System.Diagnostics.ProcessStartInfo(binary) { RedirectStandardOutput = true, UseShellExecute = false };
            using var process = System.Diagnostics.Process.Start(info)!;
            var content = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !content.Contains("first build")) throw new Exception("Published NativeAOT app did not embed the newly built frontend.");
            File.Delete(Path.Combine(root, "dist", "index.html"));
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
                """);
            error = new StringWriter();
            result = await CliApp.RunAsync(["build", "--no-bundle"], TextWriter.Null, error, root);
            if (result == 0 || !error.ToString().Contains("index.html")) throw new Exception("Missing frontend after publish was accepted.");
        }
        finally { Directory.Delete(root, true); }
    }
}
