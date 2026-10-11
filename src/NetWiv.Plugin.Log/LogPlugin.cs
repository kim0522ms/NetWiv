using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetWiv.Plugin.Log;

/// <summary>
/// Receives records from the official log guest and writes to the app log directory.
/// When the app has a logger factory, records are also written to the <c>NetWiv.Webview</c> logger
/// and the plugin's direct stdout output is replaced by the configured logging providers.
/// </summary>
public sealed class LogPlugin : IPlugin
{
    private const string LoggerCategory = "NetWiv.Webview";
    private static readonly EventId RecordEvent = new(1, "WebviewLog");
    private static readonly Action<ILogger, string, string, Exception?>[] Writers =
    [
        LoggerMessage.Define<string, string>(LogLevel.Trace, RecordEvent, "{Message}{Source}"),
        LoggerMessage.Define<string, string>(LogLevel.Debug, RecordEvent, "{Message}{Source}"),
        LoggerMessage.Define<string, string>(LogLevel.Information, RecordEvent, "{Message}{Source}"),
        LoggerMessage.Define<string, string>(LogLevel.Warning, RecordEvent, "{Message}{Source}"),
        LoggerMessage.Define<string, string>(LogLevel.Error, RecordEvent, "{Message}{Source}"),
    ];
    private readonly object _gate = new();
    private readonly int _minimumLevel;
    private readonly int _maxFileBytes;
    private readonly int _retainedFiles;

    public LogPlugin(int minimumLevel = 1) : this(minimumLevel, 5 * 1024 * 1024, 3) { }

    public LogPlugin(int minimumLevel, int maxFileBytes, int retainedFiles = 3)
    {
        if (minimumLevel is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        if (maxFileBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        if (retainedFiles < 0) throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        _minimumLevel = minimumLevel;
        _maxFileBytes = maxFileBytes;
        _retainedFiles = retainedFiles;
    }

    public string Name => "log";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("log", Log);
        context.Permission(new("allow-log", ["log"], []));
        context.Permission(new("deny-log", [], ["log"]));
        context.PermissionSet("default", "allow-log");
    }

    private async ValueTask<JsonElement> Log(InvokeContext context, JsonElement args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!args.TryGetProperty("level", out var levelValue) || !levelValue.TryGetInt32(out var level) || level is < 1 or > 5)
            throw new InvokeException("Log level must be between 1 and 5.");
        if (!args.TryGetProperty("message", out var messageValue) || messageValue.ValueKind != JsonValueKind.String)
            throw new InvokeException("Log message must be a string.");
        if (level < _minimumLevel) return JsonValue.Null;

        var message = messageValue.GetString()!;
        var location = OptionalString(args, "location");
        var file = OptionalString(args, "file");
        var line = args.TryGetProperty("line", out var lineValue) && lineValue.ValueKind == JsonValueKind.Number
            ? lineValue.GetRawText() : null;
        var source = "";
        if (location is not null) source += " (" + location + ")";
        if (file is not null) source += " [" + file + (line is null ? "" : ":" + line) + "]";
        var record = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}[webview][{LevelName(level)}] {message}{source}";
        var forwarded = context.App.LoggerFactory is not NullLoggerFactory;
        var bytes = Encoding.UTF8.GetBytes(record + Environment.NewLine);
        if (bytes.Length > _maxFileBytes)
        {
            var marker = Encoding.UTF8.GetBytes(" [truncated]" + Environment.NewLine);
            var end = _maxFileBytes - marker.Length;
            // Keep complete UTF-8 code points, including a multi-byte character at the boundary.
            while ((bytes[end] & 0xc0) == 0x80) end--;
            record = Encoding.UTF8.GetString(bytes, 0, end) + " [truncated]";
            bytes = Encoding.UTF8.GetBytes(record + Environment.NewLine);
        }
        var directory = await context.App.ResolveDirectoryAsync((int)BaseDirectory.AppLog, "", token);
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, context.App.Metadata.Name + ".log");
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maxFileBytes)
            {
                if (_retainedFiles == 0) File.Delete(path);
                else
                {
                    for (var index = _retainedFiles - 1; index >= 1; index--)
                        if (File.Exists(path + "." + index)) File.Move(path + "." + index, path + "." + (index + 1), true);
                    File.Move(path, path + ".1", true);
                }
            }
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) stream.Write(bytes);
            if (!forwarded) Console.Out.WriteLine(record);
        }
        if (forwarded) Writers[level - 1](context.App.LoggerFactory.CreateLogger(LoggerCategory), message, source, null);
        using var payload = JsonDocument.Parse("{\"level\":" + level + ",\"message\":" + JsonValue.From(record).GetRawText() + "}");
        context.App.Emit("log://log", payload.RootElement);
        return JsonValue.Null;
    }

    private static string? OptionalString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string LevelName(int level) => level switch
    {
        1 => "TRACE", 2 => "DEBUG", 3 => "INFO", 4 => "WARN", _ => "ERROR"
    };
}
