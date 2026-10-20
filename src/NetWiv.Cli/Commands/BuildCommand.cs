using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class BuildCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        if (args.Contains("--help")) { await output.WriteLineAsync("Usage: netwiv build [--debug] [--aot | --framework-dependent] [--blazor-mode Hybrid|WebAssembly] [--experimental-blazor-aot] [--no-bundle] [-b BUNDLES] [-t TARGET] [-c CONFIG] [-f FEATURES] [-- RUNNER_ARGS]"); return 0; }
        var arguments = new CommandArguments(args);
        var runnerArgs = arguments.TakeAfterSeparator();
        var debug = arguments.TakeFlag("--debug", "-d");
        var noBundle = arguments.TakeFlag("--no-bundle");
        var aot = arguments.TakeFlag("--aot");
        var blazorMode = CommandConfig.BlazorMode(arguments.TakeOption("--blazor-mode"));
        var experimentalBlazorAot = arguments.TakeFlag("--experimental-blazor-aot");
        CommandConfig.ValidateBlazorAot(aot, blazorMode, experimentalBlazorAot);
        var frameworkDependent = arguments.TakeFlag("--framework-dependent");
        if (aot && frameworkDependent) throw new ArgumentException("--aot cannot be combined with --framework-dependent.");
        var noSign = arguments.TakeFlag("--no-sign");
        var bundles = arguments.TakeOption("--bundles", "-b");
        var runner = arguments.TakeOption("--runner", "-r");
        var featuresArg = arguments.TakeOption("--features", "-f");
        arguments.TakeFlag("--ci");
        var ignoreVersions = arguments.TakeFlag("--ignore-version-mismatches");
        var (config, target) = CommandConfig.Load(arguments, workingDirectory);
        arguments.EnsureEmpty();
        if (ignoreVersions) throw new NotSupportedException("--ignore-version-mismatches cannot be honored until NetWiv package version compatibility validation is implemented.");
        target.EnsureHostOperatingSystem();

        var active = config.Boolean("bundle", "active") ?? false;
        if (!noBundle && (active || bundles is not null))
        {
            if (!aot && NativeBundler.ParseFormats(config, target, bundles).Contains("app-store"))
                throw new NotSupportedException("The app-store target requires NativeAOT; use netwiv build --aot.");
            NativeBundler.ValidateConfiguration(config, target, bundles, noSign);
        }

        var project = CommandConfig.Project(config);
        var features = CommandConfig.Features(config, featuresArg);
        var environment = HookEnvironment.Create(target, debug, config);
        await HookRunner.RunAsync(config, "beforeBuildCommand", environment, cancellationToken);

        var configuration = debug ? "Debug" : "Release";
        var profile = debug ? "debug" : "release";
        var publishDirectory = Path.Combine(config.ProjectDirectory, "target", target.RequestedTarget, profile);
        var artifactsDirectory = Path.Combine(config.ProjectDirectory, "target", target.RequestedTarget, "msbuild-artifacts");
        var effectiveConfig = CommandConfig.WriteEffectiveConfig(config, target, debug);
        // A mode switch must not leave a previous runtime or managed DLL in the new payload.
        if (Directory.Exists(publishDirectory)) Directory.Delete(publishDirectory, true);
        Directory.CreateDirectory(publishDirectory);
        var publishArguments = new List<string>
        {
            "publish", project, "-c", configuration, "-r", target.RuntimeIdentifier,
            "-p:ContinuousIntegrationBuild=true",
            $"-p:NetWivFeatures={string.Join("%3B", features)}", $"-p:NetWivConfigPath={effectiveConfig}",
            $"-p:NetWivResourceBaseDirectory={config.ConfigDirectory}",
            "--artifacts-path", artifactsDirectory, "-o", publishDirectory
        };
        if (config.String("mainBinaryName") is { } mainBinaryName) publishArguments.Add($"-p:AssemblyName={mainBinaryName}");
        if (config.String("build", "frontendDist") is { } frontend && !Uri.TryCreate(frontend, UriKind.Absolute, out _))
            publishArguments.Add($"-p:NetWivFrontendDist={Path.GetFullPath(frontend, config.ConfigDirectory)}");
        publishArguments.AddRange(runnerArgs);
        if (blazorMode is not null) publishArguments.Add($"-p:NetWivBlazorMode={blazorMode}");
        publishArguments.Add($"-p:NetWivBlazorExperimentalAot={experimentalBlazorAot.ToString().ToLowerInvariant()}");
        // CLI deployment modes are authoritative even for older AOT-first templates.
        publishArguments.AddRange(["--self-contained", frameworkDependent ? "false" : "true",
            $"-p:PublishAot={aot.ToString().ToLowerInvariant()}",
            $"-p:PublishTrimmed={aot.ToString().ToLowerInvariant()}", "-p:UseAppHost=true"]);
        if (!aot) publishArguments.Add("-p:PublishSingleFile=false");
        if (CommandConfig.IsolationDirectory(config) is { } isolation) publishArguments.Add($"-p:NetWivIsolationDist={isolation}");

        await output.WriteLineAsync($"{(aot ? "NativeAOT" : frameworkDependent ? "Framework-dependent .NET" : "Self-contained .NET")} publishing {Path.GetFileName(project)} for {target.RuntimeIdentifier}...");
        var result = await ProcessRunner.RunAsync(CommandRunner.Create(config, runner, publishArguments, environment), cancellationToken);
        if (result.ExitCode != 0) return result.ExitCode;
        // MSBuild project references may produce the frontend during publish.
        ValidateFrontend(config);
        EnsureBinary(config, project, publishDirectory);

        if (!noBundle && (active || bundles is not null))
        {
            await HookRunner.RunAsync(config, "beforeBundleCommand", environment, cancellationToken);
            return await NativeBundler.BundleAsync(config, target, publishDirectory, bundles, noSign, output, cancellationToken);
        }
        await output.WriteLineAsync($"Published: {publishDirectory}");
        return 0;
    }

    internal static void ValidateFrontend(Build.Configuration.ConfigDocument config)
    {
        if (config.String("build", "frontendDist") is not { } frontend || Uri.TryCreate(frontend, UriKind.Absolute, out _)) return;
        var path = Path.GetFullPath(frontend, config.ConfigDirectory);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"build.frontendDist does not exist: {path}");
        if (!File.Exists(Path.Combine(path, "index.html"))) throw new FileNotFoundException($"build.frontendDist must contain index.html: {path}");
    }

    internal static string EnsureBinary(Build.Configuration.ConfigDocument config, string project, string publishDirectory)
    {
        var binaryName = config.String("mainBinaryName") ?? Path.GetFileNameWithoutExtension(project);
        var binary = Path.Combine(publishDirectory, binaryName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        if (!File.Exists(binary)) throw new FileNotFoundException($"Publish did not produce expected application binary: {binary}");
        return binary;
    }
}
