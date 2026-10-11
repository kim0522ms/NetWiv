using System.Diagnostics;
using System.Text.Json;
using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;

namespace NetWiv.Cli;

internal static class DevCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        if (args.Contains("--help")) { await output.WriteLineAsync("Usage: netwiv dev [--release] [--no-watch] [--aot] [--no-dev-server] [--port PORT] [-t TARGET] [-c CONFIG] [-f FEATURES] [-- APP_ARGS]"); return 0; }
        var arguments = new CommandArguments(args);
        var appArguments = arguments.TakeAfterSeparator();
        var release = arguments.TakeFlag("--release");
        var noWatch = arguments.TakeFlag("--no-watch");
        var aot = arguments.TakeFlag("--aot");
        var runner = arguments.TakeOption("--runner", "-r");
        var featuresArg = arguments.TakeOption("--features", "-f");
        var (config, target) = CommandConfig.Load(arguments, workingDirectory);
        var additionalWatch = arguments.TakeOptions("--additional-watch-folders");
        var noDevServerWait = ResolveNoDevServerWait(arguments.TakeFlag("--no-dev-server-wait"), Environment.GetEnvironmentVariable("TAURI_CLI_NO_DEV_SERVER_WAIT"));
        var noDevServer = arguments.TakeFlag("--no-dev-server");
        var port = arguments.TakeOption("--port");
        var exitOnPanic = arguments.TakeFlag("--exit-on-panic");
        arguments.EnsureEmpty();
        if (exitOnPanic) throw new NotSupportedException("--exit-on-panic is Rust panic behavior and has no .NET equivalent.");
        var devUrl = config.String("build", "devUrl");
        var environmentPort = Environment.GetEnvironmentVariable("TAURI_CLI_PORT");
        var serverPort = ResolveServerPort(port, environmentPort);
        target.EnsureHostOperatingSystem();
        var project = CommandConfig.Project(config);
        var features = CommandConfig.Features(config, featuresArg);
        var watchFolders = ResolveWatchFolders(config, additionalWatch);
        var environment = HookEnvironment.Create(target, !release, config);
        // Generic Host apps read IHostEnvironment from DOTNET_ENVIRONMENT, as `dotnet run` launch profiles set it.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
            environment = With(environment, "DOTNET_ENVIRONMENT", "Development");
        Process? hook = null;
        Task? hookMonitor = null;
        int? hookExitCode = null;
        var callerCancellation = cancellationToken;
        using var hookLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = hookLifetime.Token;
        BuiltInDevServer? server = null;
        ChangeWatcher? configurationChanges = null;
        try
        {
            hook = await HookRunner.RunAsync(config, "beforeDevCommand", environment, cancellationToken);
            if (hook is not null) hookMonitor = MonitorHookAsync(hook);
            if (!noDevServer && devUrl is null && config.String("build", "frontendDist") is { } frontendDist && !Uri.TryCreate(frontendDist, UriKind.Absolute, out _))
            {
                server = BuiltInDevServer.Start(Path.GetFullPath(frontendDist, config.ConfigDirectory), serverPort, autoPort: port is null && environmentPort is null);
                devUrl = server.Url;
                await output.WriteLineAsync($"Serving {frontendDist} at {devUrl}");
            }
            if (devUrl is not null) environment = With(environment, "NETWIV_DEV_URL", devUrl);
            if (!noDevServerWait && devUrl is not null) await WaitForUrlAsync(devUrl, cancellationToken);

            if (aot)
                return await RunAotAsync(config, target, project, release, !noWatch, additionalWatch, featuresArg, appArguments, runner, environment, devUrl, output, cancellationToken);

            configurationChanges = noWatch ? null : WatchConfiguration(config);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string effectiveConfig;
                try
                {
                    var current = config;
                    (config, effectiveConfig) = await RetryWhileSavingAsync(() =>
                    {
                        var loaded = ConfigLoader.Load(current.ConfigDirectory, target.Platform, current.Overlays);
                        return (loaded, CommandConfig.WriteEffectiveConfig(loaded, target, !release, devUrl));
                    }, cancellationToken);
                    environment = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
                    { ["TAURI_CONFIG"] = config.CliPatch?.ToJsonString() };
                }
                catch (Exception error) when (!noWatch && error is NetWivConfigException or IOException)
                {
                    await output.WriteLineAsync($"Configuration error: {error.Message}");
                    await WaitForChangesOrRetryAsync(configurationChanges!, error, cancellationToken);
                    continue;
                }
                project = CommandConfig.Project(config);
                features = CommandConfig.Features(config, featuresArg);
                watchFolders = ResolveWatchFolders(config, additionalWatch);
                if (!noWatch)
                {
                    var replacement = WatchConfiguration(config);
                    configurationChanges?.Dispose();
                    configurationChanges = replacement;
                }
                var processArgs = new List<string>();
                if (!noWatch)
                {
                    processArgs.AddRange(["watch", "--no-hot-reload", "--project", project, "run"]);
                    environment = With(environment, "DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER", "1");
                }
                else processArgs.AddRange(["run", "--project", project]);
                processArgs.AddRange(["-c", release ? "Release" : "Debug", "--property:NetWivFeatures=" + string.Join("%3B", features)]);
                processArgs.Add("--property:NetWivDevelopment=true");
                if (!noWatch && watchFolders.Count > 0) processArgs.Add("--property:NetWivAdditionalWatchItems=" + string.Join("%3B", watchFolders.Select(path => Path.Combine(path, "**", "*"))));
                processArgs.Add($"--property:NetWivConfigPath={effectiveConfig}");
                processArgs.Add($"--property:NetWivResourceBaseDirectory={config.ConfigDirectory}");
                if (devUrl is null && config.String("build", "frontendDist") is { } frontend && !Uri.TryCreate(frontend, UriKind.Absolute, out _))
                    processArgs.Add($"--property:NetWivFrontendDist={Path.GetFullPath(frontend, config.ConfigDirectory)}");
                processArgs.Add("--");
                if (CommandConfig.IsolationDirectory(config) is { } isolation) processArgs.Insert(processArgs.Count - 1, $"--property:NetWivIsolationDist={isolation}");
                processArgs.AddRange(appArguments);
                await output.WriteLineAsync($"Running {Path.GetFileName(project)} in {(release ? "Release" : "Debug")} managed mode...");
                var process = ProcessRunner.Start(CommandRunner.Create(config, runner, processArgs, environment, noWatch ? 1 : 5));
                var exit = await WaitForExitOrChangeAsync(process, configurationChanges, cancellationToken);
                if (exit is not null) return exit.Value;
                await output.WriteLineAsync("Configuration or permissions changed; restarting managed application...");
                }
        }
        catch (OperationCanceledException) when (hookExitCode is not null && !callerCancellation.IsCancellationRequested)
        {
            await output.WriteLineAsync($"build.beforeDevCommand exited with code {hookExitCode.Value}.");
            return hookExitCode.Value;
        }
        finally
        {
            hookLifetime.Cancel();
            if (hookMonitor is not null)
                try { await hookMonitor; } catch (OperationCanceledException) when (hookLifetime.IsCancellationRequested) { }
            configurationChanges?.Dispose();
            if (hook is not null) await ProcessRunner.StopAsync(hook);
            hook?.Dispose();
            if (server is not null) await server.DisposeAsync();
        }

        async Task MonitorHookAsync(Process process)
        {
            await process.WaitForExitAsync(hookLifetime.Token);
            if (process.ExitCode == 0) return;
            hookExitCode = process.ExitCode;
            hookLifetime.Cancel();
        }
    }

    private static async Task<int> RunAotAsync(ConfigDocument config, BuildTarget target, string project, bool release, bool watch, IReadOnlyList<string> additionalWatchFolders, string? featuresArg, IReadOnlyList<string> appArgs, string? runner, IReadOnlyDictionary<string, string?> environment, string? developmentUrl, TextWriter output, CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(config.ProjectDirectory, "target", target.RequestedTarget, release ? "release" : "debug", "dev-aot");
        var effectiveConfig = string.Empty;
        var artifactsDirectory = Path.Combine(config.ProjectDirectory, "target", target.RequestedTarget, "msbuild-artifacts-dev");
        var changes = watch ? CreateWatcher(config, project, ResolveWatchFolders(config, additionalWatchFolders), includeFrontend: developmentUrl is null) : null;
        try
        {
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var current = config;
                (config, effectiveConfig) = await RetryWhileSavingAsync(() =>
                {
                    var loaded = ConfigLoader.Load(current.ConfigDirectory, target.Platform, current.Overlays);
                    return (loaded, CommandConfig.WriteEffectiveConfig(loaded, target, !release, developmentUrl));
                }, cancellationToken);
                environment = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
                { ["TAURI_CONFIG"] = config.CliPatch?.ToJsonString() };
            }
            catch (Exception error) when (watch && error is NetWivConfigException or IOException)
            {
                await output.WriteLineAsync($"Configuration error: {error.Message}");
                await WaitForChangesOrRetryAsync(changes!, error, cancellationToken);
                continue;
            }
            project = CommandConfig.Project(config);
            if (watch)
            {
                var replacement = CreateWatcher(config, project, ResolveWatchFolders(config, additionalWatchFolders), includeFrontend: developmentUrl is null);
                changes?.Dispose();
                changes = replacement;
            }
            var features = CommandConfig.Features(config, featuresArg);
            var publish = new List<string> { "publish", project, "-c", release ? "Release" : "Debug", "-r", target.RuntimeIdentifier, "--self-contained", "true", "-p:PublishAot=true", "-p:NetWivDevelopment=true", $"-p:NetWivFeatures={string.Join("%3B", features)}", $"-p:NetWivConfigPath={effectiveConfig}", $"-p:NetWivResourceBaseDirectory={config.ConfigDirectory}", "--artifacts-path", artifactsDirectory, "-o", outputDirectory };
            if (config.String("mainBinaryName") is { } mainBinaryName) publish.Add($"-p:AssemblyName={mainBinaryName}");
            if (developmentUrl is null && config.String("build", "frontendDist") is { } frontend && !Uri.TryCreate(frontend, UriKind.Absolute, out _))
                publish.Add($"-p:NetWivFrontendDist={Path.GetFullPath(frontend, config.ConfigDirectory)}");
            var binaryName = config.String("mainBinaryName") ?? Path.GetFileNameWithoutExtension(project);
            if (CommandConfig.IsolationDirectory(config) is { } isolation) publish.Add($"-p:NetWivIsolationDist={isolation}");
            var binary = Path.Combine(outputDirectory, binaryName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            var result = await ProcessRunner.RunAsync(CommandRunner.Create(config, runner, publish, environment), cancellationToken);
            if (result.ExitCode != 0)
            {
                if (!watch) return result.ExitCode;
                await WaitForChangesAsync(changes!, cancellationToken);
                continue;
            }
            if (!File.Exists(binary)) throw new FileNotFoundException($"NativeAOT publish succeeded but application binary was not found: {binary}");
            await output.WriteLineAsync($"Running AOT development build {binary}...");
            var process = ProcessRunner.Start(new ProcessSpec(binary, appArgs, config.ProjectDirectory, environment));
            var exit = await WaitForExitOrChangeAsync(process, changes, cancellationToken);
            if (exit is not null) return exit.Value;
            await output.WriteLineAsync("Source changed; rebuilding NativeAOT application...");
        } while (true);
        }
        finally { changes?.Dispose(); }
    }

    private static async Task<int?> WaitForExitOrChangeAsync(Process process, ChangeWatcher? changes, CancellationToken token)
    {
        if (changes is null) return await ProcessRunner.WaitOwnedAsync(process, token);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var exited = ProcessRunner.WaitOwnedAsync(process, stop.Token);
        var changed = changes.WaitAsync(stop.Token);
        try
        {
            if (await Task.WhenAny(exited, changed) == exited) return await exited;
            await changed;
            stop.Cancel();
            try { await exited; } catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(150, token);
            changes.Drain();
            return null;
        }
        finally
        {
                stop.Cancel();
                try { await changed; } catch (OperationCanceledException) { }
                try { await exited; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static async Task WaitForChangesAsync(ChangeWatcher changes, CancellationToken token)
    {
        await changes.WaitAsync(token);
        await Task.Delay(150, token);
        changes.Drain();
    }

    // Releasing a file that was held open raises no change event, so a reload that failed with
    // an IOException is retried periodically as well as on the next change.
    private static async Task WaitForChangesOrRetryAsync(ChangeWatcher changes, Exception error, CancellationToken token)
    {
        if (error is not IOException) { await WaitForChangesAsync(changes, token); return; }
        using var retry = CancellationTokenSource.CreateLinkedTokenSource(token);
        retry.CancelAfter(TimeSpan.FromSeconds(1));
        try { await WaitForChangesAsync(changes, retry.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }

    internal static ChangeWatcher WatchConfiguration(ConfigDocument config)
    {
        using var resources = JsonDocument.Parse(config.Node("bundle", "resources")?.ToJsonString() ?? "null");
        var matchesResource = ResourcePaths.SourceMatcher(resources.RootElement, config.ConfigDirectory);
        var sources = config.Sources.Where(path => path != "<inline-config>").ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var roots = sources.Select(path => Path.GetDirectoryName(path)!).Append(config.ConfigDirectory)
            .Concat(ResourcePaths.WatchRoots(resources.RootElement, config.ConfigDirectory)).Distinct().ToArray();
        return new ChangeWatcher(roots, path =>
        {
            if (sources.Contains(path) || matchesResource(path)) return true;
            var relative = Path.GetRelativePath(config.ConfigDirectory, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts[0] is "permissions" or "capabilities") return true;
            if (parts.Length != 1) return false;
            return relative.StartsWith("netwiv.", StringComparison.Ordinal) || relative.StartsWith("tauri.", StringComparison.Ordinal)
                || relative.StartsWith("NetWiv.", StringComparison.Ordinal) || relative.StartsWith("Tauri.", StringComparison.Ordinal);
        });
    }

    internal static IReadOnlyList<string> ResolveWatchFolders(ConfigDocument config, IReadOnlyList<string> commandLine)
    {
        var configured = commandLine.Count > 0 ? commandLine : config.Strings("build", "additionalWatchFolders");
        var result = new List<string>();
        if (CommandConfig.IsolationDirectory(config) is { } isolation)
        {
            if (!Directory.Exists(isolation)) throw new DirectoryNotFoundException($"Isolation directory does not exist: {isolation}");
            result.Add(isolation);
        }
        foreach (var value in configured)
        {
            var path = Path.GetFullPath(value, config.ConfigDirectory);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Additional watch folder does not exist: {path}");
            if (path.Contains(';', StringComparison.Ordinal)) throw new ArgumentException($"Additional watch folder cannot contain a semicolon: {path}");
            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
        }
        return result;
    }

    internal static ChangeWatcher CreateWatcher(ConfigDocument config, string project, IReadOnlyList<string> additionalWatchFolders, bool includeFrontend)
    {
        var roots = new List<string> { config.ConfigDirectory };
        var projectDirectory = Path.GetDirectoryName(project)!;
        if (!roots.Contains(projectDirectory, StringComparer.OrdinalIgnoreCase)) roots.Add(projectDirectory);
        if (includeFrontend && config.String("build", "frontendDist") is { } frontend && !Uri.TryCreate(frontend, UriKind.Absolute, out _))
        {
            var path = Path.GetFullPath(frontend, config.ConfigDirectory);
            if (Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase)) roots.Add(path);
        }
        foreach (var path in additionalWatchFolders)
            if (!roots.Contains(path, StringComparer.OrdinalIgnoreCase)) roots.Add(path);
        var sourceRoots = roots.ToArray();
        using var resources = JsonDocument.Parse(config.Node("bundle", "resources")?.ToJsonString() ?? "null");
        var matchesResource = ResourcePaths.SourceMatcher(resources.RootElement, config.ConfigDirectory);
        roots.AddRange(ResourcePaths.WatchRoots(resources.RootElement, config.ConfigDirectory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sources = config.Sources.Where(path => path != "<inline-config>").ToArray();
        roots.AddRange(sources.Select(path => Path.GetDirectoryName(path)!));
        return new ChangeWatcher(roots.Distinct(), path =>
            matchesResource(path) ||
            sources.Any(source => string.Equals(source, path, comparison)) ||
            sourceRoots.Any(root => string.Equals(root, path, comparison) ||
                path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison)));
    }

    internal static int ResolveServerPort(string? commandLine, string? environment)
    {
        var value = commandLine ?? environment;
        if (value is null) return 1430;
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"Invalid built-in development server port '{value}'.");
        return port;
    }

    internal static bool ResolveNoDevServerWait(bool commandLine, string? environment)
    {
        if (commandLine) return true;
        if (environment is null) return false;
        if (bool.TryParse(environment, out var enabled)) return enabled;
        if (environment is "1") return true;
        if (environment is "0") return false;
        throw new ArgumentException($"Invalid TAURI_CLI_NO_DEV_SERVER_WAIT value '{environment}'.");
    }

    internal sealed class ChangeWatcher : IDisposable
    {
        private readonly System.Threading.Channels.Channel<bool> _changes = System.Threading.Channels.Channel.CreateBounded<bool>(1);
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly Func<string, bool>? _include;
        private readonly object _pendingGate = new();
        private readonly Dictionary<string, bool> _pending = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public ChangeWatcher(IEnumerable<string> roots, Func<string, bool>? include = null)
        {
            _include = include;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var normalizedRoots = roots.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
            foreach (var root in normalizedRoots.Where(root => !normalizedRoots.Any(other =>
                !string.Equals(root, other, comparison) && root.StartsWith(Path.TrimEndingDirectorySeparator(other) + Path.DirectorySeparatorChar, comparison))))
            {
                var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler changed = (_, item) => OnChanged(root, item.FullPath, item.ChangeType);
                RenamedEventHandler renamed = (_, item) => { OnChanged(root, item.OldFullPath, WatcherChangeTypes.Deleted); OnChanged(root, item.FullPath, WatcherChangeTypes.Created); };
                watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed; watcher.Renamed += renamed;
                _watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
            }
        }

        public async Task WaitAsync(CancellationToken token)
        {
            while (true)
            {
                await _changes.Reader.ReadAsync(token);
                lock (_pendingGate)
                {
                    if (_pending.Count == 0) continue;
                    _pending.Clear();
                    return;
                }
            }
        }
        public void Drain()
        {
            lock (_pendingGate) { _pending.Clear(); while (_changes.Reader.TryRead(out _)) { } }
        }
        internal void OnChanged(string root, string path, WatcherChangeTypes kind)
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj" or "target" or ".git")) return;
            if (_include is not null && !_include(path)) return;
            // Directory timestamps change when build tools create temporary files.
            // Renames, creations and deletions still matter for source directories.
            if (kind == WatcherChangeTypes.Changed && Directory.Exists(path)) return;
            lock (_pendingGate)
            {
                if (kind == WatcherChangeTypes.Deleted && _pending.TryGetValue(path, out var created) && created)
                    _pending.Remove(path);
                else if (!_pending.ContainsKey(path)) _pending[path] = kind == WatcherChangeTypes.Created;
                _changes.Writer.TryWrite(true);
            }
        }
        public void Dispose() { foreach (var watcher in _watchers) watcher.Dispose(); }
    }

    private static async Task WaitForUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException($"Invalid build.devUrl '{url}'.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(180);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken); return; }
            catch (HttpRequestException) { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
        }
        throw new TimeoutException($"Timed out waiting for development server {url}.");
    }

    // A watcher reload can race an editor that still holds the file open (a Windows sharing violation)
    // or is replacing it (briefly missing). Both settle within moments, and the change event that
    // triggered the reload may be the last one, so retry instead of waiting for another change.
    internal static async Task<T> RetryWhileSavingAsync<T>(Func<T> load, CancellationToken cancellationToken, int attempts = 20)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return load(); }
            catch (IOException) when (attempt < attempts) { await Task.Delay(50, cancellationToken); }
        }
    }

    private static IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?> source, params string[] pairs)
    {
        var result = new Dictionary<string, string?>(source, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < pairs.Length; index += 2) result[pairs[index]] = pairs[index + 1];
        return result;
    }
}
