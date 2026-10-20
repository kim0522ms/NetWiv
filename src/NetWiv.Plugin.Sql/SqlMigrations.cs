using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWiv.Plugin.Sql;

public sealed partial class SqlPlugin
{
    private const string MigrationTable = "__netwiv_migrations";
    private readonly Dictionary<string, SqlMigration[]> _migrationRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SqlMigration[]> _migrations = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _initialized;

    /// <summary>Registers the complete, strictly increasing forward migration history before plugin initialization.</summary>
    public SqlPlugin AddMigrations(string database, IEnumerable<SqlMigration> migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentNullException.ThrowIfNull(migrations);
        if (!database.StartsWith("sqlite:", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(database[7..]) || database.Contains('\0'))
            throw new ArgumentException("A SQLite connection string is required.", nameof(database));
        var snapshot = migrations.ToArray();
        long previous = 0;
        foreach (var migration in snapshot)
        {
            if (migration is null || migration.Version <= previous)
                throw new ArgumentException("Migration versions must be positive and strictly increasing without duplicates.", nameof(migrations));
            if (string.IsNullOrWhiteSpace(migration.Description) || migration.Description.Contains('\0') ||
                string.IsNullOrWhiteSpace(migration.Sql) || migration.Sql.Contains('\0'))
                throw new ArgumentException("Migration descriptions and SQL must be nonempty and contain no NUL characters.", nameof(migrations));
            previous = migration.Version;
        }
        lock (_gate)
        {
            if (_initialized) throw new InvalidOperationException("Register migrations before plugin initialization.");
            if (!_migrationRegistrations.TryAdd(database, snapshot)) throw new ArgumentException("Migrations are already registered for this database.", nameof(database));
        }
        return this;
    }

    private static unsafe void ApplyMigrations(nint db, SqlMigration[] migrations, CancellationToken token)
    {
        // Serialize history checks and writes across connections/processes as well as this plugin instance.
        if (SqliteNative.BusyTimeout(db, 5000) != SqliteNative.Ok) throw new InvokeException(Error(db));
        using var cancellation = token.Register(() => SqliteNative.Interrupt(db));
        token.ThrowIfCancellationRequested();
        ExecuteMigrationSql(db, "BEGIN IMMEDIATE", token);
        try
        {
            ExecuteMigrationSql(db, $"CREATE TABLE IF NOT EXISTS main.{MigrationTable} (version INTEGER PRIMARY KEY, description TEXT NOT NULL, checksum TEXT NOT NULL)", token);
            var statement = PrepareMigrationStatement(db, $"SELECT version, description, checksum FROM main.{MigrationTable} ORDER BY version");
            JsonElement applied;
            try { applied = Select(db, statement, token); }
            finally { SqliteNative.Finalize(statement); }
            if (applied.GetArrayLength() > migrations.Length) throw new InvokeException("The database contains migrations missing from the registered history.");
            for (var index = 0; index < applied.GetArrayLength(); index++)
            {
                var migration = migrations[index];
                var row = applied[index];
                if (row.GetProperty("version").GetInt64() != migration.Version ||
                    row.GetProperty("description").GetString() != migration.Description ||
                    row.GetProperty("checksum").GetString() != Checksum(migration))
                    throw new InvokeException("Applied migration history differs from the registered version, description or SQL.");
            }
            foreach (var migration in migrations.Skip(applied.GetArrayLength()))
            {
                if (SqliteNative.SetAuthorizer(db, &AuthorizeMigration) != SqliteNative.Ok) throw new InvokeException(Error(db));
                try { ExecuteMigrationSql(db, migration.Sql, token); }
                finally { SqliteNative.SetAuthorizer(db, null); }
                statement = PrepareMigrationStatement(db, $"INSERT INTO main.{MigrationTable} (version, description, checksum) VALUES (?1, ?2, ?3)");
                try
                {
                    Bind(db, statement, 1, JsonValue.From(migration.Version));
                    Bind(db, statement, 2, JsonValue.From(migration.Description));
                    Bind(db, statement, 3, JsonValue.From(Checksum(migration)));
                    if (SqliteNative.Step(statement) != SqliteNative.Done) throw new InvokeException(Error(db));
                }
                finally { SqliteNative.Finalize(statement); }
            }
            // Once committing starts, cancellation must not report rollback after a successful commit.
            cancellation.Dispose();
            token.ThrowIfCancellationRequested();
            ExecuteMigrationSql(db, "COMMIT", CancellationToken.None);
        }
        catch
        {
            // Wait for any in-flight interrupt callback before cleanup SQL.
            cancellation.Dispose();
            if (!SqliteNative.IsAutoCommit(db)) ExecuteMigrationSql(db, "ROLLBACK", CancellationToken.None);
            throw;
        }
        finally { SqliteNative.BusyTimeout(db, 0); }
    }

    private static string Checksum(SqlMigration migration) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(migration.Sql)));

    // Scripts run inside the host-owned transaction. They cannot commit early,
    // change connection settings, attach another file or alter their own history.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AuthorizeMigration(nint user, int action, nint first, nint second, nint database, nint trigger) =>
        action is 19 or 22 or 24 or 25 or 32 ||
        string.Equals(Marshal.PtrToStringUTF8(first), MigrationTable, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Marshal.PtrToStringUTF8(second), MigrationTable, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static unsafe nint PrepareMigrationStatement(nint db, string sql)
    {
        var bytes = Encoding.UTF8.GetBytes(sql + "\0");
        fixed (byte* pointer = bytes)
        {
            var result = SqliteNative.Prepare(db, pointer, out var statement);
            if (result == SqliteNative.Ok) return statement;
            if (statement != 0) SqliteNative.Finalize(statement);
            throw new InvokeException(Error(db));
        }
    }

    private static unsafe void ExecuteMigrationSql(nint db, string sql, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(sql + "\0");
        fixed (byte* pointer = bytes)
        {
            var remaining = pointer;
            while (*remaining != 0)
            {
                token.ThrowIfCancellationRequested();
                var result = SqliteNative.PrepareScript(db, remaining, out var statement, out var tail);
                try
                {
                    if (result != SqliteNative.Ok)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvokeException(Error(db));
                    }
                    if (statement != 0)
                    {
                        while ((result = SqliteNative.Step(statement)) == SqliteNative.Row) token.ThrowIfCancellationRequested();
                        token.ThrowIfCancellationRequested();
                        if (result != SqliteNative.Done) throw new InvokeException(Error(db));
                    }
                }
                finally { if (statement != 0) SqliteNative.Finalize(statement); }
                remaining = tail;
            }
        }
    }
}
