using System.Runtime.InteropServices;

namespace NetWiv.Plugin.Sql;

internal static unsafe partial class SqliteNative
{
    internal const int Ok = 0, Row = 100, Done = 101;
    internal const int Integer = 1, Float = 2, Text = 3, Blob = 4;
    internal const int ReadWriteCreate = 0x00000002 | 0x00000004 | 0x00010000;
    internal static readonly nint Transient = new(-1);

    internal static int Open(byte* path, out nint db) => OperatingSystem.IsWindows()
        ? Windows.Open(path, out db) : Linux.Open(path, out db);
    internal static int Close(nint db) => OperatingSystem.IsWindows() ? Windows.Close(db) : Linux.Close(db);
    internal static void Interrupt(nint db) { if (OperatingSystem.IsWindows()) Windows.Interrupt(db); else Linux.Interrupt(db); }
    internal static int SetAuthorizer(nint db, delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, nint, int> callback) =>
        OperatingSystem.IsWindows() ? Windows.SetAuthorizer(db, callback, 0) : Linux.SetAuthorizer(db, callback, 0);
    internal static int Prepare(nint db, byte* query, out nint statement) => OperatingSystem.IsWindows()
        ? Windows.Prepare(db, query, -1, out statement, 0) : Linux.Prepare(db, query, -1, out statement, 0);
    internal static int PrepareScript(nint db, byte* query, out nint statement, out byte* tail) => OperatingSystem.IsWindows()
        ? Windows.PrepareScript(db, query, -1, out statement, out tail) : Linux.PrepareScript(db, query, -1, out statement, out tail);
    internal static int BusyTimeout(nint db, int milliseconds) => OperatingSystem.IsWindows()
        ? Windows.BusyTimeout(db, milliseconds) : Linux.BusyTimeout(db, milliseconds);
    internal static bool IsAutoCommit(nint db) => (OperatingSystem.IsWindows() ? Windows.GetAutoCommit(db) : Linux.GetAutoCommit(db)) != 0;
    internal static int Finalize(nint statement) => OperatingSystem.IsWindows() ? Windows.Finalize(statement) : Linux.Finalize(statement);
    internal static int Step(nint statement) => OperatingSystem.IsWindows() ? Windows.Step(statement) : Linux.Step(statement);
    internal static int BindNull(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.BindNull(statement, index) : Linux.BindNull(statement, index);
    internal static int BindInt64(nint statement, int index, long value) => OperatingSystem.IsWindows() ? Windows.BindInt64(statement, index, value) : Linux.BindInt64(statement, index, value);
    internal static int BindDouble(nint statement, int index, double value) => OperatingSystem.IsWindows() ? Windows.BindDouble(statement, index, value) : Linux.BindDouble(statement, index, value);
    internal static int BindText(nint statement, int index, byte* value, int length) => OperatingSystem.IsWindows()
        ? Windows.BindText(statement, index, value, length, Transient) : Linux.BindText(statement, index, value, length, Transient);
    internal static nint Error(nint db) => OperatingSystem.IsWindows() ? Windows.Error(db) : Linux.Error(db);
    internal static int Changes(nint db) => OperatingSystem.IsWindows() ? Windows.Changes(db) : Linux.Changes(db);
    internal static long LastInsertId(nint db) => OperatingSystem.IsWindows() ? Windows.LastInsertId(db) : Linux.LastInsertId(db);
    internal static int ColumnCount(nint statement) => OperatingSystem.IsWindows() ? Windows.ColumnCount(statement) : Linux.ColumnCount(statement);
    internal static nint ColumnName(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnName(statement, index) : Linux.ColumnName(statement, index);
    internal static int ColumnType(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnType(statement, index) : Linux.ColumnType(statement, index);
    internal static long ColumnInt64(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnInt64(statement, index) : Linux.ColumnInt64(statement, index);
    internal static double ColumnDouble(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnDouble(statement, index) : Linux.ColumnDouble(statement, index);
    internal static nint ColumnText(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnText(statement, index) : Linux.ColumnText(statement, index);
    internal static nint ColumnBlob(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnBlob(statement, index) : Linux.ColumnBlob(statement, index);
    internal static int ColumnBytes(nint statement, int index) => OperatingSystem.IsWindows() ? Windows.ColumnBytes(statement, index) : Linux.ColumnBytes(statement, index);

    private static partial class Windows
    {
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_get_autocommit")] internal static partial int GetAutoCommit(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_busy_timeout")] internal static partial int BusyTimeout(nint db, int milliseconds);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_prepare_v2")] internal static partial int PrepareScript(nint db, byte* query, int length, out nint statement, out byte* tail);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_interrupt")] internal static partial void Interrupt(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_set_authorizer")] internal static partial int SetAuthorizer(nint db, delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, nint, int> callback, nint user);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_open_v2")] internal static partial int Open(byte* path, out nint db, int flags = ReadWriteCreate, nint vfs = 0);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_close_v2")] internal static partial int Close(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_prepare_v2")] internal static partial int Prepare(nint db, byte* query, int length, out nint statement, nint tail);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_finalize")] internal static partial int Finalize(nint statement);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_step")] internal static partial int Step(nint statement);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_bind_null")] internal static partial int BindNull(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_bind_int64")] internal static partial int BindInt64(nint statement, int index, long value);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_bind_double")] internal static partial int BindDouble(nint statement, int index, double value);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_bind_text")] internal static partial int BindText(nint statement, int index, byte* value, int length, nint destructor);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_errmsg")] internal static partial nint Error(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_changes")] internal static partial int Changes(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_last_insert_rowid")] internal static partial long LastInsertId(nint db);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_count")] internal static partial int ColumnCount(nint statement);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_name")] internal static partial nint ColumnName(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_type")] internal static partial int ColumnType(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_int64")] internal static partial long ColumnInt64(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_double")] internal static partial double ColumnDouble(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_text")] internal static partial nint ColumnText(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_blob")] internal static partial nint ColumnBlob(nint statement, int index);
        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_bytes")] internal static partial int ColumnBytes(nint statement, int index);
    }

    private static partial class Linux
    {
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_get_autocommit")] internal static partial int GetAutoCommit(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_busy_timeout")] internal static partial int BusyTimeout(nint db, int milliseconds);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_prepare_v2")] internal static partial int PrepareScript(nint db, byte* query, int length, out nint statement, out byte* tail);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_interrupt")] internal static partial void Interrupt(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_set_authorizer")] internal static partial int SetAuthorizer(nint db, delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint, nint, int> callback, nint user);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_open_v2")] internal static partial int Open(byte* path, out nint db, int flags = ReadWriteCreate, nint vfs = 0);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_close_v2")] internal static partial int Close(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_prepare_v2")] internal static partial int Prepare(nint db, byte* query, int length, out nint statement, nint tail);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_finalize")] internal static partial int Finalize(nint statement);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_step")] internal static partial int Step(nint statement);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_bind_null")] internal static partial int BindNull(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_bind_int64")] internal static partial int BindInt64(nint statement, int index, long value);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_bind_double")] internal static partial int BindDouble(nint statement, int index, double value);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_bind_text")] internal static partial int BindText(nint statement, int index, byte* value, int length, nint destructor);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_errmsg")] internal static partial nint Error(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_changes")] internal static partial int Changes(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_last_insert_rowid")] internal static partial long LastInsertId(nint db);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_count")] internal static partial int ColumnCount(nint statement);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_name")] internal static partial nint ColumnName(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_type")] internal static partial int ColumnType(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_int64")] internal static partial long ColumnInt64(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_double")] internal static partial double ColumnDouble(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_text")] internal static partial nint ColumnText(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_blob")] internal static partial nint ColumnBlob(nint statement, int index);
        [LibraryImport("sqlite3", EntryPoint = "sqlite3_column_bytes")] internal static partial int ColumnBytes(nint statement, int index);
    }
}
