using System.Diagnostics;
using NetWiv.Cli;

internal static class DevReloadTests
{
    internal static async Task TransientFiles()
    {
        var root = Path.GetTempPath();
        var temporary = Path.Combine(root, "$null");
        var source = Path.Combine(root, "Program.cs");
        using var watcher = new DevCommand.ChangeWatcher([]);
        watcher.OnChanged(root, temporary, WatcherChangeTypes.Created);
        watcher.OnChanged(root, temporary, WatcherChangeTypes.Changed);
        watcher.OnChanged(root, temporary, WatcherChangeTypes.Deleted);
        watcher.OnChanged(root, root, WatcherChangeTypes.Changed);
        using (var timeout = new CancellationTokenSource(100))
        {
            try { await watcher.WaitAsync(timeout.Token); throw new Exception("A discarded build temporary triggered restart."); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
        foreach (var kind in new[] { WatcherChangeTypes.Created, WatcherChangeTypes.Changed, WatcherChangeTypes.Deleted })
        {
            watcher.OnChanged(root, source, kind);
            await watcher.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        }
        watcher.OnChanged(root, source, WatcherChangeTypes.Deleted);
        watcher.OnChanged(root, source, WatcherChangeTypes.Created);
        await watcher.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    internal static async Task SavingRetry()
    {
        var attempts = 0;
        var loaded = await DevCommand.RetryWhileSavingAsync(
            () => ++attempts < 3 ? throw new IOException("in use") : "loaded", CancellationToken.None);
        if (loaded != "loaded" || attempts != 3) throw new Exception("A briefly held configuration file was not retried.");

        attempts = 0;
        try
        {
            await DevCommand.RetryWhileSavingAsync<string>(() => { attempts++; throw new IOException("still in use"); }, CancellationToken.None, attempts: 4);
            throw new Exception("A persistently held configuration file was reported as loaded.");
        }
        catch (IOException) when (attempts == 4) { }

        attempts = 0;
        try
        {
            await DevCommand.RetryWhileSavingAsync<string>(() => { attempts++; throw new UnauthorizedAccessException(); }, CancellationToken.None);
            throw new Exception("A permission failure was swallowed.");
        }
        catch (UnauthorizedAccessException) when (attempts == 1) { }
    }

    internal static async Task NoWatchFeatures()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-dev-features-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings><NetWivFeatures>project-default</NetWivFeatures>
                  </PropertyGroup>
                  <ItemGroup><AssemblyMetadata Include="Features" Value="$(NetWivFeatures)" /></ItemGroup>
                </Project>
                """.Replace("net8.0", $"net{Environment.Version.Major}.0", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using System.Reflection;
                var value = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "Features").Value;
                File.WriteAllText(args[0], value ?? "");
                """);
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """
                {"identifier":"test.netwiv.features","build":{"dotnetProject":"Probe.csproj"}}
                """);
            var marker = Path.Combine(root, "features.txt");
            var runner = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            foreach (var features in new[] { "", "alpha,beta", "invalid!" })
            {
                File.Delete(marker);
                using var error = new StringWriter();
                var code = await CliApp.RunAsync(["dev", "--no-watch", "--no-dev-server", "--runner", runner,
                    "--features", features, "--", marker], TextWriter.Null, error, root);
                if (features == "invalid!")
                {
                    if (code == 0 || File.Exists(marker) || !error.ToString().Contains("Invalid feature name"))
                        throw new Exception("Invalid features started an application.");
                }
                else if (code != 0 || !File.Exists(marker) || File.ReadAllText(marker) != features.Replace(',', ';'))
                    throw new Exception("Managed run did not override the project's default features: " + error);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task Run(bool aot)
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-dev-reload-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app");
        var permissions = Path.Combine(app, "permissions");
        Directory.CreateDirectory(permissions);
        var outputFile = Path.Combine(root, "runs.txt");
        var overlay = Path.Combine(root, "overlay.json");
        File.WriteAllText(overlay, "{\"productName\":\"first\"}");
        File.WriteAllText(Path.Combine(app, "Probe.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
                <NetWivFeatures>project-default</NetWivFeatures>
                <ImplicitUsings>enable</ImplicitUsings><DefaultItemExcludes>$(DefaultItemExcludes);target/**</DefaultItemExcludes>
              </PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="$(NetWivConfigPath)" LogicalName="config" />
                <AssemblyMetadata Include="Features" Value="$(NetWivFeatures)" />
              </ItemGroup>
            </Project>
            """.Replace("net8.0", $"net{Environment.Version.Major}.0", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(app, "Program.cs"), """
            using System.Reflection;
            using System.Text.Json;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("config")!;
            using var document = JsonDocument.Parse(stream);
            var config = document.RootElement;
            var features = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "Features").Value;
            var expected = config.GetProperty("productName").GetString() == "second" ? "alpha;beta" : "";
            if (features != expected) throw new Exception($"Incorrect MSBuild features: '{features}', expected '{expected}'");
            using var patch = JsonDocument.Parse(Environment.GetEnvironmentVariable("TAURI_CONFIG") ?? throw new Exception("Missing CLI patch environment"));
            if (patch.RootElement.GetProperty("productName").GetString() != config.GetProperty("productName").GetString())
                throw new Exception("Reloaded configuration and TAURI_CONFIG environment disagree");
            var command = config.GetProperty("$netwivAcl").GetProperty("files")[0].GetProperty("permission")[0].GetProperty("commands").GetProperty("allow")[0].GetString();
            File.AppendAllText(args[0], $"{Environment.ProcessId}|{config.GetProperty("productName").GetString()}|{command}\n");
            await Task.Delay(Timeout.Infinite);
            """);
        File.WriteAllText(Path.Combine(app, "netwiv.conf.json"), """
            {"identifier":"test.netwiv.reload","build":{"dotnetProject":"Probe.csproj"},
             "app":{"security":{"capabilities":[{"identifier":"main","windows":["main"],"permissions":["access"]}]}}}
            """);
        var permission = Path.Combine(permissions, "access.toml");
        void WritePermission(string command) => File.WriteAllText(permission,
            $"[[permission]]\nidentifier = \"access\"\ncommands.allow = [\"{command}\"]\n");
        // Like an editor that keeps the file open after saving; Windows then rejects the watcher's read.
        async Task WritePermissionHeldOpen(string command)
        {
            await using var editor = new FileStream(permission, FileMode.Create, FileAccess.Write, FileShare.None);
            await editor.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"[[permission]]\nidentifier = \"access\"\ncommands.allow = [\"{command}\"]\n"));
            await editor.FlushAsync();
            // Longer than the reload's immediate retries, so the periodic retry must pick the release up.
            await Task.Delay(2500);
        }
        WritePermission("one");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (OperatingSystem.IsLinux() && File.Exists("/opt/dotnet/dotnet") ? "/opt/dotnet/dotnet" : "dotnet");
        var arguments = new List<string> { "--no-dev-server", "--runner", dotnet, "--config", overlay };
        if (aot) arguments.Add("--aot");
        arguments.AddRange(["--", outputFile]);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(210));
        var output = new StringWriter();
        var session = DevCommand.RunAsync(arguments.ToArray(), TextWriter.Synchronized(output), app, stop.Token);
        var pids = new List<int>();
        var completed = false;
        try
        {
            pids.Add(await WaitForRun("first|one"));
            await WritePermissionHeldOpen("two");
            pids.Add(await WaitForRun("first|two"));
            await RequireStopped(pids[0]);
            File.WriteAllText(overlay, "{\"productName\":\"second\",\"build\":{\"features\":[\"alpha\",\"beta\"]}}");
            pids.Add(await WaitForRun("second|two"));
            await RequireStopped(pids[1]);
            File.WriteAllText(permission, "[[permission]]\nidentifier = ");
            await Until(() => output.ToString().Contains("Configuration error:", StringComparison.Ordinal));
            await RequireStopped(pids[2]);
            WritePermission("three");
            pids.Add(await WaitForRun("second|three"));
            var runCount = ConcurrentFile.ReadCompleteLines(outputFile).Length;
            await Task.Delay(1000, stop.Token);
            if (ConcurrentFile.ReadCompleteLines(outputFile).Length != runCount)
                throw new Exception("Unrelated output beside the overlay triggered another restart.");
            var externalResources = Path.Combine(root, "external-resources");
            Directory.CreateDirectory(externalResources);
            File.WriteAllText(Path.Combine(externalResources, "initial.txt"), "initial");
            File.WriteAllText(overlay, """{"productName":"second","build":{"features":["alpha","beta"]},"bundle":{"resources":{"../external-resources/*.txt":"data"}}}""");
            await Until(() => ConcurrentFile.ReadCompleteLines(outputFile).Length > runCount);
            pids.Add(int.Parse(ConcurrentFile.ReadCompleteLines(outputFile)[^1].Split('|')[0]));
            await RequireStopped(pids[^2]);
            runCount = ConcurrentFile.ReadCompleteLines(outputFile).Length;
            File.WriteAllText(Path.Combine(externalResources, "added.txt"), "new glob match");
            await Until(() => ConcurrentFile.ReadCompleteLines(outputFile).Length > runCount);
            pids.Add(int.Parse(ConcurrentFile.ReadCompleteLines(outputFile)[^1].Split('|')[0]));
            await RequireStopped(pids[^2]);
            completed = true;
        }
        finally
        {
            stop.Cancel();
            try { await session; } catch (OperationCanceledException) { }
            foreach (var pid in pids) await RequireStopped(pid);
            if (completed)
            {
                // All app PIDs have already exited. Windows can briefly retain an
                // image mapping while the loader finishes releasing a killed job.
                for (var attempt = 0; ; attempt++)
                {
                    try { Directory.Delete(root, recursive: true); break; }
                    catch (Exception error) when (attempt < 20 && error is IOException or UnauthorizedAccessException)
                    { await Task.Delay(100); }
                }
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(root, "dev-session.log"), output.ToString());
                Console.WriteLine("Failed development evidence retained: " + root);
            }
        }

        async Task<int> WaitForRun(string expected)
        {
            string? match = null;
            await Until(() =>
            {
                match = ConcurrentFile.ReadCompleteLines(outputFile).LastOrDefault(line => line.EndsWith("|" + expected, StringComparison.Ordinal));
                return match is not null;
            });
            var matched = match ?? throw new Exception("No matching development run.");
            return int.Parse(matched[..matched.IndexOf('|')]);
        }
        async Task Until(Func<bool> predicate)
        {
            while (!predicate())
            {
                if (session.IsCompleted) { await session; throw new Exception("Development session exited early: " + output); }
                await Task.Delay(50, stop.Token);
            }
        }
    }

    private static async Task RequireStopped(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { throw new Exception($"Development child {pid} survived restart/cancellation."); }
        }
        catch (ArgumentException) { }
    }
}
