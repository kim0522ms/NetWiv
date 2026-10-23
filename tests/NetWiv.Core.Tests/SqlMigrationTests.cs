using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Sql;

internal static class SqlMigrationTests
{
    private const string Database = "sqlite:migrations.db";
    private static readonly SqlMigration First = new(1, "create items", "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO items (name) VALUES ('first; item'); -- trailing comment");
    private static readonly SqlMigration Second = new(3, "add score", "ALTER TABLE items ADD COLUMN score REAL; UPDATE items SET score = 2.5;");

    internal static Task Registration()
    {
        foreach (var migrations in new SqlMigration[][]
        {
            [new(0, "zero", "SELECT 1")], [new(-1, "negative", "SELECT 1")],
            [First, First], [Second, First], [new(1, "", "SELECT 1")],
            [new(1, "empty", " ")], [new(1, "nul", "SELECT 1\0; SELECT 2")], [null!]
        }) Assert.Throws<ArgumentException>(() => new SqlPlugin().AddMigrations(Database, migrations));
        Assert.Throws<ArgumentException>(() => new SqlPlugin().AddMigrations("postgres:test", [First]));
        var plugin = new SqlPlugin().AddMigrations(Database, [First]);
        Assert.Throws<ArgumentException>(() => plugin.AddMigrations(Database, [Second]));
        using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(plugin).Build();
        Assert.Throws<InvalidOperationException>(() => plugin.AddMigrations("sqlite:other.db", [First]));
        return Task.CompletedTask;
    }

