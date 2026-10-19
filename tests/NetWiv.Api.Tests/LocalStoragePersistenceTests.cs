using System.Runtime.InteropServices;
using Xunit;

namespace NetWiv.Api.Tests;

public sealed partial class LocalStoragePersistenceTests
{
    [MacOsFact]
    public async Task WaitsForTheCurrentValueToCommitOnAnIndependentConnection()
    {
        using var fixture = new Database();
        Assert.Null(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));
        fixture.Execute("CREATE TABLE ItemTable (key TEXT UNIQUE, value BLOB)");
        fixture.Execute("INSERT INTO ItemTable VALUES ('netwiv-sandbox-fixture', X'6F006C006400')");
        Assert.False(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));
        fixture.Execute("BEGIN IMMEDIATE");
        fixture.Execute("UPDATE ItemTable SET value = X'630075007200720065006E007400'");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = LocalStoragePersistence.WaitAsync(fixture.Directory, "current", true, deadline.Token);
        Assert.False(pending.IsCompleted);
        fixture.Execute("COMMIT");
        await pending;
        Assert.True(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));

        fixture.Execute("BEGIN EXCLUSIVE");
        Assert.Null(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));
        pending = LocalStoragePersistence.WaitAsync(fixture.Directory, "current", false, deadline.Token);
        Assert.False(pending.IsCompleted);
        fixture.Execute("ROLLBACK");
        fixture.Execute("BEGIN IMMEDIATE");
        fixture.Execute("DELETE FROM ItemTable");
        Assert.False(pending.IsCompleted);
        fixture.Execute("COMMIT");
        await pending;
        Assert.False(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));
    }

    [MacOsFact]
    public async Task MissingStorageCannotSatisfyCommitAndCancellationEndsTheWait()
    {
        using var fixture = new Database();
        var missing = Path.Combine(fixture.Directory, "missing");
        Assert.Null(LocalStoragePersistence.ContainsValue(missing, "current"));
        using var cancellation = new CancellationTokenSource();
        var pending = LocalStoragePersistence.WaitAsync(missing, "current", true, cancellation.Token);
        var removal = LocalStoragePersistence.WaitAsync(missing, "current", false, cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.False(removal.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => removal);
    }

    [MacOsFact]
    public void ReaderDoesNotCreateOrModifyStorageWhenItCannotOpenTheDatabase()
    {
        using var fixture = new Database();
        var inaccessible = Path.Combine(fixture.Directory, "nested");
        System.IO.Directory.CreateDirectory(inaccessible);
        var missing = Path.Combine(inaccessible, "missing.sqlite3");
        File.CreateSymbolicLink(Path.Combine(inaccessible, "localstorage.sqlite3"), missing);
        Assert.Null(LocalStoragePersistence.ContainsValue(fixture.Directory, "current"));
        Assert.False(File.Exists(missing));
    }

    private sealed class Database : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "netwiv-storage-" + Guid.NewGuid().ToString("N"));
        private readonly nint _database;

        internal Database()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Assert.Equal(0, Open(Path.Combine(Directory, "localstorage.sqlite3"), out _database, 6, 0));
        }

        internal void Execute(string sql) => Assert.Equal(0, Exec(_database, sql, 0, 0, 0));

        public void Dispose()
        {
            Close(_database);
            System.IO.Directory.Delete(Directory, true);
        }
    }

    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string filename, out nint database, int flags, nint vfs);
    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_exec", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Exec(nint database, string sql, nint callback, nint state, nint error);
    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_close")]
    private static partial int Close(nint database);
}

internal sealed class MacOsFactAttribute : FactAttribute
{
    public MacOsFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires the macOS system SQLite library.";
    }
}
