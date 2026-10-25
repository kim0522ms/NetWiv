using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Platform.Desktop;

/// <summary>NativeAOT desktop runtime backed by the pinned wry, tao, muda and tray-icon libraries.</summary>
public sealed partial class DesktopRuntime : IDesktopRuntime
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<RuntimeInvokeResponse>> _requests = new();
    private readonly ConcurrentDictionary<ulong, Action> _actions = new();
    private nint _native;
    private RuntimeContext? _context;
    private Exception? _callbackFailure;
    private long _nextRequest;
    private int _uiThread;
    private int _exitCode;
    private bool _started;
    public string? Backend { get; private set; }

    public unsafe void Run(RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Intel macOS is retained only for unsupported, locally built native backends.
        if (OperatingSystem.IsMacOS()
            ? RuntimeInformation.ProcessArchitecture is not (Architecture.Arm64 or Architecture.X64)
            : (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Official NetWiv packages support Windows x64, Linux x64 and macOS arm64.");
        if (OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Call NetWiv Run from an [STAThread] Main method.");
        var config = RuntimeJson.Config(context);
        GCHandle handle = default;
        lock (_gate)
        {
            if (_started) throw new InvalidOperationException("A DesktopRuntime instance can run only once.");
            try
            {
                var abi = NativeMethods.AbiVersion();
                if (abi != 3) throw new InvalidOperationException($"NetWiv native ABI {abi} does not match managed ABI 3. Restore matching NetWiv packages.");
                handle = GCHandle.Alloc(this);
                _native = NativeMethods.Create(&OnCallback, GCHandle.ToIntPtr(handle));
                if (_native == 0) throw NativeError("create runtime");
            }
            catch (DllNotFoundException exception)
            {
                if (handle.IsAllocated) handle.Free();
                throw new InvalidOperationException("The NetWiv native library or an OS dependency is missing. Restore NetWiv.Platform.Desktop for the application's RID and install its documented WebView dependencies.", exception);
            }
            catch (Exception exception) when (exception is EntryPointNotFoundException or BadImageFormatException)
            {
                if (handle.IsAllocated) handle.Free();
                throw new InvalidOperationException("The NetWiv native library has an incompatible ABI or architecture. Restore the matching architecture NetWiv.Platform.Desktop native assets; managed and native components must come from the same package version.", exception);
            }
            catch
            {
                if (handle.IsAllocated) handle.Free();
                throw;
            }
            _context = context;
            _uiThread = Environment.CurrentManagedThreadId;
            _started = true;
        }
        try
        {
            fixed (byte* bytes = config)
            {
                int status = NativeMethods.Run(_native, bytes, (nuint)config.Length);
                if (_callbackFailure is not null) ExceptionDispatchInfo.Capture(_callbackFailure).Throw();
                Check(status, "run event loop");
            }
        }
        finally
        {
            lock (_gate)
            {
                NativeMethods.Destroy(_native);
                _native = 0;
                _context = null;
                _uiThread = 0;
                handle.Free();
            }
            foreach (var entry in _requests)
                if (_requests.TryRemove(entry.Key, out var completion))
                    completion.TrySetException(new OperationCanceledException("The NetWiv runtime stopped before the operation completed."));
            _actions.Clear();
            Environment.ExitCode = _exitCode;
        }
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            EnsureRunning();
            if (Environment.CurrentManagedThreadId == _uiThread) { action(); return; }
            ulong id = NextId();
            _actions[id] = action;
            try { Check(NativeMethods.Dispatch(_native, id), "dispatch UI action"); }
            catch { _actions.TryRemove(id, out _); throw; }
        }
    }

    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        InvokeAsync(command, args, new RuntimeCommandContext("native", new Uri("netwiv://native"), true, "native", IsBackend: true), cancellationToken);

    private static string ResourceDirectory()
    {
        var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (OperatingSystem.IsMacOS() && Path.GetFileName(directory) == "MacOS" &&
            Path.GetFileName(Path.GetDirectoryName(directory)) == "Contents" &&
            Path.GetDirectoryName(Path.GetDirectoryName(directory))?.EndsWith(".app", StringComparison.OrdinalIgnoreCase) == true)
            return Path.GetFullPath(Path.Combine(directory, "..", "Resources"));
        return directory;
    }

    public async Task<string> ResolveDirectoryAsync(int directory, string? path, string identifier, CancellationToken cancellationToken = default)
    {
        var args = JsonValue.Parse($$"""{"directory":{{directory}},"path":{{JsonValue.From(path).GetRawText()}},"identifier":{{JsonValue.From(identifier).GetRawText()}},"resourceDirectory":{{JsonValue.From(ResourceDirectory()).GetRawText()}}}""");
        return (await InvokeAsync("plugin:path|resolve_directory", args, cancellationToken).ConfigureAwait(false)).GetString()!;
    }

    public async Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default)
    {
        var response = await InvokeResponseAsync(command, args, context, cancellationToken).ConfigureAwait(false);
        if (response.IsBinary) throw new InvokeException("The native operation returned binary data; use InvokeResponseAsync.");
        return response.Json;
    }

    public unsafe Task<RuntimeInvokeResponse> InvokeResponseAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.IsAuthorized) throw new InvokeException("Native commands require Core authorization.");
        byte[] json = RuntimeJson.Command(command, args, context, _context?.ResolveResource);
        Task<RuntimeInvokeResponse> response;
        ulong id;
        lock (_gate)
        {
            EnsureRunning();
            cancellationToken.ThrowIfCancellationRequested();
            id = NextId();
            var completion = new TaskCompletionSource<RuntimeInvokeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests[id] = completion;
            try
            {
                fixed (byte* bytes = json)
                    Check(NativeMethods.Submit(_native, id, bytes, (nuint)json.Length, null, 0), "submit native operation");
            }
            catch { _requests.TryRemove(id, out _); throw; }
            response = completion.Task;
        }
        // Native dialogs and script results support cancelling an admitted request. Other UI mutations
        // retain their result even after cancellation, including created resource handles.
        return cancellationToken.CanBeCanceled && (command is "plugin:dialog|open" or "plugin:dialog|save" or "plugin:dialog|message" or "netwiv:evaluate_script")
            ? AwaitCancellableResponseAsync(id, response, cancellationToken) : response;
    }

    private async Task<RuntimeInvokeResponse> AwaitCancellableResponseAsync(ulong id, Task<RuntimeInvokeResponse> response, CancellationToken cancellationToken)
    {
        // Register and dispose outside _gate: disposal can wait for a cancellation
        // callback that needs that lock to enqueue the native cancellation request.
        using var registration = cancellationToken.UnsafeRegister(static state =>
        {
            var (runtime, requestId) = ((DesktopRuntime, ulong))state!;
            runtime.CancelRequest(requestId);
        }, (this, id));
        try { return await response.ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private void CancelRequest(ulong id)
    {
        lock (_gate)
        {
            if (_native == 0 || !_requests.ContainsKey(id)) return;
            try { Check(NativeMethods.Cancel(_native, id), "cancel native request"); }
            catch (Exception error)
            {
                // CancellationToken callbacks must not throw on the cancelling thread.
                if (_requests.TryRemove(id, out var completion)) completion.TrySetException(error);
            }
        }
    }

    public void EvaluateScript(string webviewLabel, string script) => EvaluateScript(webviewLabel, script, 0);

    public unsafe void EvaluateScript(string webviewLabel, string script, ulong documentId)
    {
        byte[] label = Encoding.UTF8.GetBytes(webviewLabel);
        byte[] code = Encoding.UTF8.GetBytes(script);
        lock (_gate)
        {
            // A completion racing shutdown must never re-enter a destroyed native runtime.
            if (_native == 0 && _started) return;
            EnsureRunning();
            fixed (byte* labelBytes = label)
            fixed (byte* codeBytes = code)
                Check(NativeMethods.Eval(_native, labelBytes, (nuint)label.Length, documentId, codeBytes, (nuint)code.Length), "evaluate script");
        }
    }

    public void Exit(int exitCode = 0)
    {
        lock (_gate)
        {
            if (_native == 0 && _started) return;
            EnsureRunning();
            _exitCode = exitCode;
            Check(NativeMethods.Exit(_native, exitCode), "exit runtime");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int OnCallback(nint user, uint kind, ulong id, byte* json, nuint jsonLength, byte* binary, nuint binaryLength)
    {
        DesktopRuntime? runtime = null;
        try
        {
            runtime = (DesktopRuntime)GCHandle.FromIntPtr(user).Target!;
            if (kind == 4)
            {
                if (runtime._actions.TryRemove(id, out var action)) action();
                return 0;
            }
            using var document = JsonDocument.Parse(new ReadOnlySpan<byte>(json, checked((int)jsonLength)).ToArray());
            return runtime.HandleCallback(kind, id, document.RootElement, new ReadOnlySpan<byte>(binary, checked((int)binaryLength)));
        }
        catch (Exception exception)
        {
            if (runtime is not null)
            {
                Interlocked.CompareExchange(ref runtime._callbackFailure, exception, null);
                try { runtime.Exit(1); } catch { /* Never unwind a managed exception through Rust. */ }
            }
            return 0;
        }
    }

    private int HandleCallback(uint kind, ulong id, JsonElement json, ReadOnlySpan<byte> binary)
    {
        var context = _context ?? throw new InvalidOperationException("Native callback outside the runtime lifetime.");
        switch (kind)
        {
            case 1:
                context.OnMessage(new RuntimeMessage(json.GetProperty("webviewLabel").GetString()!,
                    new Uri(json.GetProperty("source").GetString()!), json.GetProperty("json").GetString()!,
                    json.GetProperty("windowLabel").GetString(), json.GetProperty("documentId").GetUInt64()));
                break;
            case 2:
                string name = json.GetProperty("eventName").GetString()!;
                string? label = json.TryGetProperty("label", out var labelValue) ? labelValue.GetString() : null;
                RuntimeEventTarget? target = null;
                if (json.TryGetProperty("target", out var targetValue) && targetValue.ValueKind == JsonValueKind.Object)
                    target = new(targetValue.GetProperty("kind").GetString()!, targetValue.TryGetProperty("label", out var targetLabel) ? targetLabel.GetString() : null);
                bool preventClose = name == "tauri://close-requested" && (context.HasEventListeners?.Invoke(name, label) ?? false);
                context.OnEvent(new RuntimeEvent(name, label, json.GetProperty("payload").Clone(), target));
                return preventClose ? 1 : 0;
            case 3:
                if (_requests.TryRemove(id, out var completion))
                {
                    if (!json.GetProperty("ok").GetBoolean())
                    {
                        if (json.TryGetProperty("cancelled", out var cancelled) && cancelled.GetBoolean()) completion.TrySetCanceled();
                        else completion.TrySetException(new InvokeException(json.GetProperty("error")));
                    }
                    else if (json.TryGetProperty("isBinary", out var isBinary) && isBinary.GetBoolean())
                        completion.TrySetResult(RuntimeInvokeResponse.FromBinary(binary.ToArray()));
                    else completion.TrySetResult(RuntimeInvokeResponse.FromJson(json.GetProperty("value")));
                }
                break;
            case 5:
                RespondToAsset(id, json, binary, context);
                break;
            case 6:
                context.OnDocumentChanged?.Invoke(new RuntimeDocumentChange(json.GetProperty("webviewLabel").GetString()!,
                    json.GetProperty("documentId").GetUInt64(), json.GetProperty("closed").GetBoolean()));
                break;
            case 7:
                throw new InvalidOperationException(json.GetProperty("error").GetString());
            case 8:
                Backend = json.TryGetProperty("backend", out var backend) ? backend.GetString() : null;
                context.OnReady?.Invoke();
                break;
            default:
                throw new InvalidOperationException($"Unknown NetWiv native callback kind {kind}.");
        }
        return 0;
    }

    private void RespondToAsset(ulong id, JsonElement json, ReadOnlySpan<byte> requestBody, RuntimeContext context)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json.TryGetProperty("headers", out var values))
            foreach (var header in values.EnumerateObject()) headers[header.Name] = header.Value.GetString()!;
        var request = new AssetRequest(new Uri(json.GetProperty("uri").GetString()!),
            json.GetProperty("method").GetString()!, headers, requestBody.ToArray(),
            json.TryGetProperty("windowOrigin", out var origin) ? origin.GetString() : null,
            json.TryGetProperty("webviewLabel", out var label) ? label.GetString() : null,
            json.TryGetProperty("windowLabel", out var window) ? window.GetString() : null,
            json.TryGetProperty("documentId", out var document) ? document.GetUInt64() : 0);
        _ = RespondToAssetAsync(id, request, context);
    }

    private async Task RespondToAssetAsync(ulong id, AssetRequest request, RuntimeContext context)
    {
        AssetResponse response;
        try
        {
            response = (context.ResolveUriScheme is null ? null : await context.ResolveUriScheme(request)) ??
                (request.Uri.Scheme == "ipc" && context.InvokeProtocol is not null &&
                    context.UriSchemeProtocols?.Contains("ipc", StringComparer.Ordinal) != true
                ? await context.InvokeProtocol(request)
                : context.ResolveAsset(request) ?? new AssetResponse(404, "text/plain", "Not found"u8.ToArray()));
        }
        catch { response = new AssetResponse(500, "text/plain", "Asset resolution failed"u8.ToArray()); }
        try { SendAssetResponse(id, response); }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref _callbackFailure, error, null);
            try { Exit(1); } catch { /* Shutdown must not leave an unobserved async callback exception. */ }
        }
    }

    private unsafe void SendAssetResponse(ulong id, AssetResponse response)
    {
        byte[] metadata = RuntimeJson.Asset(response);
        lock (_gate)
        {
            if (_native == 0) return; // Native teardown already released its pending responders.
            fixed (byte* data = metadata)
            fixed (byte* body = response.Body.Span)
                Check(NativeMethods.RespondAsset(_native, id, data, (nuint)metadata.Length, body, (nuint)response.Body.Length), "respond to asset request");
        }
    }

    private ulong NextId() => checked((ulong)Interlocked.Increment(ref _nextRequest));
    private void EnsureRunning() { if (_native == 0) throw new InvalidOperationException("The NetWiv native runtime is not running."); }
    private static void Check(int status, string operation) { if (status != 0) throw NativeError(operation); }
    private static unsafe Exception NativeError(string operation)
    {
        var error = NativeMethods.LastError();
        try { return new InvalidOperationException($"Could not {operation}: {Encoding.UTF8.GetString(new ReadOnlySpan<byte>(error.Data, checked((int)error.Length)))}"); }
        finally { NativeMethods.Free(error); }
    }
}
