using System.Buffers;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Channel = System.Threading.Channels.Channel;
using NetWiv;
using NetWiv.Platform.Desktop;
using NetWiv.Plugin.DeepLink;
using NetWiv.Runtime;

// Functional assertion receiver for MacLaunchServicesTests. No result files are written.
internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS()) return 2;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(125), Timeout.InfiniteTimeSpan);
        var opened = Channel.CreateUnbounded<(string[] Urls, Task<IReadOnlyList<Window>> Windows)>();
        var deepLinks = Channel.CreateUnbounded<(string[] Urls, Task<JsonElement> Current)>();
        var shutdownCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = 0;
        Task<int>? work = null;
        try
        {
            using (var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedConfig()
                .ResolveAssets(_ => new AssetResponse(200, "text/html", "<!doctype html><title>URL activation</title>"u8.ToArray()))
                .ExitOnLastWindowClose(false).Plugin(new DeepLinkPlugin(["fixture"]))
                .Window("main", "NetWiv URL activation", "index.html")
                .OnOpened((handle, value) =>
                {
                    Require(Environment.CurrentManagedThreadId == uiThread, "native-thread");
                    opened.Writer.TryWrite((value.Urls.Select(url => url.AbsoluteUri).ToArray(), handle.GetAllWindowsAsync(deadline.Token)));
                })
                .Setup(app =>
                {
                    uiThread = Environment.CurrentManagedThreadId;
                    app.Handle.EventHandlerFailed += (_, _) => opened.Writer.TryComplete(new InvalidOperationException("event-handler"));
                    app.Handle.Events.Listen("deep-link://new-url", value =>
                        deepLinks.Writer.TryWrite((Strings(value.Payload), app.Handle.InvokeAsync("plugin:deep-link|get_current", JsonValue.EmptyObject, deadline.Token).AsTask())));
                    work = RunAsync(app.Handle);
                }).Build())
            {
                app.Run();
            }
            // Terminal success is acknowledged only after the loop and disposal succeed.
            shutdownCompleted.SetResult();
            return work?.GetAwaiter().GetResult() ?? 1;
        }
        catch
        {
            Console.Error.WriteLine("FAIL LaunchServices receiver");
            return 1;
        }

        async Task<int> RunAsync(AppHandle handle)
        {
            var identifier = handle.Metadata.Identifier;
            var instance = Guid.NewGuid().ToString("N");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier))).ToLowerInvariant();
            using var pipe = new NamedPipeClientStream(".", "nwls-" + hash[..16], PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            StreamWriter? writer = null;
            var assertion = "connect";
            try
            {
                await pipe.ConnectAsync(deadline.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                assertion = "ready";
                Require(identifier.StartsWith("dev.netwiv.urltest.", StringComparison.Ordinal), assertion);
                var nonce = identifier["dev.netwiv.urltest.".Length..];
                Require(nonce.Length == 32 && nonce.All(char.IsAsciiHexDigit), assertion);
                var scheme = "netwiv-urltest-" + nonce;
                Require(await handle.GetWindow("main")!.IsVisibleAsync(deadline.Token), assertion);
                await SendAsync("ready", fields => fields.WriteNumber("pid", Environment.ProcessId));
                foreach (var phase in new[] { "startup", "warm", "resident" })
                {
                    assertion = phase;
                    if (phase == "resident")
                    {
                        await CommandAsync("close");
                        await handle.GetWindow("main")!.CloseAsync(deadline.Token);
                        Require((await handle.GetAllWindowsAsync(deadline.Token)).Count == 0 && handle.Windows.Count == 0, assertion);
                        await SendAsync("resident-ready", fields => fields.WriteNumber("windowCount", 0));
                    }
                    var suffixes = phase == "startup" ? new[] { "startup" } : new[] { phase + "-one", phase + "-two" };
                    var expected = suffixes.Select(suffix => scheme + "://activation/" + suffix).ToArray();
                    var aggregate = new List<string>();
                    var windowCount = phase == "resident" ? 0 : 1;
                    while (aggregate.Count < expected.Length)
                    {
                        var native = await opened.Reader.ReadAsync(deadline.Token);
                        var deep = await deepLinks.Reader.ReadAsync(deadline.Token);
                        Require(native.Urls.Length > 0 && native.Urls.SequenceEqual(deep.Urls) &&
                            native.Urls.SequenceEqual(Strings(await deep.Current)) && (await native.Windows).Count == windowCount, assertion);
                        aggregate.AddRange(native.Urls);
                    }
                    Require(aggregate.SequenceEqual(expected), assertion);
                    if (phase == "resident")
                    {
                        await new WebviewWindowBuilder(handle, "main").Title("NetWiv restored URL activation").Url("index.html").BuildAsync(deadline.Token);
                        Require((await handle.GetAllWindowsAsync(deadline.Token)).Count == 1, assertion);
                    }
                    await SendAsync(phase, fields =>
                    {
                        fields.WritePropertyName("urls"); fields.WriteStartArray();
                        foreach (var url in aggregate) fields.WriteStringValue(url);
                        fields.WriteEndArray(); fields.WriteNumber("nativeWindowCount", windowCount);
                        if (phase == "resident") fields.WriteNumber("recreatedWindowCount", 1);
                    });
                }
                assertion = "quit";
                await CommandAsync("quit");
                Require(!opened.Reader.TryPeek(out _) && !deepLinks.Reader.TryPeek(out _), assertion);
                await SendAsync("quit");
                handle.Exit();
                assertion = "complete";
                await shutdownCompleted.Task.WaitAsync(deadline.Token);
                await SendAsync("complete");
                return 0;

                async Task CommandAsync(string expected)
                {
                    var line = await reader.ReadLineAsync(deadline.Token);
                    Require(line is not null, expected);
                    using var command = JsonDocument.Parse(line!);
                    Require(command.RootElement.GetProperty("command").GetString() == expected, expected);
                }
            }
            catch
            {
                if (writer is not null)
                {
                    try { await writer.WriteLineAsync(Message("error", fields => fields.WriteString("assertion", assertion)).AsMemory(), deadline.Token); }
                    catch { }
                }
                handle.Exit(1);
                return 1;
            }
            finally { writer?.Dispose(); }

            Task SendAsync(string phase, Action<Utf8JsonWriter>? fields = null) =>
                writer!.WriteLineAsync(Message(phase, fields).AsMemory(), deadline.Token);

            string Message(string phase, Action<Utf8JsonWriter>? fields)
            {
                var buffer = new ArrayBufferWriter<byte>();
                using (var json = new Utf8JsonWriter(buffer))
                {
                    json.WriteStartObject(); json.WriteString("phase", phase);
                    if (phase != "error") json.WriteString("instance", instance);
                    fields?.Invoke(json); json.WriteEndObject();
                }
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
        }
    }

    private static string[] Strings(JsonElement values) => values.EnumerateArray().Select(value => value.GetString()!).ToArray();
    private static void Require(bool condition, string assertion)
    {
        if (!condition) throw new InvalidOperationException(assertion);
    }
}
