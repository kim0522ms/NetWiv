using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class FilePathSmoke
{
    private static string _expectedPath = "";
    private static bool _interactive;
    private static bool _mainDrop;
    private static bool _pickerSelection;
    private static bool _pickerCanceled;
    private static bool _htmlReordered;
    private static bool _nativeDrop;
    private static bool _nativeHtmlDropAbsent;
    private static bool _automaticPassed;
    private static bool _windowDenied;
    private static bool _browserFile;
    private static bool _browserFileDeleted;
    private static volatile bool _browserVerified;
    private static int _result = 1;
    private static int _debugPort;
    private static bool _timeoutSmoke;
    private static volatile bool _timedOut;
    private static CancellationTokenSource? _browserCancellation;
    private static readonly object BrowserAppGate = new();
    private static Task? _browserTask;
    private static Timer? _watchdog;
    private static IDisposable? _nativeDropEvents;
    private static Webview? _nativeWebview;

    internal static int Run(bool interactive, bool timeoutSmoke = false)
    {
        if (interactive && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("FAIL interactive file path smoke requires Windows.");
            return 2;
        }

        _interactive = interactive;
        _mainDrop = _pickerSelection = _pickerCanceled = _htmlReordered = false;
        _nativeDrop = _nativeHtmlDropAbsent = false;
        _automaticPassed = _windowDenied = false;
        _browserFile = _browserFileDeleted = _browserVerified = false;
        _result = 1;
        _timeoutSmoke = timeoutSmoke;
        _timedOut = false;
        _browserTask = null;
        using var browserCancellation = new CancellationTokenSource();
        _browserCancellation = browserCancellation;
        var fixture = Directory.CreateTempSubdirectory("netwiv-file-path-");
        _expectedPath = Path.Combine(fixture.FullName, "sample.txt");
        var debugPort = 0;
        try
        {
            File.WriteAllText(_expectedPath, "NetWiv synthetic file path smoke sample.");
            debugPort = OperatingSystem.IsWindows() && !interactive ? ReserveLoopbackPort() : 0;
            _debugPort = debugPort;
            var runtime = new DesktopRuntime();
            var builder = new Builder().UseRuntime(runtime).UseEmbeddedAssets().UseEmbeddedConfig()
                .Permission(new("file-path-main", "file_path_config", "file_path_report", "file_path_open_fixture"))
                .Permission(new("file-path-native", "file_path_config", "file_path_report"))
                .Capability(new("file-path-main", ["file-path-main", "core:webview:allow-get-file-path"], Windows: ["main"]))
                .Capability(new("file-path-native", ["file-path-native"], Windows: ["native"]))
                .Capability(new("file-path-native-events", ["core:event:allow-listen", "core:event:allow-unlisten"], Windows: ["native"]))
                .Window("main", "NetWiv file path smoke", "file-path.html?role=main", 680, 620,
                    WindowConfig("main", debugPort, interactive))
                .Window("native", "NetWiv native drop comparison", "file-path.html?role=native", 520, 360,
                    WindowConfig("native", 0, interactive))
                .RegisterCommand("file_path_config", (context, _, _) => Value(Write(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteBoolean("interactive", _interactive);
                    writer.WriteBoolean("windows", OperatingSystem.IsWindows());
                    writer.WriteString("window", context.WebviewLabel);
                    writer.WriteString("fileName", "sample.txt");
                    writer.WriteEndObject();
                })))
                .RegisterCommand("file_path_report", (context, args, _) => Report(context, args))
                .RegisterCommand("file_path_open_fixture", (context, _, _) =>
                {
                    Require(_interactive && OperatingSystem.IsWindows() && context.WebviewLabel == "main", "fixture opener source");
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _expectedPath + "\"")
                    { UseShellExecute = true });
                    return Value(JsonValue.Null);
                })
                .Setup(context =>
                {
                    Program.VerifyBackend(runtime);
                    if (_timedOut) { context.Handle.Exit(124); return; }
                    _nativeWebview = context.Handle.GetWebview("native");
                    _nativeDropEvents = context.Handle.GetWindow("native")!.OnDragDropEvent(value =>
                    {
                        if (value.Payload is not DragDroppedEvent drop) return;
                        _nativeHtmlDropAbsent = false;
                        if (drop.Paths.Any(path => SamePath(path, _expectedPath))) _nativeDrop = true;
                        if (_interactive) _nativeWebview!.EvaluateScript("window.__netwivCheckNativeDomDrop?.()");
                        TryComplete(context.Handle);
                    });
                });
            using var app = builder.Build();
            using var watchdog = new Timer(_ =>
            {
                lock (BrowserAppGate)
                {
                    if (browserCancellation.IsCancellationRequested) return;
                    _timedOut = true;
                    browserCancellation.Cancel();
                    _result = 124;
                    if (!_timeoutSmoke) Console.Error.WriteLine("FAIL file path smoke timed out.");
                    try { app.Handle.Exit(124); }
                    catch (InvalidOperationException) { /* Setup observes timeouts that precede runtime startup. */ }
                }
            }, null, TimeSpan.FromMinutes(interactive ? 10 : 2), Timeout.InfiniteTimeSpan);
            _watchdog = watchdog;
            try { app.Run(); }
            finally
            {
                watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                lock (BrowserAppGate) browserCancellation.Cancel();
                if (!watchdog.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)))
                {
                    Console.Error.WriteLine("FAIL watchdog shutdown.");
                    _result = 1;
                }
                if (_browserTask is { } browserTask && !browserTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Console.Error.WriteLine("FAIL browser task shutdown.");
                    _result = 1;
                }
                _nativeDropEvents?.Dispose();
                _nativeDropEvents = null;
            }
        }
        catch
        {
            Console.Error.WriteLine("FAIL file path smoke during startup.");
            _result = 1;
        }
        finally
        {
            browserCancellation.Cancel();
            _watchdog = null;
            _nativeDropEvents?.Dispose();
            _nativeDropEvents = null;
            for (var attempt = 0; ; attempt++)
            {
                try { fixture.Delete(recursive: true); break; }
                catch (IOException) when (attempt < 100) { Thread.Sleep(100); }
                catch { Console.Error.WriteLine("FAIL synthetic file cleanup."); _result = 1; break; }
            }
        }
        if (_timeoutSmoke)
        {
            if (_timedOut && _result == 124 && _browserTask?.IsCompleted == true && !Directory.Exists(fixture.FullName))
            {
                Console.WriteLine("PASS file path watchdog cancels browser work and removes its fixture");
                return 0;
            }
            Console.Error.WriteLine("FAIL file path watchdog cleanup.");
            return 1;
        }
        return _result;
    }

    private static ValueTask<JsonElement> Report(InvokeContext context, JsonElement args)
    {
        var kind = args.GetProperty("kind").GetString();
        switch (kind)
        {
            case "failure":
                var failureStage = args.GetProperty("stage").GetString();
                Require(failureStage is "configuration" or "window permission" or "synthetic File" or "plain object"
                    or "missing File" or "forged stage-two ticket" or "guessed ticket request" or "browser file"
                    or "file path API", "failure stage");
                var failureKind = args.GetProperty("failureKind").GetString();
                Require(failureKind is "resolved" or "permission-denied" or "unsupported" or "unavailable" or "other", "failure kind");
                Console.Error.WriteLine($"FAIL file path smoke during {failureStage} ({failureKind}).");
                _result = 1;
                context.App.Exit(1);
                break;
            case "automatic":
                Require(context.WebviewLabel == "main", "automatic check source");
                if (!args.GetProperty("passed").GetBoolean())
                {
                    Console.Error.WriteLine("FAIL file path automatic checks.");
                    _result = 1;
                    context.App.Exit(1);
                    break;
                }
                _automaticPassed = true;
                if (OperatingSystem.IsWindows() && !_interactive)
                {
                    var cancellationToken = _browserCancellation!.Token;
                    _browserTask = Task.Run(async () =>
                    {
                        var browserStage = "browser debugger target";
                        try
                        {
                            await VerifyBrowserFileAsync(_debugPort, value => browserStage = value, cancellationToken);
                            lock (BrowserAppGate)
                            {
                                if (cancellationToken.IsCancellationRequested) return;
                                _browserVerified = true;
                                context.App.Dispatch(() =>
                                {
                                    if (!cancellationToken.IsCancellationRequested) TryComplete(context.App);
                                });
                            }
                        }
                        catch
                        {
                            lock (BrowserAppGate)
                            {
                                if (cancellationToken.IsCancellationRequested) return;
                                Console.Error.WriteLine($"FAIL browser file path smoke during {browserStage}.");
                                _result = 1;
                                context.App.Exit(1);
                            }
                        }
                    });
                }
                TryComplete(context.App);
                break;
            case "browser-file":
                Require(!_interactive && OperatingSystem.IsWindows() && context.WebviewLabel == "main", "browser file source");
                var browserPath = args.GetProperty("path").GetString();
                Require(browserPath is not null && SamePath(browserPath, _expectedPath), "browser file path result");
                File.Delete(_expectedPath);
                _browserFile = true;
                break;
            case "browser-file-deleted":
                Require(!_interactive && OperatingSystem.IsWindows() && context.WebviewLabel == "main", "deleted browser file source");
                var deletedBrowserPath = args.GetProperty("path").GetString();
                Require(deletedBrowserPath is not null && SamePath(deletedBrowserPath, _expectedPath)
                    && !File.Exists(_expectedPath), "deleted browser file path result");
                _browserFileDeleted = true;
                break;
            case "window-denied":
                Require(context.WebviewLabel == "native" && args.GetProperty("passed").GetBoolean(), "window capability denial");
                _windowDenied = true;
                TryComplete(context.App);
                break;
            case "main-drop":
            case "picker-selection":
                Require(_interactive && context.WebviewLabel == "main", "file source");
                var path = args.GetProperty("path").GetString();
                Require(path is not null && SamePath(path, _expectedPath), "file path result");
                if (kind == "main-drop") _mainDrop = true;
                else _pickerSelection = true;
                TryComplete(context.App);
                break;
            case "picker-cancelled":
                Require(_interactive && context.WebviewLabel == "main", "picker cancellation source");
                _pickerCanceled = true;
                TryComplete(context.App);
                break;
            case "html-reordered":
                Require(_interactive && context.WebviewLabel == "main"
                    && args.GetProperty("order").GetString() == "second,first", "HTML5 reorder");
                _htmlReordered = true;
                TryComplete(context.App);
                break;
            case "native-html-drop-count":
                Require(_interactive && context.WebviewLabel == "native", "native comparison source");
                if (args.GetProperty("count").GetInt32() != 0)
                {
                    _nativeHtmlDropAbsent = false;
                    _result = 1;
                    Console.Error.WriteLine("FAIL native drop also reached HTML.");
                    context.App.Exit(1);
                    break;
                }
                Require(_nativeDrop, "native comparison requires a native file drop");
                _nativeHtmlDropAbsent = true;
                TryComplete(context.App);
                break;
            default:
                throw new InvokeException("Unknown file path smoke result.");
        }
        return Value(JsonValue.Null);
    }

    private static void TryComplete(AppHandle app)
    {
        if (_result == 0 || _timedOut) return;
        if (!_interactive && _automaticPassed && _windowDenied
            && (!OperatingSystem.IsWindows() || _browserFile && _browserFileDeleted && _browserVerified))
        {
            _result = 0;
            Console.WriteLine(OperatingSystem.IsWindows()
                ? "PASS WebView2 file input path, invalid inputs and window capability denial"
                : "PASS unsupported file path behavior");
            app.Exit();
        }
        else if (_interactive && _automaticPassed && _windowDenied && _mainDrop && _pickerSelection
            && _pickerCanceled && _htmlReordered && _nativeDrop && _nativeHtmlDropAbsent)
        {
            _result = 0;
            Console.WriteLine("PASS Windows file drop paths, picker, HTML5 reorder and native drop comparison");
            app.Exit();
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch { return false; }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static JsonElement WindowConfig(string label, int debugPort, bool interactive)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("dataDirectory", Path.Combine(Path.GetDirectoryName(_expectedPath)!, "profile-" + label));
            writer.WriteBoolean("visible", interactive);
            writer.WriteBoolean("dragDropEnabled", label != "main");
            if (debugPort != 0)
                writer.WriteString("additionalBrowserArgs", $"--remote-debugging-port={debugPort} --remote-debugging-address=127.0.0.1");
            writer.WriteEndObject();
            writer.Flush();
        }
        return JsonValue.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static async Task VerifyBrowserFileAsync(int port, Action<string> setStage, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        JsonElement target = default;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var targets = JsonDocument.Parse(await client.GetStringAsync($"http://127.0.0.1:{port}/json/list", timeout.Token));
                foreach (var candidate in targets.RootElement.EnumerateArray())
                {
                    if (!candidate.TryGetProperty("url", out var urlValue) || urlValue.GetString() is not { } urlText
                        || !Uri.TryCreate(urlText, UriKind.Absolute, out var url)
                        || !url.AbsolutePath.EndsWith("/file-path.html", StringComparison.Ordinal)
                        || !url.Query.Contains("role=main", StringComparison.Ordinal)) continue;
                    target = candidate.Clone();
                    break;
                }
                if (target.ValueKind != JsonValueKind.Undefined) break;
            }
            catch { }
            await Task.Delay(100, timeout.Token);
        }
        if (target.ValueKind == JsonValueKind.Undefined) throw new TimeoutException();

        setStage("browser debugger WebSocket");
        using var socket = new ClientWebSocket();
        var socketUrl = new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
        if (socketUrl.Scheme != "ws" || socketUrl.Host != IPAddress.Loopback.ToString() || socketUrl.Port != port)
            throw new InvalidOperationException();
        await socket.ConnectAsync(socketUrl, timeout.Token);
        if (_timeoutSmoke)
        {
            _watchdog!.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            await Task.Delay(Timeout.Infinite, timeout.Token);
        }
        var cdp = new CdpClient(socket, timeout.Token);
        setStage("browser document query");
        var document = await cdp.CallAsync("DOM.getDocument", "{}");
        var rootNode = document.GetProperty("root").GetProperty("nodeId").GetInt32();
        var query = Write(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("nodeId", rootNode);
            writer.WriteString("selector", "#picker"); writer.WriteEndObject();
        }).GetRawText();
        var input = await cdp.CallAsync("DOM.querySelector", query);
        var nodeId = input.GetProperty("nodeId").GetInt32();
        if (nodeId == 0) throw new InvalidOperationException();
        var files = Write(writer =>
        {
            writer.WriteStartObject(); writer.WriteStartArray("files");
            writer.WriteStringValue(_expectedPath); writer.WriteEndArray();
            writer.WriteNumber("nodeId", nodeId); writer.WriteEndObject();
        }).GetRawText();
        setStage("browser file injection");
        await cdp.CallAsync("DOM.setFileInputFiles", files);
        setStage("browser file path API");
        var evaluation = await cdp.CallAsync("Runtime.evaluate",
            "{\"expression\":\"window.__netwivVerifyPicker?.()\",\"awaitPromise\":true,\"returnByValue\":true}");
        if (!evaluation.TryGetProperty("result", out var result)
            || !result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.True)
        {
            var failure = await cdp.CallAsync("Runtime.evaluate",
                "{\"expression\":\"window.__netwivPickerFailure ?? 'no-result'\",\"returnByValue\":true}");
            if (failure.TryGetProperty("result", out var failureResult)
                && failureResult.TryGetProperty("value", out var failureValue)
                && failureValue.ValueKind == JsonValueKind.String)
            {
                var kind = failureValue.GetString();
                if (kind is "host-assertion" or "unavailable" or "unsupported" or "type-error" or "other" or "no-result" or "no-file")
                    setStage($"browser file path API ({kind})");
            }
            throw new InvalidOperationException();
        }
    }

    private sealed class CdpClient(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        private int _nextId;

        internal async Task<JsonElement> CallAsync(string method, string parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            using var parameterDocument = JsonDocument.Parse(parameters);
            var request = Encoding.UTF8.GetBytes(Write(writer =>
            {
                writer.WriteStartObject(); writer.WriteNumber("id", id); writer.WriteString("method", method);
                writer.WritePropertyName("params"); parameterDocument.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }).GetRawText());
            await socket.SendAsync(request, WebSocketMessageType.Text, true, cancellationToken);
            while (true)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult received;
                do
                {
                    var buffer = new byte[8192];
                    received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                    if (received.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException();
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                using var response = JsonDocument.Parse(message.ToArray());
                if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                if (response.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException();
                return response.RootElement.GetProperty("result").Clone();
            }
        }
    }

    private static void Require(bool condition, string stage)
    {
        if (!condition) throw new InvalidOperationException(stage);
    }

    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);

    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) { write(writer); writer.Flush(); }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}
