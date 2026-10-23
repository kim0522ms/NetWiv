using System.Diagnostics;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Sql;

internal static class SqlMigrationProcessTests
{
    private const string Database = "sqlite:process.db";
    private const string SeedSql = "CREATE TABLE items (id INTEGER PRIMARY KEY); " +
        "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x + 1 FROM n WHERE x < 120000) " +
        "INSERT INTO items SELECT x FROM n;";

    internal static async Task<int> Probe(string configRoot, string identifier, string role)
    {
        Directory.CreateDirectory(configRoot);
        if (OperatingSystem.IsLinux()) Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configRoot);
        else if (OperatingSystem.IsMacOS()) Environment.SetEnvironmentVariable("HOME", configRoot);

        var migrations = role switch
        {
            "migrate" or "verify" => new[] { new SqlMigration(1, "seed competing process database", SeedSql) },
            "fail" => new[] { new SqlMigration(1, "rollback failed process migration",
                "CREATE TABLE pending (id INTEGER); INSERT INTO absent VALUES (1);") },
            "retry" => new[] { new SqlMigration(1, "retry after process rollback",
                "CREATE TABLE items (id INTEGER PRIMARY KEY); INSERT INTO items VALUES (1);") },
            _ => throw new ArgumentException("Unknown SQLite migration probe role.", nameof(role))
        };

        using var app = new Builder().UseRuntime(new FakeRuntime())
            .Metadata(new AppMetadata("SQLite migration process probe", "1.0.0", identifier))
            .Plugin(new SqlPlugin().AddMigrations(Database, migrations))
            .Capability(new("main", ["sql:allow-load", "sql:allow-select"], Windows: ["main"]))
            .Build();
        var profileDirectory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig);
        if (!IsExpectedProfileDirectory(profileDirectory, configRoot, identifier))
            throw new InvalidOperationException("SQLite migration probe resolved an unexpected app config directory.");
        Console.Out.WriteLine("NETWIV_SQL_APP_CONFIG=" + profileDirectory);
        app.Run();
        if (role == "migrate")
        {
            var ready = Path.Combine(configRoot, "ready-" + Environment.ProcessId);
            await File.WriteAllTextAsync(ready, "ready");
            var start = Path.Combine(configRoot, "start");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(start)) await Task.Delay(10, timeout.Token);
        }
        try
        {
            await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("{\"db\":\"" + Database + "\"}"));
        }
        catch (InvokeException) when (role == "fail") { return 0; }
        if (role == "fail") return 2;
        if (role == "retry")
        {
            var pending = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                "{\"db\":\"" + Database + "\",\"query\":\"SELECT name FROM sqlite_master WHERE type='table' AND name='pending'\"}"));
            if (pending.GetArrayLength() != 0) return 3;
            var rows = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                "{\"db\":\"" + Database + "\",\"query\":\"SELECT COUNT(*) AS count FROM items\"}"));
            return rows[0].GetProperty("count").GetInt32() == 1 ? 0 : 4;
        }
        if (role == "verify")
        {
            var rows = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                "{\"db\":\"" + Database + "\",\"query\":\"SELECT COUNT(*) AS count FROM items\"}"));
            if (rows[0].GetProperty("count").GetInt32() != 120000) return 5;
            var history = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                "{\"db\":\"" + Database + "\",\"query\":\"SELECT COUNT(*) AS count FROM main.__netwiv_migrations WHERE version=1\"}"));
            return history[0].GetProperty("count").GetInt32() == 1 ? 0 : 6;
        }
        return 0;
    }

    internal static ProcessStartInfo StartInfo(string configRoot, string identifier, string role)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var entry = Environment.GetCommandLineArgs()[0];
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(entry);
        start.ArgumentList.Add("--sql-migration-probe");
        start.ArgumentList.Add(configRoot);
        start.ArgumentList.Add(identifier);
        start.ArgumentList.Add(role);
        return start;
    }

    internal static async Task<int[]> RunCompetingProbes(string configRoot, string identifier, Action<string> captureProfileDirectory)
    {
        Process? first = null;
        Process? second = null;
        Task? firstOutput = null;
        Task? secondOutput = null;
        Task<string>? firstError = null;
        Task<string>? secondError = null;
        try
        {
            first = Process.Start(StartInfo(configRoot, identifier, "migrate")) ??
                throw new Exception("Could not start the first SQLite migration process.");
            firstOutput = CaptureProfileDirectory(first.StandardOutput, configRoot, identifier, captureProfileDirectory);
            firstError = first.StandardError.ReadToEndAsync();
            second = Process.Start(StartInfo(configRoot, identifier, "migrate")) ??
                throw new Exception("Could not start the second SQLite migration process.");
            secondOutput = CaptureProfileDirectory(second.StandardOutput, configRoot, identifier, captureProfileDirectory);
            secondError = second.StandardError.ReadToEndAsync();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                while (Directory.GetFiles(configRoot, "ready-*").Length < 2)
                {
                    if (first.HasExited || second.HasExited)
                        throw new Exception("A SQLite migration process exited before the contention gate.");
                    await Task.Delay(10, timeout.Token);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(configRoot, "start"), "start");
            await Task.WhenAll(
                first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)),
                second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            await Task.WhenAll(firstOutput, secondOutput);
            await Task.WhenAll(firstError, secondError);
            return [first.ExitCode, second.ExitCode];
        }
        finally
        {
            if (first is not null)
            {
                Stop(first);
                await first.WaitForExitAsync();
            }
            if (second is not null)
            {
                Stop(second);
                await second.WaitForExitAsync();
            }
            await Task.WhenAll(new[] { firstOutput, secondOutput, firstError, secondError }.OfType<Task>());
            first?.Dispose();
            second?.Dispose();
        }

        static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    internal static async Task<int> RunProbe(string configRoot, string identifier, string role, Action<string> captureProfileDirectory)
    {
        using var process = Process.Start(StartInfo(configRoot, identifier, role)) ??
            throw new Exception("Could not start SQLite migration probe process.");
        var output = CaptureProfileDirectory(process.StandardOutput, configRoot, identifier, captureProfileDirectory);
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await output;
            await error;
            throw new TimeoutException("SQLite migration probe process did not exit.");
        }
        await output;
        await error;
        return process.ExitCode;
    }

    private static async Task CaptureProfileDirectory(StreamReader output, string configRoot, string identifier, Action<string> capture)
    {
        while (await output.ReadLineAsync() is { } line)
        {
            const string prefix = "NETWIV_SQL_APP_CONFIG=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var directory = line[prefix.Length..];
            if (!IsExpectedProfileDirectory(directory, configRoot, identifier))
                throw new InvalidOperationException("SQLite migration probe reported an unexpected app config directory.");
            capture(directory);
        }
    }

    internal static bool IsExpectedProfileDirectory(string directory, string configRoot, string identifier)
    {
        var candidates = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (Path.IsPathFullyQualified(appData)) candidates.Add(Path.Combine(appData, identifier));
        if (OperatingSystem.IsLinux()) candidates.Add(Path.Combine(configRoot, identifier));
        if (OperatingSystem.IsMacOS()) candidates.Add(Path.Combine(configRoot, "Library", "Application Support", identifier));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var actual = Path.GetFullPath(directory);
        return candidates.Any(candidate => string.Equals(actual, Path.GetFullPath(candidate), comparison));
    }
}
