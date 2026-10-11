using System.Globalization;
using System.Text;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Plugin.Fs;

/// <summary>Desktop implementation of the official @tauri-apps/plugin-fs wire commands.</summary>
public sealed class FsPlugin : IPlugin
{
    private FileScope? _selectedPaths;
    private static readonly string[] Commands =
    [
        "copy_file", "create", "exists", "fstat", "ftruncate", "lstat", "mkdir", "open",
        "read", "read_dir", "read_file", "read_text_file", "read_text_file_lines",
        "read_text_file_lines_next", "remove", "rename", "seek", "size", "stat", "truncate",
        "write", "write_file", "write_text_file", "start_accessing_security_scoped_resource",
        "stop_accessing_security_scoped_resource"
    ];

    private static readonly string[] DefaultDirectories = ["$APPCONFIG", "$APPDATA", "$APPLOCALDATA", "$APPCACHE", "$APPLOG"];

    public string Name => "fs";

    public void Setup(AppHandle app) => _selectedPaths = new FileScope(JsonValue.Parse("""{"allow":[],"deny":[]}"""), app.Metadata.Identifier);

    /// <summary>Grants a path chosen through a native file dialog for this app session.</summary>
    public void GrantSelectedPath(string path, bool directory = false, bool recursive = false)
    {
        var scope = _selectedPaths ?? throw new InvalidOperationException("The fs plugin has not started.");
        if (directory) scope.AllowDirectory(path, recursive);
        else scope.AllowFile(path);
    }

