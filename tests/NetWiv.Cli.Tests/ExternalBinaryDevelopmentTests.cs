using System.Security;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class ExternalBinaryDevelopmentTests
{
    internal static async Task Run(bool aot)
    {
        if (!aot) await RootProjectIsolation();
        var root = Directory.CreateTempSubdirectory("netwiv-dev-sidecar-").FullName;
        try
        {
            var repository = FindRepository();
            var targets = SecurityElement.Escape(Path.Combine(repository, "src/NetWiv.Build/buildTransitive/NetWiv.Build.targets"));
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>{(OperatingSystem.IsWindows() ? "WinExe" : "Exe")}</OutputType><TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings><DefaultItemExcludes>$(DefaultItemExcludes);target/**</DefaultItemExcludes>
                  </PropertyGroup>
                  <Import Project="{targets}" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using System.Diagnostics;
                var helper = Path.Combine(AppContext.BaseDirectory, "helper" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
                if (OperatingSystem.IsWindows()) start.ArgumentList.Add("cmd.exe");
                using var child = Process.Start(start)!;
                await child.WaitForExitAsync();
                if (child.ExitCode != 0) throw new Exception("Staged helper execution failed.");
                File.WriteAllText(args[0], "sidecar executed");
                """);
            var triple = OperatingSystem.IsWindows() ? "x86_64-pc-windows-msvc" :
                OperatingSystem.IsMacOS() ? "aarch64-apple-darwin" : "x86_64-unknown-linux-gnu";
            var source = Path.Combine(root, "helper-" + triple + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var executable = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "where.exe") : "/usr/bin/true";
            File.Copy(executable, source);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"test.netwiv.dev.sidecar","build":{"dotnetProject":"Probe.csproj"},"bundle":{"externalBin":["helper"]}}""");
            var marker = Path.Combine(root, "executed.txt");
            var arguments = new List<string> { "dev", "--release", "--no-watch", "--no-dev-server" };
            if (aot) arguments.Add("--aot");
            arguments.AddRange(["--", marker]);
            for (var repeat = 0; repeat < 2; repeat++)
            {
                File.Delete(marker);
                using var error = new StringWriter();
                var code = await CliApp.RunAsync(arguments.ToArray(), TextWriter.Null, error, root);
                if (code != 0 || !File.Exists(marker) || File.ReadAllText(marker) != "sidecar executed")
                    throw new Exception("CLI development did not stage and execute the helper: " + error);
            }
            var original = File.ReadAllBytes(source);
            File.WriteAllBytes(source, [.. original, 0]);
            File.Delete(marker);
            using var failure = new StringWriter();
            var failed = await CliApp.RunAsync(arguments.ToArray(), TextWriter.Null, failure, root);
            if (failed == 0 || File.Exists(marker)) throw new Exception("Changed development helper overwrote an existing output.");
            // MSBuild writes its diagnostic to the process console; AOT staging reports it through the CLI.
            if (aot && !failure.ToString().Contains("remove the old generated copy", StringComparison.Ordinal))
                throw new Exception("Changed helper failure did not explain how to retry.");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task RootProjectIsolation()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-dev-sidecar-reference-").FullName;
        try
        {
            var repository = FindRepository();
            var targets = SecurityElement.Escape(Path.Combine(repository, "src/NetWiv.Build/buildTransitive/NetWiv.Build.targets"));
            var framework = $"net{Environment.Version.Major}.0";
            Directory.CreateDirectory(Path.Combine(root, "Helper"));
            File.WriteAllText(Path.Combine(root, "App.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>{(OperatingSystem.IsWindows() ? "WinExe" : "Exe")}</OutputType><TargetFramework>{framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
                  <ItemGroup><ProjectReference Include="Helper/Helper.csproj" ReferenceOutputAssembly="false" Private="false" /><Compile Remove="Helper/**/*.cs" /></ItemGroup>
                  <Import Project="{targets}" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using System.Diagnostics;
                var sidecar = Path.Combine(AppContext.BaseDirectory, "Helper" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                var start = new ProcessStartInfo(sidecar) { UseShellExecute = false, CreateNoWindow = true };
                if (OperatingSystem.IsWindows()) start.ArgumentList.Add("cmd.exe");
                using var child = Process.Start(start)!;
                await child.WaitForExitAsync();
                if (child.ExitCode != 0) throw new Exception("Staged helper execution failed.");
                File.WriteAllText(args[0], "root sidecar executed");
                """);
            File.WriteAllText(Path.Combine(root, "Helper", "Helper.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
                  <Import Project="{targets}" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Helper", "Program.cs"), "Console.WriteLine(\"referenced helper project\");");

            var target = BuildTarget.Resolve(null);
            var extension = target.Platform == "windows" ? ".exe" : string.Empty;
            var sidecar = Path.Combine(root, "Helper-" + target.RequestedTarget + extension);
            var executable = target.Platform == "windows" ? Path.Combine(Environment.SystemDirectory, "where.exe") : "/usr/bin/true";
            File.Copy(executable, sidecar);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(sidecar, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"identifier":"test.netwiv.dev.sidecar.reference","build":{"dotnetProject":"App.csproj"},"bundle":{"externalBin":["Helper"]}}
                """);

            var marker = Path.Combine(root, "executed.txt");
            using var error = new StringWriter();
            var result = await CliApp.RunAsync(["dev", "--release", "--no-watch", "--no-dev-server", "--", marker], TextWriter.Null, error, root);
            if (result != 0 || !File.Exists(marker) || File.ReadAllText(marker) != "root sidecar executed")
                throw new Exception("Development staging for an executable project reference failed: " + error);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "NetWiv.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
