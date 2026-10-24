using System.Text;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.Fs;

internal static class FsGuestSmoke
{
    internal static int Run()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-fs-guest-").FullName;
        var result = 1;
        try
        {
            File.WriteAllText(Path.Combine(root, "utf8.txt"), "Hello 한글 😀\r\n\r\nlast", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(root, "utf16.txt"), "Hello 한글 😀\r\n\r\nlast", Encoding.Unicode);
            File.WriteAllBytes(Path.Combine(root, "empty.txt"), []);
            var fs = new FsPlugin();
            using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedAssets().Plugin(fs)
                .Window("main", "NetWiv file API regression", "fs-guest.html", extraConfig: JsonValue.Parse(
                    "{\"dataDirectory\":" + JsonValue.From(Path.Combine(root, "profile")).GetRawText() + "}"))
                .Capability(new("main", ["fs:allow-read-text-file", "fs:allow-read-text-file-lines", "fs:allow-read-text-file-lines-next",
                    "fs:allow-read-file", "fs:allow-write-file", "fs:allow-write-text-file",
                    "core:event:allow-listen", "core:event:allow-unlisten"], Windows: ["main"]))
                .RegisterCommand("fs_config", (_, _, _) =>
                {
                    fs.GrantSelectedPath(root, directory: true, recursive: true);
                    return ValueTask.FromResult(JsonValue.From(root.Replace('\\', '/') + "/"));
                })
                .InvokeHandler("fs_ordinary_failure", "fs_expected_failure")
                .RegisterCommand("fs_emit_progress", (context, _, _) =>
                {
                    context.App.EmitTo("main", "batch-progress", new GuestProgress(9007199254740993, "Complete"), GuestEventJson.Default.GuestProgress);
                    return ValueTask.FromResult(JsonValue.Null);
                })
                .RegisterCommand("fs_result", (context, args, _) =>
                {
                    if (args.GetProperty("passed").GetBoolean())
                    {
                        result = 0;
                    }
                    else Console.Error.WriteLine("FAIL public fs guest stage " + args.GetProperty("stage").GetString());
                    context.App.Exit(result);
                    return ValueTask.FromResult(JsonValue.Null);
                }).Build();
            using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
            app.Run();
        }
        finally
        {
            // WebView2 may release its profile lock shortly after runtime disposal.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root, recursive: true); break; }
                catch (IOException) when (attempt < 100) { Thread.Sleep(100); }
            }
        }
        if (result == 0) Console.WriteLine("PASS public fs guest text/bytes/options/lines, command errors and typed events");
        return result;
    }

    [Command]
    public static void FsOrdinaryFailure() => throw new ArgumentException("A note needs some text.");

    [Command]
    public static void FsExpectedFailure() => throw new InvokeException("A note needs some text.");
}

internal sealed record GuestProgress([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalBytes, string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GuestProgress))]
internal partial class GuestEventJson : JsonSerializerContext;