    public void Initialize(PluginContext context)
    {
        foreach (var command in Commands)
        {
            var name = command;
            context.RegisterResponseCommand(name, (request, args, token) => Execute(name, request, args, token));
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.Permission(new("scope", [], []));
        var appPatterns = DefaultDirectories.SelectMany(directory => new[] { directory, directory + "/**" })
            .Select(JsonValue.From).ToArray();
        context.Permission(new("scope-app-recursive", [], [], appPatterns));
        var denied = OperatingSystem.IsWindows()
            ? new[] { "$APPLOCALDATA/EBWebView", "$APPLOCALDATA/EBWebView/**" }
            : new[] { "$APPLOCALDATA/CacheStorage/**", "$APPLOCALDATA/cookies", "$APPLOCALDATA/serviceworkers/**",
                "$APPLOCALDATA/WebKitCache/**", "$APPLOCALDATA/databases/**", "$APPLOCALDATA/localstorage/**" };
        context.Permission(new("deny-default", [], [], ScopeDeny: denied.Select(JsonValue.From).ToArray()));
        context.PermissionSet("default", "scope-app-recursive", "deny-default", "allow-exists", "allow-read-file",
            "allow-read-text-file", "allow-read-dir", "allow-stat", "allow-lstat", "allow-size", "allow-mkdir");
    }

    private static async ValueTask<RuntimeInvokeResponse> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            switch (command)
            {
                case "exists":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    return Json(JsonValue.From(File.Exists(path) || Directory.Exists(path)));
                }
                case "read_file":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    return RuntimeInvokeResponse.FromBinary(await File.ReadAllBytesAsync(path, token));
                }
                case "read_text_file":
                {
                    var options = Options(args);
                    var path = await Resolve(context, args, "path", options, token);
                    return Json(JsonValue.From(await File.ReadAllTextAsync(path, EncodingFor(options), token)));
                }
                case "write_file":
                case "write_text_file":
                {
                    if (!context.Request.Body().IsBinary) throw new InvokeException("File write requires a binary request body.");
                    var headers = context.Request.Headers();
                    if (!headers.TryGetValue("path", out var encodedPath)) throw new InvokeException("File write path header is missing.");
                    var options = headers.TryGetValue("options", out var optionsJson) ? JsonValue.Parse(optionsJson) : default;
                    var path = await Resolve(context, Uri.UnescapeDataString(encodedPath), options, token);
                    var mode = Flag(options, "append") ? FileMode.Append : Flag(options, "createNew") ? FileMode.CreateNew : FileMode.Create;
                    await using var file = new FileStream(path, mode, FileAccess.Write, FileShare.Read, 81920, true);
                    await file.WriteAsync(context.Request.Body().Bytes, token);
                    return Json(JsonValue.Null);
                }
                case "mkdir":
                {
                    var options = Options(args);
                    var path = await Resolve(context, args, "path", options, token);
                    if (!Flag(options, "recursive") && !Directory.Exists(Path.GetDirectoryName(path)))
                        throw new DirectoryNotFoundException(Path.GetDirectoryName(path));
                    if (!Flag(options, "recursive") && (Directory.Exists(path) || File.Exists(path)))
                        throw new IOException("The directory already exists: " + path);
                    Directory.CreateDirectory(path);
                    return Json(JsonValue.Null);
                }
                case "read_dir":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    var entries = Directory.EnumerateFileSystemEntries(path).Select(entry =>
                    {
                        var attributes = File.GetAttributes(entry);
                        return (Name: Path.GetFileName(entry), Attributes: attributes);
                    }).ToArray();
                    return Json(Write(writer =>
                    {
                        writer.WriteStartArray();
                        foreach (var entry in entries)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("name", entry.Name);
                            writer.WriteBoolean("isFile", !entry.Attributes.HasFlag(FileAttributes.Directory));
                            writer.WriteBoolean("isDirectory", entry.Attributes.HasFlag(FileAttributes.Directory));
                            writer.WriteBoolean("isSymlink", entry.Attributes.HasFlag(FileAttributes.ReparsePoint));
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }));
                }
                case "remove":
                {
                    var options = Options(args);
                    var path = await Resolve(context, args, "path", options, token);
                    if (Directory.Exists(path)) Directory.Delete(path, Flag(options, "recursive"));
                    else if (File.Exists(path)) File.Delete(path);
                    else throw new FileNotFoundException("The path does not exist.", path);
                    return Json(JsonValue.Null);
                }
                case "copy_file":
                {
                    var options = Options(args);
                    var source = await Resolve(context, args, "fromPath", options, token, "fromPathBaseDir");
                    var destination = await Resolve(context, args, "toPath", options, token, "toPathBaseDir");
                    File.Copy(source, destination, overwrite: true);
                    return Json(JsonValue.Null);
                }
                case "rename":
                {
                    var options = Options(args);
                    var source = await Resolve(context, args, "oldPath", options, token, "oldPathBaseDir");
                    var destination = await Resolve(context, args, "newPath", options, token, "newPathBaseDir");
                    if (Directory.Exists(source)) Directory.Move(source, destination);
                    else File.Move(source, destination, overwrite: true);
                    return Json(JsonValue.Null);
                }
                case "stat":
                case "lstat":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    return Json(Info(path));
                }
                case "size":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    if (!Directory.Exists(path)) return Json(JsonValue.From(new FileInfo(path).Length));
                    long size = 0;
                    var scope = context.IsTrusted ? null : Scope(context);
                    foreach (var entry in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        if (scope is not null && !IsAllowed(context, scope, entry))
                            throw new InvokeException("forbidden path: " + entry);
                        size = checked(size + new FileInfo(entry).Length);
                    }
                    return Json(JsonValue.From(size));
                }
                case "truncate":
                {
                    var path = await Resolve(context, args, "path", Options(args), token);
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
                    file.SetLength(Number(args, "len", 0));
                    return Json(JsonValue.Null);
                }
                case "create":
                case "open":
                {
                    var options = Options(args);
                    var path = await Resolve(context, args, "path", options, token);
                    var write = command == "create" || Flag(options, "write") || Flag(options, "append");
                    var access = write ? Flag(options, "read") ? FileAccess.ReadWrite : FileAccess.Write : FileAccess.Read;
                    var mode = command == "create" || Flag(options, "truncate") ? FileMode.Create
                        : Flag(options, "createNew") ? FileMode.CreateNew
                        : Flag(options, "append") && !Flag(options, "read") ? FileMode.Append
                        : Flag(options, "create") ? FileMode.OpenOrCreate : FileMode.Open;
                    var file = new FileStream(path, mode, access, FileShare.Read, 81920, true);
                    if (Flag(options, "append") && Flag(options, "read")) file.Seek(0, SeekOrigin.End);
                    var resource = new FileResource(file, context.WebviewLabel, context.DocumentId);
                    try { return Json(JsonValue.From(context.AddResource(resource))); }
                    catch { resource.Dispose(); throw; }
                }
                case "read":
                {
                    var file = Handle(context, args);
                    var length = checked((int)Number(args, "len"));
                    if (length < 0 || length > 64 * 1024 * 1024) throw new InvokeException("Invalid read length.");
                    var bytes = new byte[length + 8];
                    var read = await file.Stream.ReadAsync(bytes.AsMemory(0, length), token);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(length, 8), (ulong)read);
                    if (read < length) bytes = [.. bytes.AsSpan(0, read).ToArray(), .. bytes.AsSpan(length, 8).ToArray()];
                    return RuntimeInvokeResponse.FromBinary(bytes);
                }
                case "write":
                {
                    var file = Handle(context, args);
                    var data = Property(args, "data");
                    if (data.ValueKind != JsonValueKind.Array) throw new InvokeException("File write data must be a byte array.");
                    var bytes = data.EnumerateArray().Select(value => value.GetByte()).ToArray();
                    await file.Stream.WriteAsync(bytes, token);
                    return Json(JsonValue.From(bytes.Length));
                }
                case "seek":
                {
                    var file = Handle(context, args);
                    var origin = Number(args, "whence") switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => throw new InvokeException("Invalid seek mode.") };
                    return Json(JsonValue.From(file.Stream.Seek(Number(args, "offset"), origin)));
                }
                case "fstat": return Json(Info(Handle(context, args).Stream.Name));
                case "ftruncate":
                {
                    Handle(context, args).Stream.SetLength(Number(args, "len", 0));
                    return Json(JsonValue.Null);
                }
                case "read_text_file_lines":
                {
                    var options = Options(args);
                    var path = await Resolve(context, args, "path", options, token);
                    var encoding = EncodingFor(options);
                    var reader = new LineResource(new StreamReader(path, encoding), encoding, context.WebviewLabel, context.DocumentId);
                    try { return Json(JsonValue.From(context.AddResource(reader))); }
                    catch { reader.Dispose(); throw; }
                }
                case "read_text_file_lines_next":
                {
                    var reader = Lines(context, args);
                    var line = await reader.Reader.ReadLineAsync(token);
                    if (line is null)
                    {
                        context.App.Resources.Close(checked((uint)Number(args, "rid")));
                        return RuntimeInvokeResponse.FromBinary(new byte[] { 1 });
                    }
                    return RuntimeInvokeResponse.FromBinary(reader.Encoding.GetBytes(line).Concat(new byte[] { 0 }).ToArray());
                }
                case "start_accessing_security_scoped_resource":
                case "stop_accessing_security_scoped_resource":
                    return Json(JsonValue.Null);
                default: throw new InvokeException($"File system command {command} is not implemented.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        {
            throw new InvokeException(error.Message, error);
        }
    }

    private static JsonElement Options(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty("options", out var value) ? value : default;

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) ? property : throw new InvokeException($"Missing file system argument '{name}'.");

    private static string Text(JsonElement value, string name) => Property(value, name).ValueKind == JsonValueKind.String
        ? Property(value, name).GetString()! : throw new InvokeException($"File system argument '{name}' must be a string.");

    private static long Number(JsonElement value, string name, long? fallback = null) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var number) && number.ValueKind != JsonValueKind.Null ? number.GetInt64()
        : fallback ?? throw new InvokeException($"Missing file system argument '{name}'.");

    private static bool Flag(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;

    private static Encoding EncodingFor(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object || !options.TryGetProperty("encoding", out var value) || value.ValueKind == JsonValueKind.Null)
            return Encoding.UTF8;
        return value.GetString()?.ToLowerInvariant() switch
        {
            "utf-8" or "utf8" => Encoding.UTF8,
            "utf-16le" => Encoding.Unicode,
            _ => throw new InvokeException("The requested text encoding is not supported.")
        };
    }

    private static async Task<string> Resolve(InvokeContext context, JsonElement args, string name, JsonElement options,
        CancellationToken token, string baseProperty = "baseDir") =>
        await Resolve(context, Text(args, name), options, token, baseProperty);

    private static async Task<string> Resolve(InvokeContext context, string input, JsonElement options,
        CancellationToken token, string baseProperty = "baseDir")
    {
        if (input.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !uri.IsFile) throw new InvokeException("Invalid file URL.");
            input = uri.LocalPath;
        }
        if (string.IsNullOrWhiteSpace(input) || input.IndexOf('\0') >= 0 ||
            input.Split(['/', '\\']).Any(component => component == ".."))
            throw new InvokeException("File system paths cannot contain parent traversal.");
        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty(baseProperty, out var baseValue) &&
            baseValue.ValueKind != JsonValueKind.Null)
        {
            if (Path.IsPathRooted(input)) throw new InvokeException("A base directory requires a relative path.");
            input = await context.App.ResolveDirectoryAsync(baseValue.GetInt32(), input, token);
        }
        else if (!Path.IsPathFullyQualified(input)) throw new InvokeException("A relative path requires a base directory.");
        var path = Path.GetFullPath(input);
        if (!context.IsTrusted)
        {
            var scope = Scope(context);
            if (!IsAllowed(context, scope, path)) throw new InvokeException("forbidden path: " + path);
        }
        return path;
    }

    private static FileScope Scope(InvokeContext context)
    {
        string[] Patterns(IReadOnlyList<JsonElement> entries) => entries.Select(entry => entry.ValueKind switch
        {
            JsonValueKind.String => entry.GetString()!,
            JsonValueKind.Object when entry.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String => path.GetString()!,
            _ => throw new InvokeException("File system scope entries require a path string.")
        }).ToArray();
        var allow = Patterns(context.Scope.Allow);
        var deny = Patterns(context.Scope.Deny);
        var config = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("allow"); writer.WriteStartArray(); foreach (var pattern in allow) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WritePropertyName("deny"); writer.WriteStartArray(); foreach (var pattern in deny) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WriteEndObject();
        });
        return new FileScope(config, context.App.Metadata.Identifier);
    }

    private static bool IsAllowed(InvokeContext context, FileScope scope, string path)
    {
        var resolved = ResolveLinks(path);
        var selected = context.App.TryPlugin<FsPlugin>()?._selectedPaths;
        bool Allowed(string candidate) => scope.IsAllowed(candidate) ||
            (!scope.IsForbidden(candidate) && selected?.IsAllowed(candidate) == true);
        return Allowed(path) && Allowed(resolved);
    }

    private static string ResolveLinks(string path)
    {
        var root = Path.GetPathRoot(path) ?? throw new InvokeException("File system path has no root.");
        var current = root;
        foreach (var component in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var info = new FileInfo(current);
            if (info.LinkTarget is null) continue;
            var target = info.ResolveLinkTarget(true) ?? throw new InvokeException("File system link target is unavailable.");
            current = Path.GetFullPath(target.FullName);
        }
        return current;
    }

    private static FileResource Handle(InvokeContext context, JsonElement args)
    {
        var file = context.App.Resources.Get<FileResource>(checked((uint)Number(args, "rid")));
        if (!context.IsTrusted && (file.WebviewLabel != context.WebviewLabel || file.DocumentId != context.DocumentId))
            throw new InvokeException("File handle belongs to another document.");
        return file;
    }

    private static LineResource Lines(InvokeContext context, JsonElement args)
    {
        var lines = context.App.Resources.Get<LineResource>(checked((uint)Number(args, "rid")));
        if (!context.IsTrusted && (lines.WebviewLabel != context.WebviewLabel || lines.DocumentId != context.DocumentId))
            throw new InvokeException("File iterator belongs to another document.");
        return lines;
    }

    private static JsonElement Info(string path)
    {
        var attributes = File.GetAttributes(path);
        var directory = attributes.HasFlag(FileAttributes.Directory);
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        var size = directory ? 0 : ((FileInfo)info).Length;
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteBoolean("isFile", !directory);
            writer.WriteBoolean("isDirectory", directory);
            writer.WriteBoolean("isSymlink", attributes.HasFlag(FileAttributes.ReparsePoint));
            writer.WriteNumber("size", size);
            writer.WriteString("mtime", info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("atime", info.LastAccessTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("birthtime", info.CreationTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteBoolean("readonly", attributes.HasFlag(FileAttributes.ReadOnly));
            writer.WriteNull("fileAttributes"); writer.WriteNull("dev"); writer.WriteNull("ino");
            writer.WriteNull("mode"); writer.WriteNull("nlink"); writer.WriteNull("uid");
            writer.WriteNull("gid"); writer.WriteNull("rdev"); writer.WriteNull("blksize"); writer.WriteNull("blocks");
            writer.WriteEndObject();
        });
    }

    private static RuntimeInvokeResponse Json(JsonElement value) => RuntimeInvokeResponse.FromJson(value);

    private static JsonElement Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) action(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private sealed record FileResource(FileStream Stream, string WebviewLabel, ulong DocumentId) : IDisposable
    {
        public void Dispose() => Stream.Dispose();
    }

    private sealed record LineResource(StreamReader Reader, Encoding Encoding, string WebviewLabel, ulong DocumentId) : IDisposable
    {
        public void Dispose() => Reader.Dispose();
    }
}
