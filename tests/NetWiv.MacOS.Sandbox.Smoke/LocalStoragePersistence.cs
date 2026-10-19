using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class LocalStoragePersistence
{
    internal static async Task WaitAsync(string directory, string value, bool present, CancellationToken cancellationToken)
    {
        // Observe committed state through a separate, read-only SQLite connection.
        // A same-document getItem does not establish that WebKit has committed it.
        while (ContainsValue(directory, value) != present)
            await Task.Delay(25, cancellationToken);
    }

    internal static bool? ContainsValue(string directory, string value)
    {
        if (!Directory.Exists(directory)) return null;
        string hex = Convert.ToHexString(Encoding.Unicode.GetBytes(value));
        bool? result = null;
        foreach (var file in Directory.EnumerateFiles(directory, "localstorage.sqlite3", SearchOption.AllDirectories))
        {
            var found = DatabaseContainsValue(file, hex);
            if (found is true) return true;
            if (found is false) result = false;
        }
        return result;
    }

    private static unsafe bool? DatabaseContainsValue(string file, string hex)
    {
        int status = Open(file, out var database, 1 /* SQLITE_OPEN_READONLY */, 0);
        try
        {
            if (status != 0) return null;
            bool found = false;
            string query = $"SELECT 1 FROM ItemTable WHERE key = 'netwiv-sandbox-fixture' AND value = X'{hex}'";
            return Execute(database, query, &Found, (nint)(&found), 0) == 0 ? found : null;
        }
        finally { if (database != 0) Close(database); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Found(nint state, int columns, nint values, nint names)
    {
        *(bool*)state = true;
        return 0;
    }

    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string filename, out nint database, int flags, nint vfs);
    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_exec", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int Execute(nint database, string sql,
        delegate* unmanaged[Cdecl]<nint, int, nint, nint, int> callback, nint state, nint error);
    [LibraryImport("/usr/lib/libsqlite3.dylib", EntryPoint = "sqlite3_close")]
    private static partial int Close(nint database);
}
