using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class AppHandle : IDisposable
{
    private readonly IDesktopRuntime _runtime;
    private readonly IReadOnlyList<WindowOptions> _windowOptions;
    private readonly StateStore _state;
    private readonly CommandRegistry _commands;
    private readonly AclAuthorizer _authorizer;
    private readonly Dictionary<string, Window> _windows;
    private readonly Dictionary<string, Webview> _webviews;
    private readonly object _registryGate = new();
    private readonly Func<AssetRequest, AssetResponse?> _assetResolver;
    private readonly IReadOnlyList<IPlugin> _plugins;
    private readonly bool _freezePrototype;
    private readonly IReadOnlyDictionary<string, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>>> _uriSchemeProtocols;
    private readonly string _invokeKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentDictionary<long, JsEventListener> _jsEventListeners = new();
    private readonly ConcurrentDictionary<string, RuntimeDocumentChange> _documents = new(StringComparer.Ordinal);
    private long _nextJsEventId;
    private bool _disposed;
    private IServiceProvider? _services;
    private IServiceScopeFactory? _scopeFactory;
    private ILogger _ipcLogger = NullLogger.Instance;
    private ILogger _commandLogger = NullLogger.Instance;
    internal bool DebugAssertions { get; set; }
    internal bool ExitOnLastWindowClose { get; set; } = true;

    internal AppHandle(
        IDesktopRuntime runtime,
        AppMetadata metadata,
        IReadOnlyList<WindowOptions> windows,
        StateStore state,
        CommandRegistry commands,
        IReadOnlyList<PermissionDefinition> permissions,
        IReadOnlyList<Capability> capabilities,
        IReadOnlySet<string> allowedOrigins,
        bool hasAppCommandManifest,
        Func<AssetRequest, AssetResponse?> assetResolver,
        IReadOnlyList<IPlugin> plugins,
        bool freezePrototype,
        IReadOnlyDictionary<string, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>>> uriSchemeProtocols)
    {
        _runtime = runtime;
        _windowOptions = windows;
        _state = state;
        _commands = commands;
        _assetResolver = assetResolver;
        _plugins = plugins;
        _freezePrototype = freezePrototype;
        _uriSchemeProtocols = uriSchemeProtocols;
        Metadata = metadata;
        Resources = new ResourceTable();
        Events = new EventBus();
        _authorizer = new AclAuthorizer(BuiltInCommands.Permissions.Concat(permissions).ToArray(), capabilities, allowedOrigins, hasAppCommandManifest,
            localProtocols: uriSchemeProtocols.Keys.ToHashSet(StringComparer.Ordinal));
        _windows = windows.ToDictionary(static option => option.Label,
            option => option.Webviews is null ? (Window)new WebviewWindow(this, option.Label) : new Window(this, option.Label), StringComparer.Ordinal);
        _webviews = windows
            .SelectMany(option => option.Webviews is { Count: > 0 }
                ? option.Webviews.Select(webview => new KeyValuePair<string, Webview>(webview.Label, new Webview(this, webview.Label, option.Label)))
                : option.Webviews is null ? [new KeyValuePair<string, Webview>(option.Label, ((WebviewWindow)_windows[option.Label]).Webview)] : [])
            .ToDictionary(StringComparer.Ordinal);
        BuiltInCommands.Register(this, commands);
    }

    public AppMetadata Metadata { get; }
    public ImageData? DefaultWindowIcon { get; internal set; }
    internal byte[]? EncodedDefaultWindowIcon { get; set; }
    internal bool AssetProtocolEnabled { get; set; }
    internal IsolationPattern? Isolation { get; set; }
    internal Func<AssetRequest, AssetResponse, AssetResponse>? AssetResponsePolicy { get; set; }
    internal FileScope? FileAssetScope { get; set; }
    public FileScope AssetProtocolScope() => FileAssetScope ?? throw new InvalidOperationException("The asset protocol is not enabled.");
    public FileScope? TryAssetProtocolScope() => FileAssetScope;
    /// <summary>Resolves a bundled application asset with the configured response headers and CSP.</summary>
    public AssetResponse? ResolveAsset(AssetRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        return _assetResolver(request);
    }
    public ResourceTable Resources { get; }
    public EventBus Events { get; }
    /// <summary>The application's root service provider, or an empty provider when none is configured.</summary>
    public IServiceProvider Services => _services ?? EmptyServiceProvider.Instance;
    /// <summary>The logger factory used by NetWiv and its plugins; a no-op factory when none is configured.</summary>
    public ILoggerFactory LoggerFactory { get; private set; } = NullLoggerFactory.Instance;

    internal void ConfigureServices(IServiceProvider? services, ILoggerFactory? loggerFactory)
    {
        _services = services;
        _scopeFactory = services?.GetService<IServiceScopeFactory>();
        LoggerFactory = loggerFactory ?? services?.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        _ipcLogger = LoggerFactory.CreateLogger(CoreLog.IpcCategory);
        _commandLogger = LoggerFactory.CreateLogger(CoreLog.CommandCategory);
    }
    public IReadOnlyCollection<Window> Windows { get { lock (_registryGate) return _windows.Values.Select(window => window is WebviewWindow ? new Window(this, window.Label) : window).ToArray(); } }
    public IReadOnlyCollection<WebviewWindow> WebviewWindows { get { lock (_registryGate) return _webviews.Keys.Select(GetWebviewWindow).OfType<WebviewWindow>().ToArray(); } }
    public IReadOnlyCollection<Webview> Webviews { get { lock (_registryGate) return _webviews.Values.ToArray(); } }
    public State<T> State<T>() where T : notnull => _state.Get<T>();
    public State<T>? TryState<T>() where T : notnull => _state.TryGet<T>();
    public T? TryPlugin<T>() where T : class, IPlugin => _plugins.OfType<T>().FirstOrDefault();
    public bool Manage<T>(T value) where T : notnull => _state.Manage(value);
    public bool Unmanage<T>([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) where T : notnull => _state.Unmanage(out value);
    /// <summary>Returns the managed WebView window, or null when the label is absent or belongs to a bare window.</summary>
    public WebviewWindow? GetWebviewWindow(string label)
    {
        lock (_registryGate)
        {
            if (!_webviews.TryGetValue(label, out var webview) || webview.ParentWindowLabel != label ||
                !_windows.TryGetValue(label, out var window) ||
                _webviews.Values.Any(view => view.ParentWindowLabel == label && view.Label != label)) return null;
            return window is WebviewWindow combined && ReferenceEquals(combined.Webview, webview)
                ? combined : new WebviewWindow(this, label, webview);
        }
    }
    /// <summary>Returns the managed window, or null when the label is absent.</summary>
    public Window? GetWindow(string label)
    {
        lock (_registryGate) return _windows.TryGetValue(label, out var window)
            ? window is WebviewWindow ? new Window(this, label) : window
            : null;
    }
    /// <summary>Returns the managed WebView, or null when the label is absent.</summary>
    public Webview? GetWebview(string label)
    {
        lock (_registryGate) return _webviews.GetValueOrDefault(label);
    }

    internal string? WebviewParentLabel(string label)
    {
        lock (_registryGate) return _webviews.GetValueOrDefault(label)?.ParentWindowLabel;
    }

    internal Window RegisterWindow(string label)
    {
        WindowBuilder.ValidateLabel(label);
        lock (_registryGate)
        {
            if (!_windows.ContainsKey(label)) _windows.Add(label, new Window(this, label));
            return GetWindow(label)!;
        }
    }

    internal WebviewWindow RegisterWebviewWindow(string label)
    {
        WindowBuilder.ValidateLabel(label);
        lock (_registryGate)
        {
            if (_windows.TryGetValue(label, out var existing) && existing is WebviewWindow existingWindow)
            {
                _webviews.TryAdd(label, existingWindow.Webview);
                return existingWindow;
            }
            var window = new WebviewWindow(this, label);
            _windows[label] = window;
            _webviews.TryAdd(label, window.Webview);
            return window;
        }
    }

    internal Webview RegisterWebview(string label, string? windowLabel = null)
    {
        WindowBuilder.ValidateLabel(label);
        lock (_registryGate)
        {
            if (_webviews.TryGetValue(label, out var existing)) return existing;
            var webview = new Webview(this, label, windowLabel);
            _webviews.Add(label, webview);
            return webview;
        }
    }

    internal bool UnregisterWebviewWindow(string label)
    {
        lock (_registryGate)
        {
            // A window and an unrelated webview can legally share a label.
            // Native owners report closed documents before the window destruction event.
            return _windows.Remove(label);
        }
    }

    internal bool UnregisterWebview(string label)
    {
        lock (_registryGate)
        {
            RemoveJsEventListeners(label);
            return _webviews.Remove(label);
        }
    }

    public ValueTask<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        InvokeJsonAsync(command, args, "native", "native", new Uri("netwiv://native"), true, cancellationToken);

    public ValueTask<RuntimeInvokeResponse> InvokeResponseAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        InvokeCoreAsync(command, args, "native", "native", new Uri("netwiv://native"), true, cancellationToken);

    internal Task<JsonElement> InvokeForTargetAsync(string command, JsonElement args, string label, CancellationToken cancellationToken) =>
        InvokeJsonAsync(command, args, label, label, new Uri("netwiv://native"), true, cancellationToken).AsTask();

    public void Dispatch(Action action) => _runtime.Dispatch(action);
    public void EvaluateScript(string webviewLabel, string script) => _runtime.EvaluateScript(webviewLabel, script);
    public void Exit(int exitCode = 0)
    {
        lock (_nativeEventGate) _exitRequestGeneration++;
        _runtime.Exit(exitCode);
    }

    public Task<string> ResolveDirectoryAsync(int directory, string? path = null, CancellationToken cancellationToken = default) =>
        _runtime.ResolveDirectoryAsync(directory, path, Metadata.Identifier, cancellationToken);

    internal async ValueTask<RuntimeInvokeResponse> InvokePlatformAsync(string command, JsonElement args, InvokeContext context, CancellationToken cancellationToken)
    {
        RequireGuestOrTrustedBackend(context);
        return await _runtime.InvokeResponseAsync(command, args, new RuntimeCommandContext(context.WebviewLabel, context.Source,
            true, context.WindowLabel, context.Scope, context.DocumentId, context.IsTrusted), cancellationToken);
    }

    public void Emit(string eventName, JsonElement payload, string? label = null)
        => Emit(eventName, payload, label is null ? RuntimeEventTarget.Any : new("AnyLabel", label));

    public void Emit(string eventName, JsonElement payload, RuntimeEventTarget target)
        => EmitMatching(eventName, payload, target, listener => RuntimeEventTarget.Matches(listener, target));

    private void EmitMatching(string eventName, JsonElement payload, RuntimeEventTarget target, Func<RuntimeEventTarget, bool> matches)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EventName.Validate(eventName);
        ArgumentNullException.ThrowIfNull(target);
        Events.Publish(eventName, target, payload, matches);
        foreach (var listener in _jsEventListeners.Values)
        {
            if (!string.Equals(listener.EventName, eventName, StringComparison.Ordinal) ||
                !matches(listener.Target))
                continue;
            var eventPayload = JsonValue.Write(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("event", eventName); writer.WriteNumber("id", listener.EventId);
                writer.WritePropertyName("payload"); payload.WriteTo(writer); writer.WriteEndObject();
            });
            // Tauri event::emit_js_script embeds payloads as object literals,
            // independently of IPC's size-dependent JSON.parse optimization.
            RunRawCallback(listener.WebviewLabel, listener.CallbackId, eventPayload.GetRawText(), listener.DocumentId);
        }
    }

    internal RuntimeContext CreateRuntimeContext(Action? setup = null) => new(
        _windowOptions,
        InitializationScript.Create(_invokeKey, _freezePrototype, Isolation?.Scheme) +
        string.Concat(_plugins.Select(plugin => plugin.InitializationScript is { Length: > 0 } script ? "\n" + script : "")),
        OnRuntimeMessage,
        runtimeEvent =>
        {
            if (_disposed) return;
            if (runtimeEvent.EventName == "netwiv://webview-reparented" && runtimeEvent.Label is { } label)
            {
                lock (_registryGate)
                    if (_webviews.TryGetValue(label, out var webview)) webview.ParentWindowLabel = runtimeEvent.Payload.GetProperty("windowLabel").GetString()!;
                return;
            }
            SynchronizeWindowRegistry(runtimeEvent);
            PublishNativeEvent(runtimeEvent);
            if (_disposed) return;
            foreach (var plugin in _plugins) plugin.OnEvent(this, runtimeEvent);
            var target = runtimeEvent.Target ?? (runtimeEvent.Label is null ? RuntimeEventTarget.Any : new("AnyLabel", runtimeEvent.Label));
            if (target.Kind == "Window" && runtimeEvent.EventName is
                "tauri://resize" or "tauri://move" or "tauri://close-requested" or "tauri://destroyed" or
                "tauri://focus" or "tauri://blur" or "tauri://scale-change" or "tauri://theme-changed")
                EmitMatching(runtimeEvent.EventName, runtimeEvent.Payload, target, listener => listener.Kind == "Any" ||
                    listener.Kind is "Window" or "WebviewWindow" && listener.Label == target.Label);
            else Emit(runtimeEvent.EventName, runtimeEvent.Payload, target);
        },
        _assetResolver,
        Resources.TryGet,
        HasJsEventListeners,
        change =>
        {
            ChangeDocument(change);
            RemoveJsEventListeners(change.WebviewLabel);
            foreach (var plugin in _plugins) plugin.OnDocumentChanged(this, change);
            if (change.Closed) UnregisterWebview(change.WebviewLabel);
        },
        () =>
        {
            foreach (var plugin in _plugins) plugin.Setup(this);
            setup?.Invoke();
        }, DefaultWindowIcon,
        BuiltInCommands.ResolveDirectory((int)BaseDirectory.LocalData, Metadata.Identifier),
        BuiltInCommands.ResolveDirectory((int)BaseDirectory.AppLocalData, Metadata.Identifier), AssetProtocolEnabled, Isolation?.Scheme, OnProtocolInvokeAsync,
        _uriSchemeProtocols.Keys.ToArray(), ResolveUriSchemeAsync)
        {
            UsesDefaultAppIdentifier = string.Equals(Metadata.Identifier, AppMetadata.Default.Identifier, StringComparison.OrdinalIgnoreCase),
            ExitOnLastWindowClose = ExitOnLastWindowClose,
            ApplyAssetPolicy = AssetResponsePolicy,
            EncodedDefaultWindowIcon = EncodedDefaultWindowIcon is { } icon ? new ReadOnlyMemory<byte>(icon) : (ReadOnlyMemory<byte>?)null
        };

    private async ValueTask<AssetResponse?> ResolveUriSchemeAsync(AssetRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scheme = request.Uri.Scheme;
        if (OperatingSystem.IsWindows() && scheme is "http" or "https" && request.Uri.Host.EndsWith(".localhost", StringComparison.Ordinal))
            scheme = request.Uri.Host[..^".localhost".Length];
        return _uriSchemeProtocols.TryGetValue(scheme, out var handler)
            ? await handler(new(this, request.WebviewLabel ?? ""), request).ConfigureAwait(false)
            : null;
    }

    private void SynchronizeWindowRegistry(RuntimeEvent runtimeEvent)
    {
        if (runtimeEvent.EventName == "tauri://window-created" &&
            runtimeEvent.Payload.TryGetProperty("label", out var windowLabel))
        {
            string label = windowLabel.GetString()!;
            lock (_registryGate)
                if (!_windows.ContainsKey(label)) _windows.Add(label,
                    runtimeEvent.Payload.TryGetProperty("isWebviewWindow", out var isWebviewWindow) && !isWebviewWindow.GetBoolean()
                        ? new Window(this, label) : new WebviewWindow(this, label));
        }
        else if (runtimeEvent.EventName == "tauri://webview-created" &&
            runtimeEvent.Payload.TryGetProperty("label", out var webviewLabel))
        {
            string label = webviewLabel.GetString()!;
            lock (_registryGate)
                if (!_webviews.ContainsKey(label)) _webviews.Add(label, new Webview(this, label,
                    runtimeEvent.Payload.TryGetProperty("windowLabel", out var parent) ? parent.GetString() : null));
        }
        else if (runtimeEvent.EventName == "tauri://destroyed" && runtimeEvent.Label is { } label &&
                 runtimeEvent.Target?.Kind is "Window" or "WebviewWindow")
            UnregisterWebviewWindow(label);
    }

    internal void RunCallback(string webviewLabel, uint callbackId, JsonElement payload, ulong documentId = 0)
        => RunRawCallback(webviewLabel, callbackId, CallbackScript.Serialize(payload), documentId);

    private void RunRawCallback(string webviewLabel, uint callbackId, string payload, ulong documentId)
    {
        if (!IsCurrentDocument(webviewLabel, documentId)) return;
        _runtime.EvaluateScript(webviewLabel, $"window.__TAURI_INTERNALS__.runCallback({callbackId},{payload});", documentId);
    }

    private bool IsCurrentDocument(string webviewLabel, ulong documentId) =>
        !_disposed && (documentId == 0 || !_documents.TryGetValue(webviewLabel, out var current) || (!current.Closed && current.DocumentId == documentId));

    internal long AddJsEventListener(string eventName, string webviewLabel, uint callbackId, RuntimeEventTarget target, ulong documentId = 0)
    {
        var id = Interlocked.Increment(ref _nextJsEventId);
        if (!IsCurrentDocument(webviewLabel, documentId)) throw new InvokeException("The originating document has been replaced.");
        _jsEventListeners[id] = new(id, eventName, webviewLabel, callbackId, target, documentId);
        _runtime.EvaluateScript(webviewLabel,
            $"window.__TAURI_EVENT_PLUGIN_INTERNALS__.registerListener({id},{callbackId});", documentId);
        return id;
    }

    internal bool RemoveJsEventListener(string eventName, long id) =>
        _jsEventListeners.TryGetValue(id, out var listener) && listener.EventName == eventName &&
        _jsEventListeners.TryRemove(new KeyValuePair<long, JsEventListener>(id, listener));

    private bool HasJsEventListeners(string eventName, string? targetLabel) =>
        _jsEventListeners.Values.Any(listener =>
            string.Equals(listener.EventName, eventName, StringComparison.Ordinal) &&
            listener.Target.Kind is "Window" or "WebviewWindow" && listener.Target.Label == targetLabel) ||
        Events.HasListener(eventName, target => target.Kind is "Window" or "WebviewWindow" && target.Label == targetLabel);

    private void RemoveJsEventListeners(string webviewLabel)
    {
        foreach (var listener in _jsEventListeners)
        {
            if (string.Equals(listener.Value.WebviewLabel, webviewLabel, StringComparison.Ordinal))
                _jsEventListeners.TryRemove(listener.Key, out _);
        }
    }

    internal Task<JsonElement> InvokeRuntimeDirectAsync(
        string command,
        JsonElement args,
        InvokeContext context,
        CancellationToken cancellationToken)
    {
        RequireGuestOrTrustedBackend(context);
        return _runtime.InvokeAsync(command, args,
            new RuntimeCommandContext(context.WebviewLabel, context.Source, true, context.WindowLabel, context.Scope, context.DocumentId, context.IsTrusted), cancellationToken);
    }

    private async void OnRuntimeMessage(RuntimeMessage message)
    {
        if (!IsCurrentDocument(message.WebviewLabel, message.DocumentId)) return;
        try
        {
            using var document = JsonDocument.Parse(message.Json);
            var root = document.RootElement;
            var callback = root.GetProperty("callback").GetUInt32();
            var error = root.GetProperty("error").GetUInt32();
            if (!root.TryGetProperty("__NETWIV_INVOKE_KEY__", out var key) || key.GetString() != _invokeKey)
            {
                CoreLog.RejectedRequest(_ipcLogger, message.WebviewLabel, "invalid invoke key");
                RunCallback(message.WebviewLabel, error, JsonValue.From("invalid invoke key"), message.DocumentId);
                return;
            }

            try
            {
                var command = root.GetProperty("cmd").GetString() ?? throw new InvokeException("IPC command is missing.");
                var args = root.TryGetProperty("payload", out var payload) ? payload.Clone() : JsonValue.EmptyObject;
                var fallback = root.TryGetProperty("options", out var transportOptions) && transportOptions.ValueKind == JsonValueKind.Object &&
                    transportOptions.TryGetProperty("customProtocolIpcBlocked", out var blocked) && blocked.ValueKind == JsonValueKind.True;
                var isBinary = Isolation is not null
                    ? args.GetProperty("contentType").GetString() == "application/octet-stream"
                    : root.TryGetProperty("__NETWIV_BODY_TYPE__", out var bodyType) ? bodyType.GetString() == "application/octet-stream" : args.ValueKind == JsonValueKind.Array;
                if (Isolation is not null) args = Isolation.Decrypt(args);
                var jsonText = root.TryGetProperty("__NETWIV_JSON__", out var jsonMode) && jsonMode.ValueKind == JsonValueKind.True;
                if (jsonText)
                {
                    if (isBinary || args.ValueKind != JsonValueKind.String)
                        throw new InvokeException("invokeJson requires a JSON object encoded as text.");
                    using var parsed = JsonDocument.Parse(args.GetString()!);
                    if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvokeException("invokeJson requires a JSON object encoded as text.");
                    args = parsed.RootElement.Clone();
                }
                if (fallback) isBinary = false;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object &&
                    options.TryGetProperty("headers", out var configuredHeaders) && configuredHeaders.ValueKind == JsonValueKind.Object)
                    foreach (var header in configuredHeaders.EnumerateObject()) headers[header.Name] = header.Value.GetString()!;
                if (!fallback)
                {
                    headers["content-type"] = isBinary && Isolation is null ? "application/octet-stream" : "application/json";
                    headers["tauri-callback"] = callback.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    headers["tauri-error"] = error.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    headers["tauri-invoke-key"] = _invokeKey;
                    headers["origin"] = message.Source.GetLeftPart(UriPartial.Authority);
                }
                var body = isBinary ? InvokeBody.FromBytes(args.EnumerateArray().Select(static item => item.GetByte()).ToArray()) : InvokeBody.FromJson(args);
                var result = await InvokeCoreAsync(command, args, message.WindowLabel ?? message.WebviewLabel, message.WebviewLabel, message.Source, false, CancellationToken.None, message.DocumentId, new Request(body, headers));
                if (jsonText)
                {
                    if (result.IsBinary) throw new InvokeException("invokeJson does not support binary responses; use invoke.");
                    RunCallback(message.WebviewLabel, callback, JsonValue.From(result.Json.GetRawText()), message.DocumentId);
                }
                else RunInvokeCallback(message.WebviewLabel, callback, result, message.DocumentId);
            }
            catch (InvokeException exception)
            {
                RunCallback(message.WebviewLabel, error, exception.ErrorPayload, message.DocumentId);
            }
            catch (Exception exception)
            {
                RunCallback(message.WebviewLabel, error, JsonValue.From(exception.Message), message.DocumentId);
            }
        }
        catch (Exception exception)
        {
            // Malformed messages without readable callback identifiers cannot be answered safely.
            CoreLog.MalformedMessage(_ipcLogger, message.WebviewLabel, exception);
        }
    }

    private async ValueTask<JsonElement> InvokeJsonAsync(
        string command, JsonElement args, string windowLabel, string webviewLabel, Uri source, bool trusted, CancellationToken cancellationToken)
    {
        var response = await InvokeCoreAsync(command, args, windowLabel, webviewLabel, source, trusted, cancellationToken);
        if (response.IsBinary) throw new InvokeException("The command returned binary data; use InvokeResponseAsync.");
        return response.Json;
    }

    private async ValueTask<RuntimeInvokeResponse> InvokeCoreAsync(
        string command, JsonElement args, string windowLabel, string webviewLabel, Uri source, bool trusted, CancellationToken cancellationToken, ulong documentId = 0, Request? request = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var authorization = trusted
            ? AuthorizationResult.AllowedWithoutScope
            : _authorizer.Authorize(new(command, windowLabel, webviewLabel, source));
        if (!authorization.Allowed)
        {
            CoreLog.CommandDenied(_commandLogger, command, windowLabel, webviewLabel, source.GetLeftPart(UriPartial.Authority));
            throw new InvokeException(DebugAssertions
                ? _authorizer.DebugMessage(new(command, windowLabel, webviewLabel, source))
                : $"Command {command} not allowed by ACL");
        }

        var lifetime = BeginInvocation(webviewLabel, documentId, trusted, cancellationToken);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (_commands.TryGet(command, out var handler))
            {
                // Like an HTTP request, each command invocation owns one service scope.
                var scope = _scopeFactory?.CreateAsyncScope();
                try
                {
                    var context = new InvokeContext(this, windowLabel, webviewLabel, source, trusted, authorization.Scope, documentId,
                        request ?? new Request(InvokeBody.FromJson(args)), scope?.ServiceProvider ?? _services);
                    // A guest invocation enters from the native UI callback. Even an async
                    // C# handler can do substantial synchronous work before its first await.
                    if (!trusted) return await Task.Run(async () => await handler!(context, args, lifetime.Token), lifetime.Token);
                    return await handler!(context, args, lifetime.Token);
                }
                catch (Exception exception) when (exception is not InvokeException and not OperationCanceledException)
                {
                    CoreLog.CommandFailed(_commandLogger, command, exception);
                    throw;
                }
                finally
                {
                    if (scope is { } active) await active.DisposeAsync();
                }
            }
            if (!command.StartsWith("plugin:", StringComparison.Ordinal) || !command.Contains('|'))
                throw new InvokeException($"Command {command} not found");
            return await _runtime.InvokeResponseAsync(command, args, new RuntimeCommandContext(webviewLabel, source, true, windowLabel, authorization.Scope, documentId, trusted), lifetime.Token);
        }
        finally { EndInvocation(lifetime); }
    }

    private void RunInvokeCallback(string webviewLabel, uint callbackId, RuntimeInvokeResponse response, ulong documentId = 0)
    {
        if (!IsCurrentDocument(webviewLabel, documentId)) return;
        if (!response.IsBinary) { RunCallback(webviewLabel, callbackId, response.Json, documentId); return; }
        var bytes = JsonValue.Write(writer =>
        {
            writer.WriteStartArray(); foreach (var value in response.Binary.Span) writer.WriteNumberValue(value); writer.WriteEndArray();
        });
        _runtime.EvaluateScript(webviewLabel,
            $"window.__TAURI_INTERNALS__.runCallback({callbackId},Uint8Array.from({bytes.GetRawText()}).buffer);", documentId);
    }

    public void Dispose()
    {
        CancellationTokenSource[] pending;
        lock (_invocationGate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _invocations.Keys.ToArray();
        }
        foreach (var lifetime in pending) _ = CancelInvocation(lifetime);
        CancelScriptEvaluations(closeAdmission: true);
        Isolation?.Dispose();
        _jsEventListeners.Clear();
        Events.Dispose();
        ClearNativeEventHandlers();
        List<Exception>? errors = null;
        try { Resources.Dispose(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        foreach (var plugin in _plugins)
        {
            try { plugin.Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        try { _state.Dispose(); }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        if (errors is not null) throw new AggregateException("One or more application resources failed to dispose.", errors);
    }

    private sealed record JsEventListener(long EventId, string EventName, string WebviewLabel, uint CallbackId, RuntimeEventTarget Target, ulong DocumentId);
}
