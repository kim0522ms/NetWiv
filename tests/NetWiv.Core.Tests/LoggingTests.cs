using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetWiv;
using NetWiv.Plugin.Log;

internal static class LoggingTests
{
    internal static async Task CommandPipeline()
    {
        var records = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(records).SetMinimumLevel(LogLevel.Trace));
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).UseLoggerFactory(factory)
            .RegisterCommand("explode", static (_, _, _) => throw new InvalidOperationException("boom"))
            .RegisterCommand("expected", static (_, _, _) => throw new InvokeException("expected failure"))
            .Build();
        app.Run();

        Send(runtime, "explode", "{}");
        Assert.Contains("boom", await runtime.NextScript());
        Send(runtime, "expected", "{}");
        Assert.Contains("expected failure", await runtime.NextScript());
        Send(runtime, "plugin:missing|denied", "{}");
        Assert.Contains("runCallback(2,", await runtime.NextScript());
        Send(runtime, "explode", "{}", key: new string('0', 64));
        Assert.Contains("invalid invoke key", await runtime.NextScript());
        runtime.Context!.OnMessage(new("main", new Uri("tauri://localhost"), "{not json", null, 0));

        var failure = records.Single("NetWiv.Commands", LogLevel.Error);
        Assert.Contains("'explode'", failure.Message);
        Assert.True(failure.Exception is InvalidOperationException { Message: "boom" }, "The command exception was not attached.");
        Assert.True(!records.Records.Any(record => record.Message.Contains("'expected'", StringComparison.Ordinal)),
            "An intentional InvokeException must not be logged as a failure.");
        Assert.Contains("plugin:missing|denied", records.Single("NetWiv.Commands", LogLevel.Debug).Message);
        Assert.Contains("invalid invoke key", records.Records.Single(record => record.Category == "NetWiv.Ipc" && record.EventId == 2).Message);
        Assert.True(records.Records.Single(record => record.Category == "NetWiv.Ipc" && record.EventId == 1).Exception is not null,
            "A malformed IPC message was discarded without a diagnostic.");
        Assert.True(app.Handle.LoggerFactory == factory, "The configured logger factory was not exposed.");
    }

    internal static async Task WebviewRecordsUseLoggerFactory()
    {
        var records = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(records).SetMinimumLevel(LogLevel.Trace));
        using var app = new Builder().UseRuntime(new FakeRuntime()).UseLoggerFactory(factory)
            .Metadata(new("LogProbe", "1.0", "dev.netwiv.log." + Guid.NewGuid().ToString("N")))
            .Plugin(new LogPlugin()).Build();
        var directory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppLog, "");
        try
        {
            await app.Handle.InvokeAsync("plugin:log|log", JsonValue.Parse("""{"level":4,"message":"disk almost full","file":"app.js","line":12}"""));
            var record = records.Single("NetWiv.Webview", LogLevel.Warning);
            Assert.Equal("disk almost full [app.js:12]", record.Message);
            Assert.Contains("disk almost full", File.ReadAllText(Path.Combine(directory, "LogProbe.log")));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Send(FakeRuntime runtime, string command, string payload, string? key = null)
    {
        const string marker = "__NETWIV_INVOKE_KEY__: '";
        var script = runtime.Context!.InitializationScript;
        key ??= script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
        runtime.Context.OnMessage(new("main", new Uri("tauri://localhost"),
            $"{{\"cmd\":\"{command}\",\"callback\":1,\"error\":2,\"payload\":{payload},\"__NETWIV_INVOKE_KEY__\":\"{key}\"}}", null, 0));
    }
}

internal sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message, Exception? Exception);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _records = new();
    public IReadOnlyCollection<CapturedLog> Records => _records.ToArray();
    public CapturedLog Single(string category, LogLevel level) => Records.Single(record => record.Category == category && record.Level == level);
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _records);
    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            records.Enqueue(new(category, logLevel, eventId.Id, formatter(state, exception), exception));
    }
}
