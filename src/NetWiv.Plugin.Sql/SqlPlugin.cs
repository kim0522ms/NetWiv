using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NetWiv.Plugin.Sql;

/// <summary>SQLite implementation of the official SQL guest commands.</summary>
public sealed partial class SqlPlugin : IPlugin
{
    private readonly object _gate = new();
    private readonly Dictionary<string, nint> _databases = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string? _directory;
    private string[] _preload = [];

    public string Name => "sql";

    public void Initialize(PluginContext context)
    {
        lock (_gate) _initialized = true;
        if (context.Config.ValueKind == JsonValueKind.Object && context.Config.TryGetProperty("preload", out var preload))
        {
            if (preload.ValueKind != JsonValueKind.Array) throw new InvokeException("SQL preload must be an array.");
            _preload = preload.EnumerateArray().Select(item => item.GetString() ?? throw new InvokeException("SQL preload must contain strings.")).ToArray();
        }
        context.RegisterCommand("load", (_, args, token) => Run(() => Load(args.GetProperty("db").GetString()!, token), token));
        context.RegisterCommand("execute", (_, args, token) => Run(() => Query(args, select: false, token), token));
        context.RegisterCommand("select", (_, args, token) => Run(() => Query(args, select: true, token), token));
        context.RegisterCommand("close", (_, args, token) => Run(() => Close(args), token));
        foreach (var command in new[] { "load", "execute", "select", "close" })
        {
            context.Permission(new("allow-" + command, [command], []));
            context.Permission(new("deny-" + command, [], [command]));
        }
        context.PermissionSet("default", "allow-load", "allow-select", "allow-close");
    }

