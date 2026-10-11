using System.Text.Json;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class FirstBuildTests
{
    internal static async Task MissingDist(string mode)
    {
        var root = Directory.CreateTempSubdirectory("netwiv-first-build-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"identifier":"dev.netwiv.firstbuild","productName":"Probe","version":"1.0.0","build":{"frontendDist":"dist","dotnetProject":"Probe.csproj"}}
                """);
            var core = Path.GetFullPath("src/NetWiv.Core/NetWiv.Core.csproj");
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>net{{Environment.Version.Major}}.0</TargetFramework>
                    <!-- Legacy app settings must not force AOT or trimming in managed CLI builds. -->
                    <PublishAot>true</PublishAot><PublishTrimmed>true</PublishTrimmed>
                    <DefineConstants Condition="'$(PublishAot)' != 'true'">MANAGED_PROBE</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="{{System.Security.SecurityElement.Escape(core)}}" /></ItemGroup>
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
                using System.Runtime.CompilerServices;
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("frontend");
                Console.WriteLine(new StreamReader(stream!).ReadToEnd());
                using var marker = typeof(NetWiv.AppHandle).Assembly.GetManifestResourceStream("netwiv.bundle.type");
                Console.WriteLine(new StreamReader(marker!).ReadToEnd());
                Console.WriteLine("dynamic=" + RuntimeFeature.IsDynamicCodeSupported);
                #if MANAGED_PROBE
                var method = new System.Reflection.Emit.DynamicMethod("probe", typeof(int), Type.EmptyTypes);
                var il = method.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 42);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                Console.WriteLine("emit=" + ((Func<int>)method.CreateDelegate(typeof(Func<int>)))());
                #endif
                """);
            var arguments = new List<string> { "build" };
            if (OperatingSystem.IsMacOS() && mode != "aot") arguments.AddRange(["--bundles", "app", "--no-sign"]);
            else arguments.Add("--no-bundle");
            if (mode != "managed") arguments.Add(mode == "aot" ? "--aot" : "--framework-dependent");
            if (mode == "managed") arguments.AddRange(["--", "-p:PublishAot=true", "-p:PublishTrimmed=true", "-p:PublishSingleFile=true", "-p:UseAppHost=false"]);
            var error = new StringWriter();
            var result = await CliApp.RunAsync(arguments.ToArray(), TextWriter.Null, error, root);
            if (result != 0) throw new Exception(error.ToString());
            var publish = Path.Combine(root, "target", BuildTarget.Resolve(null).RequestedTarget, "release");
            var binary = Path.Combine(publish, OperatingSystem.IsWindows() ? "Probe.exe" : "Probe");
            var content = await Execute(binary);
            if (!content.Contains("first build") || !content.Contains("dynamic=" + (mode != "aot")) ||
                mode != "aot" && !content.Contains("emit=42")) throw new Exception("Published application did not execute the selected mode and embedded frontend.");
            if (mode != "aot")
            {
                using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(publish, "Probe.runtimeconfig.json")));
                var selfContained = runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _);
                if (selfContained != (mode == "managed")) throw new Exception("Incorrect runtime deployment model.");
                if (OperatingSystem.IsMacOS())
                {
                    var app = Path.Combine(publish, "bundle/macos/Probe.app");
                    content = await Execute(Path.Combine(app, "Contents/MacOS/Probe"));
                    if (!content.Contains("__NETWIV_BUNDLE_TYPE_VAR_MAC") || !content.Contains("emit=42")) throw new Exception("Managed macOS bundle did not execute with patched metadata.");
                }
                else
                {
                    NativeBundler.PatchBundleType(binary, OperatingSystem.IsWindows() ? "MSI" : "DEB");
                    content = await Execute(binary);
                    if (!content.Contains("__NETWIV_BUNDLE_TYPE_VAR_" + (OperatingSystem.IsWindows() ? "MSI" : "DEB"))) throw new Exception("Managed bundle metadata was not patched.");
                }
            }
            if (mode == "managed")
            {
                // Reusing an output directory must not ship stale managed runtime files in AOT mode.
                error = new StringWriter();
                result = await CliApp.RunAsync(["build", "--aot", "--no-bundle"], TextWriter.Null, error, root);
                if (result != 0 || File.Exists(Path.Combine(publish, "Probe.runtimeconfig.json")) ||
                    File.Exists(Path.Combine(publish, "NetWiv.Core.dll"))) throw new Exception("Mode switch retained managed payload: " + error);
                content = await Execute(binary);
                if (!content.Contains("dynamic=False")) throw new Exception("Mode switch did not execute NativeAOT.");
            }
            File.Delete(Path.Combine(root, "dist", "index.html"));
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{Environment.Version.Major}.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"{System.Security.SecurityElement.Escape(core)}\" /></ItemGroup></Project>");
            error = new StringWriter();
            result = await CliApp.RunAsync(arguments.ToArray(), TextWriter.Null, error, root);
            if (result == 0 || !error.ToString().Contains("index.html")) throw new Exception("Missing frontend after publish was accepted.");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task InvalidModes()
    {
        var error = new StringWriter();
        var result = await CliApp.RunAsync(["build", "--aot", "--framework-dependent"], TextWriter.Null, error, Path.GetTempPath());
        if (result == 0 || !error.ToString().Contains("--framework-dependent")) throw new Exception("Conflicting deployment modes were not rejected before loading the project.");
    }

    private static async Task<string> Execute(string binary)
    {
        var result = await ProcessRunner.RunAsync(new ProcessSpec(binary, [], Path.GetDirectoryName(binary)!, CaptureOutput: true));
        if (result.ExitCode != 0) throw new Exception(result.StandardError);
        return result.StandardOutput;
    }
}