    internal static async Task UpgradeAndReopen()
    {
        using var fixture = new DatabaseFixture();
        var supplied = new[] { First };
        var plugin = new SqlPlugin().AddMigrations(Database, supplied);
        supplied[0] = new(99, "mutated caller array", "SELECT 99");
        using (var app = fixture.Start(plugin))
        {
            await Load(app);
            await Load(app);
            Assert.Equal(1, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
            await Invoke(app, "close");
            await Load(app);
            Assert.Equal("first; item", (await Select(app, "SELECT name FROM items"))[0].GetProperty("name").GetString());
        }
        using (var app = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second])))
        {
            await Load(app);
            Assert.Equal(2.5, (await Select(app, "SELECT score FROM items"))[0].GetProperty("score").GetDouble());
            Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM __netwiv_migrations"))[0].GetProperty("count").GetInt32());
        }
        using (var app = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second])))
        {
            await Load(app);
            Assert.Equal(1, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
        }
    }

    internal static async Task RollbackAndRetry()
    {
        using var fixture = new DatabaseFixture();
        var failing = new SqlMigration(2, "later statement fails", "INSERT INTO items (name) VALUES ('pending'); INSERT INTO prerequisite VALUES (1);");
        using var app = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, failing]));
        await Reject(() => Load(app));
        await Reject(() => Select(app, "SELECT 1"), "not loaded");
        using (var repair = fixture.Start(new SqlPlugin()))
        {
            await Load(repair);
            var tables = await Select(repair, "SELECT name FROM sqlite_master WHERE type = 'table'");
            Assert.Equal(0, tables.GetArrayLength());
            await Execute(repair, "CREATE TABLE prerequisite (id INTEGER)");
        }
        // The same plugin retains its registration after the failed load.
        await Load(app);
        Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
        using var failedUpgrade = fixture.Start(new SqlPlugin().AddMigrations(Database,
            [First, failing, new(4, "failed upgrade", "CREATE TABLE abandoned (id INTEGER); INSERT INTO items (name) VALUES ('upgrade'); INSERT INTO missing VALUES (1);")]));
        await Reject(() => Load(failedUpgrade));
        Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
        Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM main.__netwiv_migrations"))[0].GetProperty("count").GetInt32());
        Assert.Equal(0, (await Select(app, "SELECT name FROM sqlite_master WHERE name='abandoned'")).GetArrayLength());
        Assert.Equal(1, (await Select(app, "SELECT COUNT(*) AS count FROM prerequisite"))[0].GetProperty("count").GetInt32());
        await Invoke(app, "close");
        await Load(app);
        Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
    }

    internal static async Task ChangedHistory()
    {
        using var fixture = new DatabaseFixture();
        using (var initial = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second]))) await Load(initial);
        foreach (var history in new SqlMigration[][]
        {
            [First], [First with { Description = "changed" }, Second],
            [First with { Sql = First.Sql + " SELECT 1;" }, Second],
            [First, new(2, "inserted past migration", "SELECT 2"), Second],
            [First, Second with { Version = 4 }]
        })
        {
            using var app = fixture.Start(new SqlPlugin().AddMigrations(Database, history));
            await Reject(() => Load(app), "history");
        }
        using var verified = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second]));
        await Load(verified);
        Assert.Equal(2.5, (await Select(verified, "SELECT score FROM items"))[0].GetProperty("score").GetDouble());
    }

    internal static async Task TransactionBoundary()
    {
        foreach (var prohibited in new[]
        {
            "COMMIT", "ROLLBACK", "SAVEPOINT other", "PRAGMA user_version=42",
            "ATTACH DATABASE ':memory:' AS other", "DROP TABLE __NETWIV_MIGRATIONS",
            "DELETE FROM __netwiv_migrations", "ALTER TABLE __netwiv_migrations RENAME TO stolen",
            "CREATE TEMP TABLE __netwiv_migrations (version INTEGER)"
        })
        {
            using var fixture = new DatabaseFixture();
            using (var app = fixture.Start(new SqlPlugin().AddMigrations(Database, [new(1, "restricted", "CREATE TABLE pending (id INTEGER); " + prohibited)])))
                await Reject(() => Load(app), "authorized");
            using var inspect = fixture.Start(new SqlPlugin());
            await Load(inspect);
            Assert.Equal(0, (await Select(inspect, "SELECT name FROM sqlite_master WHERE type='table'")).GetArrayLength());
        }
        // SQLite's ON CONFLICT ROLLBACK can itself end the host transaction on failure.
        using var rollbackFixture = new DatabaseFixture();
        using (var app = rollbackFixture.Start(new SqlPlugin().AddMigrations(Database,
                   [new(1, "automatic rollback", "CREATE TABLE items (id INTEGER UNIQUE); INSERT INTO items VALUES (1); INSERT OR ROLLBACK INTO items VALUES (1);")])))
            await Reject(() => Load(app), "UNIQUE");
        using var reopened = rollbackFixture.Start(new SqlPlugin());
        await Load(reopened);
        Assert.Equal(0, (await Select(reopened, "SELECT name FROM sqlite_master WHERE type='table'")).GetArrayLength());
    }

    internal static async Task PreloadAndPermissions()
    {
        using var fixture = new DatabaseFixture();
        var runtime = new FakeRuntime();
        using var app = fixture.Start(new SqlPlugin().AddMigrations("sqlite:preloaded.db", [First]), runtime, preload: true);
        var rows = await GuestIpc.Invoke(runtime, "plugin:sql|select", """{"db":"sqlite:preloaded.db","query":"SELECT name FROM items"}""");
        Assert.Equal("ok", rows.Headers!["Tauri-Response"]);
        Assert.Contains("first; item", GuestIpc.Body(rows));
        foreach (var query in new[] { "DELETE FROM items", "PRAGMA user_version=0", "DELETE FROM __netwiv_migrations", "COMMIT" })
        {
            var response = await GuestIpc.Invoke(runtime, "plugin:sql|select", "{\"db\":\"sqlite:preloaded.db\",\"query\":" + JsonValue.From(query).GetRawText() + "}");
            Assert.Equal("error", response.Headers!["Tauri-Response"]);
        }
        var denied = await GuestIpc.Invoke(runtime, "plugin:sql|execute", """{"db":"sqlite:preloaded.db","query":"DELETE FROM items"}""");
        Assert.Equal("error", denied.Headers!["Tauri-Response"]);
        // Preload and explicit load use the same retained migration definitions.
        await app.Handle.InvokeAsync("plugin:sql|close", JsonValue.Parse("""{"db":"sqlite:preloaded.db"}"""));
        await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:preloaded.db"}"""));
        var count = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse("""{"db":"sqlite:preloaded.db","query":"SELECT COUNT(*) AS count FROM items"}"""));
        Assert.Equal(1, count[0].GetProperty("count").GetInt32());
    }

    internal static async Task ConcurrentLoad()
    {
        using var fixture = new DatabaseFixture();
        using var first = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second]));
        using var second = fixture.Start(new SqlPlugin().AddMigrations(Database, [First, Second]));
        await Task.WhenAll(Load(first), Load(second));
        Assert.Equal(1, (await Select(first, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
        Assert.Equal(2, (await Select(second, "SELECT COUNT(*) AS count FROM __netwiv_migrations"))[0].GetProperty("count").GetInt32());
    }

    internal static async Task ConcurrentProcessLoads()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-sql-process-").FullName;
        var profileDirectories = new List<(string Path, string Identifier)>();
        var profilesGate = new object();
        try
        {
            var identifier = "dev.netwiv.sql-process-" + Guid.NewGuid().ToString("N");
            foreach (var result in await SqlMigrationProcessTests.RunCompetingProbes(root, identifier,
                         path => RecordProfileDirectory(identifier, path)))
                Assert.Equal(0, result);
            var verified = await SqlMigrationProcessTests.RunProbe(root, identifier, "verify",
                path => RecordProfileDirectory(identifier, path));
            Assert.Equal(0, verified);

            var rollbackIdentifier = identifier + "-rollback";
            var failed = await SqlMigrationProcessTests.RunProbe(root, rollbackIdentifier, "fail",
                path => RecordProfileDirectory(rollbackIdentifier, path));
            Assert.Equal(0, failed);
            var retried = await SqlMigrationProcessTests.RunProbe(root, rollbackIdentifier, "retry",
                path => RecordProfileDirectory(rollbackIdentifier, path));
            Assert.Equal(0, retried);
            Assert.Equal(3, profileDirectories.Count(profile => profile.Identifier == identifier));
            Assert.Equal(2, profileDirectories.Count(profile => profile.Identifier == rollbackIdentifier));
        }
        finally
        {
            try
            {
                (string Path, string Identifier)[] paths;
                lock (profilesGate) paths = [.. profileDirectories];
                foreach (var (path, identifier) in paths)
                {
                    if (!SqlMigrationProcessTests.IsExpectedProfileDirectory(path, root, identifier)) continue;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException) { continue; }
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidOperationException("SQLite migration app config directory unexpectedly became a reparse point.");
                    if (!attributes.HasFlag(FileAttributes.Directory))
                        throw new InvalidOperationException("SQLite migration app config path is not a directory.");
                    Directory.Delete(path, true);
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        void RecordProfileDirectory(string identifier, string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!SqlMigrationProcessTests.IsExpectedProfileDirectory(fullPath, root, identifier))
                throw new InvalidOperationException("SQLite migration probe resolved an unexpected app config directory.");
            lock (profilesGate) profileDirectories.Add((fullPath, identifier));
        }
    }

    internal static async Task Cancellation()
    {
        using var fixture = new DatabaseFixture();
        using var app = fixture.Start(new SqlPlugin().AddMigrations(Database,
            [new(1, "long migration", "CREATE TABLE pending (value INTEGER); WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<20000000) INSERT INTO pending SELECT sum(x) FROM n;")]));
        using var stop = new CancellationTokenSource();
        var loading = app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:migrations.db"}"""), stop.Token).AsTask();
        await Task.Delay(50);
        stop.Cancel();
        try { await loading.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("Migration ignored cancellation."); }
        catch (OperationCanceledException) { }
        using var inspect = fixture.Start(new SqlPlugin());
        await Load(inspect);
        Assert.Equal(0, (await Select(inspect, "SELECT name FROM sqlite_master WHERE type='table'")).GetArrayLength());
    }

    internal static async Task PreloadFailure()
    {
        using var fixture = new DatabaseFixture();
        Assert.Throws<InvokeException>(() => fixture.Start(new SqlPlugin().AddMigrations("sqlite:preloaded.db",
            [new(1, "bad preload", "CREATE TABLE pending (id INTEGER); INSERT INTO missing VALUES (1);")]), preload: true));
        using var app = fixture.Start(new SqlPlugin().AddMigrations("sqlite:preloaded.db", [First]), preload: true);
        var rows = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse("""{"db":"sqlite:preloaded.db","query":"SELECT name FROM sqlite_master WHERE type='table' AND name='pending'"}"""));
        Assert.Equal(0, rows.GetArrayLength());
    }

    internal static async Task DatabaseAliases()
    {
        using var fixture = new DatabaseFixture();
        using (var app = fixture.Start(new SqlPlugin().AddMigrations(Database, [First])))
        {
            await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:./migrations.db"}"""));
            Assert.Equal(1, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
            if (OperatingSystem.IsWindows())
            {
                var rows = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse("""{"db":"sqlite:.\\MIGRATIONS.db","query":"SELECT COUNT(*) AS count FROM items"}"""));
                Assert.Equal(1, rows[0].GetProperty("count").GetInt32());
            }
            await app.Handle.InvokeAsync("plugin:sql|close", JsonValue.Parse("""{"db":"sqlite:./migrations.db"}"""));
            await Reject(() => Select(app, "SELECT 1"), "not loaded");
            await Load(app);
            Assert.Equal(1, (await Select(app, "SELECT COUNT(*) AS count FROM items"))[0].GetProperty("count").GetInt32());
        }
        using (var app = fixture.Start(new SqlPlugin().AddMigrations("sqlite:./migrations.db", [First, Second])))
        {
            await Load(app);
            Assert.Equal(2.5, (await Select(app, "SELECT score FROM items"))[0].GetProperty("score").GetDouble());
            // Guest write authority can create temporary tables, but cannot shadow persistent history on reopen.
            await Execute(app, "CREATE TEMP TABLE __netwiv_migrations (version INTEGER)");
            await Invoke(app, "close");
            await Load(app);
            Assert.Equal(2, (await Select(app, "SELECT COUNT(*) AS count FROM main.__netwiv_migrations"))[0].GetProperty("count").GetInt32());
        }
        Assert.Throws<InvokeException>(() => fixture.Start(new SqlPlugin().AddMigrations(Database, [First])
            .AddMigrations("sqlite:./migrations.db", [First])));
    }

    internal static async Task ScriptParsing()
    {
        using var fixture = new DatabaseFixture();
        using var app = fixture.Start(new SqlPlugin().AddMigrations(Database,
        [
            First,
            new(long.MaxValue, "trigger and Unicode", """
                /* ; */ CREATE TABLE audit (message TEXT);
                CREATE TRIGGER audit_name AFTER UPDATE ON items BEGIN
                    INSERT INTO audit VALUES ('첫째; value');
                    INSERT INTO audit VALUES (NEW.name);
                END;
                UPDATE items SET name='updated'; -- ;
                """)
        ]));
        await Load(app);
        var rows = await Select(app, "SELECT message FROM audit ORDER BY rowid");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("첫째; value", rows[0].GetProperty("message").GetString());
        Assert.Equal("updated", rows[1].GetProperty("message").GetString());
        Assert.Equal(long.MaxValue, (await Select(app, "SELECT max(version) AS version FROM main.__netwiv_migrations"))[0].GetProperty("version").GetInt64());
    }

    private static Task<JsonElement> Load(App app) => Invoke(app, "load");
    private static Task<JsonElement> Select(App app, string query) => Invoke(app, "select", query);
    private static Task<JsonElement> Execute(App app, string query) => Invoke(app, "execute", query);
    private static Task<JsonElement> Invoke(App app, string command, string? query = null) => app.Handle.InvokeAsync("plugin:sql|" + command,
        JsonValue.Parse("{\"db\":\"" + Database + "\"" + (query is null ? "}" : ",\"query\":" + JsonValue.From(query).GetRawText() + "}"))).AsTask();

    private static async Task Reject(Func<Task<JsonElement>> operation, string? message = null)
    {
        try { await operation(); }
        catch (InvokeException error) { if (message is not null) Assert.Contains(message, error.Message); return; }
        throw new Exception("Invalid migration operation succeeded.");
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _identifier = "dev.netwiv.sql-migration-" + Guid.NewGuid().ToString("N");
        private string? _directory;
        internal App Start(SqlPlugin plugin, FakeRuntime? runtime = null, bool preload = false)
        {
            var builder = new Builder().UseRuntime(runtime ?? new FakeRuntime()).Metadata(new("SQL migration test", "1.0", _identifier));
            if (preload) builder.UseEmbeddedConfig(resourceName: "plugins-sql.json");
            var app = builder.Plugin(plugin).Capability(new("main", ["sql:default"], Windows: ["main"])).Build();
            _directory = app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig).GetAwaiter().GetResult();
            try { app.Run(); }
            catch { app.Dispose(); throw; }
            return app;
        }
        public void Dispose() { if (_directory is not null && Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