    public void Setup(AppHandle app)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _directory = Path.Combine(config, app.Metadata.Identifier);
        foreach (var (database, migrations) in _migrationRegistrations)
            if (!_migrations.TryAdd(DatabasePath(database), migrations))
                throw new InvokeException("Migration registrations resolve to the same SQLite database.");
        foreach (var db in _preload) Load(db);
    }

    private static async ValueTask<JsonElement> Run(Func<JsonElement> operation, CancellationToken token) =>
        await Task.Run(operation, token);

    private unsafe JsonElement Load(string db, CancellationToken token = default)
    {
        lock (_gate)
        {
            var path = DatabasePath(db);
            if (_databases.ContainsKey(path)) return JsonValue.From(db);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* pointer = bytes)
            {
                var result = SqliteNative.Open(pointer, out var handle);
                if (result != SqliteNative.Ok)
                {
                    var error = handle != 0 ? Error(handle) : $"SQLite open error {result}";
                    if (handle != 0) SqliteNative.Close(handle);
                    throw new InvokeException(error);
                }
                try
                {
                    if (_migrations.TryGetValue(path, out var migrations)) ApplyMigrations(handle, migrations, token);
                    _databases.Add(path, handle);
                }
                catch
                {
                    SqliteNative.Close(handle);
                    throw;
                }
            }
            return JsonValue.From(db);
        }
    }

    private string DatabasePath(string db)
    {
        if (_directory is null) throw new InvokeException("SQL plugin has not started.");
        if (!db.StartsWith("sqlite:", StringComparison.Ordinal))
            throw new InvokeException("Only SQLite connection strings are supported.");
        var name = db["sqlite:".Length..];
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains('\0'))
            throw new InvokeException("SQLite database name must be relative to the app config directory.");
        var root = Path.GetFullPath(_directory);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvokeException("SQLite database path escapes the app config directory.");
        return path;
    }

    private unsafe JsonElement Query(JsonElement args, bool select, CancellationToken token)
    {
        var db = args.GetProperty("db").GetString() ?? throw new InvokeException("Database name is required.");
        var query = args.GetProperty("query").GetString() ?? throw new InvokeException("SQL query is required.");
        var values = Array.Empty<JsonElement>();
        if (args.TryGetProperty("values", out var supplied) && supplied.ValueKind != JsonValueKind.Null)
        {
            if (supplied.ValueKind != JsonValueKind.Array) throw new InvokeException("SQL bind values must be an array.");
            values = supplied.EnumerateArray().ToArray();
        }
        lock (_gate)
        {
            if (!_databases.TryGetValue(DatabasePath(db), out var handle)) throw new InvokeException($"Database '{db}' is not loaded.");
            using var cancellation = token.Register(() => SqliteNative.Interrupt(handle));
            token.ThrowIfCancellationRequested();
            var bytes = Encoding.UTF8.GetBytes(query + "\0");
            fixed (byte* pointer = bytes)
            {
                if (select && SqliteNative.SetAuthorizer(handle, &AuthorizeSelect) != SqliteNative.Ok) throw new InvokeException(Error(handle));
                nint statement = 0;
                try
                {
                    var status = SqliteNative.Prepare(handle, pointer, out statement);
                    if (status != SqliteNative.Ok)
                    {
                        // Cancellation interrupts the connection, which can also abort preparation.
                        token.ThrowIfCancellationRequested();
                        throw new InvokeException(Error(handle));
                    }
                    if (statement == 0) throw new InvokeException("SQL query is empty.");
                    for (var index = 0; index < values.Length; index++) Bind(handle, statement, index + 1, values[index]);
                    token.ThrowIfCancellationRequested();
                    if (select) return Select(handle, statement, token);
                    while ((status = SqliteNative.Step(statement)) == SqliteNative.Row) token.ThrowIfCancellationRequested();
                    token.ThrowIfCancellationRequested();
                    if (status != SqliteNative.Done) throw new InvokeException(Error(handle));
                    return JsonValue.Parse($"[{SqliteNative.Changes(handle)},{SqliteNative.LastInsertId(handle)}]");
                }
                finally
                {
                    if (statement != 0) SqliteNative.Finalize(statement);
                    if (select) SqliteNative.SetAuthorizer(handle, null);
                }
            }
        }
    }

    // SQLite authorizer actions: READ, SELECT, FUNCTION, RECURSIVE. Deny writes,
    // PRAGMAs and ATTACH at preparation time, before they can have side effects.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AuthorizeSelect(nint user, int action, nint first, nint second, nint database, nint trigger) =>
        action is 20 or 21 or 31 or 33 ? 0 : 1;

    private static unsafe void Bind(nint db, nint statement, int index, JsonElement value)
    {
        int result;
        if (value.ValueKind == JsonValueKind.Null) result = SqliteNative.BindNull(statement, index);
        else if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer)) result = SqliteNative.BindInt64(statement, index, integer);
            else if (value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0)
                throw new InvokeException("SQLite integer parameters must fit a signed 64-bit integer.");
            else result = SqliteNative.BindDouble(statement, index, value.GetDouble());
        }
        else
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* pointer = bytes) result = SqliteNative.BindText(statement, index, pointer, bytes.Length);
        }
        if (result != SqliteNative.Ok) throw new InvokeException(Error(db));
    }

    private static JsonElement Select(nint db, nint statement, CancellationToken token)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            int status;
            while ((status = SqliteNative.Step(statement)) == SqliteNative.Row)
            {
                token.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                for (var index = 0; index < SqliteNative.ColumnCount(statement); index++)
                {
                    writer.WritePropertyName(Marshal.PtrToStringUTF8(SqliteNative.ColumnName(statement, index)) ?? string.Empty);
                    switch (SqliteNative.ColumnType(statement, index))
                    {
                        case SqliteNative.Integer: writer.WriteNumberValue(SqliteNative.ColumnInt64(statement, index)); break;
                        case SqliteNative.Float: writer.WriteNumberValue(SqliteNative.ColumnDouble(statement, index)); break;
                        case SqliteNative.Text:
                            writer.WriteStringValue(Marshal.PtrToStringUTF8(SqliteNative.ColumnText(statement, index), SqliteNative.ColumnBytes(statement, index)));
                            break;
                        case SqliteNative.Blob:
                            var size = SqliteNative.ColumnBytes(statement, index);
                            var bytes = new byte[size];
                            if (size > 0) Marshal.Copy(SqliteNative.ColumnBlob(statement, index), bytes, 0, size);
                            writer.WriteStartArray(); foreach (var value in bytes) writer.WriteNumberValue(value); writer.WriteEndArray();
                            break;
                        default: writer.WriteNullValue(); break;
                    }
                }
                writer.WriteEndObject();
            }
            token.ThrowIfCancellationRequested();
            if (status != SqliteNative.Done) throw new InvokeException(Error(db));
            writer.WriteEndArray();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private JsonElement Close(JsonElement args)
    {
        string? named = null;
        if (args.TryGetProperty("db", out var db) && db.ValueKind != JsonValueKind.Null)
        {
            if (db.ValueKind != JsonValueKind.String) throw new InvokeException("Database name must be a string.");
            named = db.GetString();
        }
        lock (_gate)
        {
            if (named is not null)
            {
                if (!_databases.Remove(DatabasePath(named), out var handle)) throw new InvokeException($"Database '{named}' is not loaded.");
                SqliteNative.Close(handle);
            }
            else
            {
                foreach (var handle in _databases.Values) SqliteNative.Close(handle);
                _databases.Clear();
            }
        }
        return JsonValue.From(true);
    }

    private static string Error(nint db) => Marshal.PtrToStringUTF8(SqliteNative.Error(db)) ?? "SQLite error";

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var handle in _databases.Values) SqliteNative.Close(handle);
            _databases.Clear();
        }
    }
}
