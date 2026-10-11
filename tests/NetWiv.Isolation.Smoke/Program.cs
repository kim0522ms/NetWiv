using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

[assembly: CommandJsonContext(typeof(SmokeJsonContext))]

internal static class Program
{
    private static readonly CancellationTokenSource Finished = new();
    private static readonly TaskCompletionSource PendingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource PendingFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _blocked;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedConfig().UseEmbeddedAssets()
            .RegisterGeneratedCommands().InvokeHandler(registry => registry.RegisterResponse("raw", (context, _, _) =>
                ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(context.Request.Body().Bytes))))
            .Build();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(60), Finished.Token); Console.Error.WriteLine("FAIL isolation smoke timed out"); app.Handle.Exit(1); }
            catch (OperationCanceledException) { }
        });
        app.Run();
        Finished.Cancel();
        return Environment.ExitCode;
    }

    [Command] public static JsonElement Echo(JsonElement value) => value;
    [Command] public static int Blocked() => Interlocked.Increment(ref _blocked);
    [Command] public static int BlockedCount() => _blocked;
    [Command] public static JsonElement BodyInfo(Request request) => JsonValue.Parse($$"""{"binary":{{(request.Body().IsBinary ? "true" : "false")}},"value":{{(request.Body().IsBinary ? "null" : request.Body().Json.GetRawText())}}}""");
    [Command] public static void Fail() => throw new InvokeException(JsonValue.Parse("{\"code\":\"expected\",\"detail\":42}"));
    [Command] public static JsonElement RequestInfo(Request request) => JsonValue.Parse($$"""{"header":{{JsonValue.From(request.Headers()["x-test"]).GetRawText()}},"binary":{{(request.Body().IsBinary ? "true" : "false")}},"value":{{request.Body().Json.GetProperty("value").GetRawText()}}}""");
    [Command] public static void Channel(NetWiv.Channel channel)
    {
        using (channel) for (var i = 0; i < 5; i++) channel.Send(JsonValue.From(i));
    }
    [Command] public static async Task<string> Pending(NetWiv.Channel channel)
    {
        PendingStarted.TrySetResult();
        await Task.Delay(1500);
        using (channel) channel.Send(JsonValue.From("old"));
        PendingFinished.TrySetResult();
        return "old";
    }
    [Command] public static Task WaitPendingStarted() => PendingStarted.Task;
    [Command] public static Task WaitPending() => PendingFinished.Task;
    [Command] public static void Complete(AppHandle app, JsonElement results)
    {
        var success = results.GetArrayLength() == 19;
        foreach (var result in results.EnumerateArray())
        {
            var passed = result.GetProperty("passed").GetBoolean();
            success &= passed;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {result.GetProperty("name").GetString()}{(passed ? "" : ": " + result.GetProperty("error").GetString())}");
        }
        if (Environment.GetEnvironmentVariable("NETWIV_ISOLATION_REPORT") is { } report) File.WriteAllText(report, results.GetRawText());
        Finished.Cancel();
        app.Exit(success ? 0 : 1);
    }
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class SmokeJsonContext : JsonSerializerContext;
