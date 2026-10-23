using System.Security;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class ShellSidecarConsumerTests
{
    internal static async Task Run(bool aot)
    {
        var root = Directory.CreateTempSubdirectory("netwiv-shell-consumer-").FullName;
        try
        {
            var repository = FindRepository();
            var target = BuildTarget.Resolve(null);
            var app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var helper = Directory.CreateDirectory(Path.Combine(root, "helper")).FullName;
            var helpers = Directory.CreateDirectory(Path.Combine(app, "helpers")).FullName;
            var framework = $"net{Environment.Version.Major}.0";
            File.WriteAllText(Path.Combine(helper, "Helper.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(helper, "Program.cs"), """
                if (args is ["echo"]) { Console.WriteLine("sidecar-fixture-ok"); return 0; }
                if (args is ["wait"]) { await Task.Delay(Timeout.Infinite); return 0; }
                return 1;
                """);
            var helperOutput = Path.Combine(root, "helper-publish");
            await Execute("dotnet", ["publish", Path.Combine(helper, "Helper.csproj"), "-c", "Release", "-r", target.RuntimeIdentifier,
                "-p:PublishAot=true", "-o", helperOutput], root);
            var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
            var triple = OperatingSystem.IsWindows() ? "x86_64-pc-windows-msvc" :
                OperatingSystem.IsMacOS() ? "aarch64-apple-darwin" : "x86_64-unknown-linux-gnu";
            var helperSource = Path.Combine(helpers, "helper-" + triple + suffix);
            File.Copy(Path.Combine(helperOutput, "Helper" + suffix), helperSource);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helperSource, File.GetUnixFileMode(Path.Combine(helperOutput, "Helper")));
            string Source(string relative) => SecurityElement.Escape(Path.Combine(repository, "src", relative))!;
            File.WriteAllText(Path.Combine(app, "Consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
                    <DefaultItemExcludes>$(DefaultItemExcludes);target/**</DefaultItemExcludes>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{Source("NetWiv.Core/NetWiv.Core.csproj")}" />
                    <ProjectReference Include="{Source("NetWiv.Plugin.Shell/NetWiv.Plugin.Shell.csproj")}" />
                  </ItemGroup>
                  <Import Project="{Source("NetWiv.Build/buildTransitive/NetWiv.Build.targets")}" />
                </Project>
                """);
            using (var source = typeof(ShellSidecarConsumerTests).Assembly.GetManifestResourceStream("sidecar-consumer.cs")!)
            using (var output = File.Create(Path.Combine(app, "Program.cs"))) await source.CopyToAsync(output);
            File.WriteAllText(Path.Combine(app, "netwiv.conf.json"), """
                {"identifier":"dev.netwiv.sidecar-consumer","productName":"Consumer","version":"1.0.0",
                 "build":{"dotnetProject":"Consumer.csproj"},"bundle":{"externalBin":["helpers/helper"]}}
                """);
            var dev = new List<string> { "dev", "--release", "--no-watch", "--no-dev-server" };
            if (aot) dev.Add("--aot");
            await Cli(dev.ToArray(), app);
            var build = new List<string> { "build", "--no-bundle" };
            if (aot) build.Add("--aot");
            await Cli(build.ToArray(), app);
            var publish = Path.Combine(app, "target", target.RequestedTarget, "release");
            var config = ConfigLoader.Load(app, target.Platform);
            string consumer;
            if (OperatingSystem.IsMacOS())
            {
                var bundle = await NativeBundler.BuildMacAppAsync(config, publish, Path.Combine(publish, "Consumer"), true, CancellationToken.None, target: target);
                await Execute("/usr/bin/codesign", ["--verify", "--deep", "--strict", bundle], app);
                consumer = Path.Combine(bundle, "Contents/MacOS/Consumer");
                Check(!File.Exists(Path.Combine(bundle, "Contents/Resources/helper")), "macOS helper must reside in MacOS.");
            }
            else if (OperatingSystem.IsLinux())
            {
                await NativeBundler.BundleAsync(config, target, publish, "deb", true, TextWriter.Null, CancellationToken.None);
                var package = Directory.GetFiles(Path.Combine(publish, "bundle/deb"), "*.deb").Single();
                var extracted = Path.Combine(root, "extracted");
                await Execute("dpkg-deb", ["-x", package, extracted], app);
                consumer = Path.Combine(extracted, "usr/lib/consumer/Consumer");
            }
            else
            {
                await NativeBundler.BundleAsync(config, target, publish, "nsis", true, TextWriter.Null, CancellationToken.None);
                // Execute only the staged application, never the generated installer.
                consumer = Path.Combine(publish, "bundle/nsis/payload/Consumer.exe");
            }
            var result = await Execute(consumer, [], app);
            Check(result.Contains("PASS default ShellPlugin staged sidecar execute scope spawn and kill", StringComparison.Ordinal), "Packaged consumer did not complete its public Shell assertions.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task Cli(string[] args, string cwd)
    {
        using var error = new StringWriter();
        var result = await CliApp.RunAsync(args, TextWriter.Null, error, cwd);
        Check(result == 0, "Sidecar consumer CLI failed: " + error);
    }

    private static async Task<string> Execute(string command, string[] args, string cwd)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await ProcessRunner.RunAsync(new ProcessSpec(command, args, cwd, CaptureOutput: true), timeout.Token);
        Check(result.ExitCode == 0, "Sidecar consumer command failed: " + result.StandardOutput + result.StandardError);
        return result.StandardOutput;
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "NetWiv.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
