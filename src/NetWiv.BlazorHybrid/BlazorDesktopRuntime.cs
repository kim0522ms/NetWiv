using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid;

/// <summary>Hosts Blazor components using an existing NetWiv desktop runtime.</summary>
/// <remarks>The supplied service provider remains owned by the caller. This adapter supports Windows execution.</remarks>
public sealed class BlazorDesktopRuntime : IDesktopRuntime
{
    internal const string Protocol = "netwiv-blazor";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly IDesktopRuntime _runtime;
    private readonly IServiceProvider _services;
    private readonly BlazorWebViewOptions _options;
    private readonly BlazorAotOptions? _aotOptions;
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private Session? _session;
    private Exception? _failure;
    private bool _started;
    private bool _nativeRunning;
    private bool _accepting;
    private ulong _documentId;
    private bool _documentClosed;

    public BlazorDesktopRuntime(IDesktopRuntime runtime, IServiceProvider services, BlazorWebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        _aotOptions = services.GetService<BlazorAotOptions>()?.Snapshot();
        if (!RuntimeFeature.IsDynamicCodeSupported && _aotOptions is null)
            throw new PlatformNotSupportedException("NativeAOT Hybrid requires AddNetWivBlazorHybridAot with explicit component and interop registration.");
        BlazorWebViewManager.EnsureSupportedFrameworkVersion();
        if (_aotOptions is not null) AotFrameworkVersion.EnsureSupported();
        options.Validate();
        _runtime = runtime;
        _services = services;
        _options = options.Snapshot();
    }

    public void Run(RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("NetWiv Blazor Hybrid currently supports Windows execution.");
        if (context.IsolationProtocol is not null)
            throw new NotSupportedException("NetWiv Blazor Hybrid does not support the isolation security pattern.");
        lock (_gate)
        {
            if (_started) throw new InvalidOperationException("A BlazorDesktopRuntime instance can run only once.");
            _started = true;
            _accepting = true;
        }
        ExceptionDispatchInfo? runFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            var windows = ConfigureWindows(context.Windows);
            // Native window creation can request assets before OnReady.
            GetSession(0)!.Prepared.GetAwaiter().GetResult();
            var wrapped = context with
            {
                Windows = windows,
                UriSchemeProtocols = (context.UriSchemeProtocols ?? []).Append(Protocol).Distinct(StringComparer.Ordinal).ToArray(),
                InitializationScript = context.InitializationScript + "\n" + CreateInitializationScript(),
                OnMessage = message =>
                {
                    if (!message.Json.StartsWith(BlazorBridge.Prefix, StringComparison.Ordinal))
                    {
                        context.OnMessage(message);
                        return;
                    }
                    ReceiveMessage(message);
                },
                OnDocumentChanged = change =>
                {
                    if (change.WebviewLabel == _options.WebviewLabel) ChangeDocument(change);
                    context.OnDocumentChanged?.Invoke(change);
                },
                ResolveUriScheme = request => ResolveAssetAsync(request, context.ResolveUriScheme, context.ApplyAssetPolicy)
            };
            lock (_gate) _nativeRunning = true;
            _runtime.Run(wrapped);
        }
        catch (Exception error) { runFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            lock (_gate) _nativeRunning = false;
            try
            {
                RetireCurrent();
                DrainAsync().GetAwaiter().GetResult();
            }
            catch (Exception error) { cleanupFailure = error; }
        }
        var failure = runFailure?.SourceException ?? _failure;
        if (failure is not null && cleanupFailure is not null)
            throw new AggregateException("The runtime and Blazor cleanup both failed.", failure, cleanupFailure);
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        if (runFailure is not null) runFailure.Throw();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private IReadOnlyList<WindowOptions> ConfigureWindows(IReadOnlyList<WindowOptions> windows)
    {
        var found = 0;
        var result = windows.Select(window =>
        {
            if (window.Webviews is null)
            {
                if (window.Label != _options.WebviewLabel) return window;
                found++;
                return window with { Url = _options.AppBaseUri.AbsoluteUri };
            }
            return window with
            {
                Webviews = window.Webviews.Select(view =>
                {
                    if (view.Label != _options.WebviewLabel) return view;
                    found++;
                    return view with { Url = _options.AppBaseUri.AbsoluteUri };
                }).ToArray()
            };
        }).ToArray();
        if (found != 1) throw new InvalidOperationException("The configured Blazor webview label must identify exactly one initial webview.");
        return result;
    }

    private string CreateInitializationScript() =>
        "if(window.__NETWIV_IPC__?.webviewLabel===" + JsonSerializer.Serialize(_options.WebviewLabel, BlazorJsonContext.Default.String) +
        "&&location.origin===" + JsonSerializer.Serialize(_options.AppBaseUri.GetLeftPart(UriPartial.Authority), BlazorJsonContext.Default.String) +
        "){" + BlazorBridge.Script + "}";

    private Session? GetSession(ulong documentId)
    {
        lock (_gate)
        {
            if (!_accepting || _documentClosed || _documentId != documentId) return null;
            if (_session is { } current) return current.DocumentId == documentId ? current : null;
            var session = new Session(documentId);
            session.Dispatcher.OperationStarted = operation => Observe(operation, session);
            _session = session;
            session.Prepared = session.Dispatcher.InvokeAsync(async () =>
            {
                IBlazorPageHost manager;
                if (_aotOptions is not null)
                    manager = new AotBlazorPageHost(_services, session.Dispatcher, _options, _aotOptions,
                        message => SendMessage(session, message), error => Observe(Task.FromException(error), session));
                else if (RuntimeFeature.IsDynamicCodeSupported)
                    manager = new ManagedBlazorPageHost(_services, session.Dispatcher, _options,
                        uri => Navigate(session, uri), message => SendMessage(session, message), task => Observe(task, session));
                else
                    throw new PlatformNotSupportedException("NativeAOT Hybrid requires AddNetWivBlazorHybridAot.");
                session.Host = manager;
                if (!session.Active) manager.Retire();
                try
                {
                    await manager.InitializeAsync();
                    return manager;
                }
                catch
                {
                    await manager.DisposeAsync();
                    throw;
                }
            });
            return session;
        }
    }

    private void ChangeDocument(RuntimeDocumentChange change)
    {
        Session? previous;
        lock (_gate)
        {
            if (!_accepting || change.DocumentId < _documentId ||
                change.Closed && change.DocumentId != _documentId) return;
            if (!change.Closed && change.DocumentId == _documentId) return;
            _documentId = change.DocumentId;
            _documentClosed = change.Closed;
            previous = _session;
            _session = null;
            if (previous is not null)
            {
                previous.Active = false;
                previous.Host?.Retire();
            }
            if (!change.Closed) GetSession(change.DocumentId);
            if (previous is not null) Retire(previous);
        }
    }

    private void ReceiveMessage(RuntimeMessage message)
    {
        if (message.WebviewLabel != _options.WebviewLabel || message.DocumentId == 0 || !IsAppUri(message.Source)) return;
        Session? session;
        lock (_gate)
            session = _accepting && _session is { Active: true } current && current.DocumentId == message.DocumentId ? current : null;
        if (session is null) return;
        _ = session.Dispatcher.InvokeAsync(async () =>
        {
            var manager = await session.Prepared;
            if (IsCurrent(session))
            {
                var payload = message.Json[BlazorBridge.Prefix.Length..];
                await manager.ReceiveAsync(message.Source, payload);
            }
        });
    }

    private async ValueTask<AssetResponse?> ResolveAssetAsync(AssetRequest request,
        Func<AssetRequest, ValueTask<AssetResponse?>>? fallback, Func<AssetRequest, AssetResponse, AssetResponse>? policy)
    {
        var uri = request.Uri;
        if (uri.Scheme == Protocol && uri.Host == "localhost")
            uri = new Uri(_options.AppBaseUri, uri.PathAndQuery.TrimStart('/'));
        if (!IsAppUri(uri)) return fallback is null ? null : await fallback(request);
        if (request.WebviewLabel != _options.WebviewLabel) return new AssetResponse(403, "text/plain", "Forbidden"u8.ToArray());
        var session = GetSession(request.DocumentId);
        if (session is null) return new AssetResponse(410, "text/plain", "Document closed"u8.ToArray());
        return await session.Dispatcher.InvokeAsync(async () =>
        {
            var manager = await session.Prepared;
            if (!IsCurrent(session)) return new AssetResponse(410, "text/plain", "Document closed"u8.ToArray());
            var response = manager.TryGetResponse(request with { Uri = uri });
            if (response is not null && policy is not null) response = policy(request, response);
            return response is not null && request.Method == "HEAD" ? response with { Body = ReadOnlyMemory<byte>.Empty } : response;
        });
    }

    private bool IsAppUri(Uri uri) =>
        uri.Scheme == _options.AppBaseUri.Scheme && uri.IdnHost == _options.AppBaseUri.IdnHost &&
        uri.Port == _options.AppBaseUri.Port && string.IsNullOrEmpty(uri.UserInfo);

    private bool IsCurrent(Session session)
    {
        lock (_gate) return _accepting && session.Active && ReferenceEquals(_session, session);
    }

    private void SendMessage(Session session, string message)
    {
        if (IsCurrent(session) && session.DocumentId != 0)
            _runtime.EvaluateScript(_options.WebviewLabel, BlazorBridge.GetReceiveScript(message), session.DocumentId);
    }

    private void Navigate(Session session, Uri uri)
    {
        if (!IsCurrent(session)) return;
        var args = JsonSerializer.SerializeToElement(new BlazorNavigationRequest(_options.WebviewLabel, uri.AbsoluteUri),
            BlazorJsonContext.Default.BlazorNavigationRequest);
        Observe(_runtime.InvokeAsync("webview.navigate", args,
            new RuntimeCommandContext(_options.WebviewLabel, _options.AppBaseUri, true,
                DocumentId: session.DocumentId, IsBackend: true)), session);
    }

    private void RetireCurrent()
    {
        Session? session;
        lock (_gate)
        {
            _accepting = false;
            session = _session;
            _session = null;
            if (session is not null)
            {
                session.Active = false;
                session.Host?.Retire();
            }
            if (session is not null) Retire(session);
        }
    }

    private void Retire(Session session) => _ = session.Dispatcher.InvokeAsync(async () =>
    {
        IBlazorPageHost manager;
        try { manager = await session.Prepared; }
        catch { return; } // Preparation already disposed its partially initialized manager.
        await manager.DisposeAsync();
    });

    private void Observe(Task operation, Session session)
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pending.Add(observed.Task);
        _ = CompleteAsync(operation, observed, session);
    }

    private async Task CompleteAsync(Task operation, TaskCompletionSource observed, Session session)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (session.Host?.IsExpectedDisconnect(error) == true) return;
            bool running;
            lock (_gate)
            {
                _failure ??= error;
                running = _nativeRunning;
            }
            if (running)
            {
                try { Exit(1); }
                catch { } // The native runtime may already have completed shutdown.
            }
        }
        finally
        {
            observed.TrySetResult();
            lock (_gate) _pending.Remove(observed.Task);
        }
    }

    private async Task DrainAsync()
    {
        using var timeout = new CancellationTokenSource(CleanupTimeout);
        try
        {
            while (true)
            {
                Task[] pending;
                lock (_gate) pending = _pending.ToArray();
                if (pending.Length == 0) return;
                await Task.WhenAll(pending).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        { throw new TimeoutException("Blazor component cleanup did not complete within five seconds."); }
        catch { } // Individual failures are retained by CompleteAsync.
    }

    public void Dispatch(Action action) => _runtime.Dispatch(action);
    public Task<string> ResolveDirectoryAsync(int directory, string? path, string identifier, CancellationToken cancellationToken = default) =>
        _runtime.ResolveDirectoryAsync(directory, path, identifier, cancellationToken);
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        _runtime.InvokeAsync(command, args, cancellationToken);
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) =>
        _runtime.InvokeAsync(command, args, context, cancellationToken);
    public Task<RuntimeInvokeResponse> InvokeResponseAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default) =>
        _runtime.InvokeResponseAsync(command, args, context, cancellationToken);
    public void EvaluateScript(string webviewLabel, string script) => _runtime.EvaluateScript(webviewLabel, script);
    public void EvaluateScript(string webviewLabel, string script, ulong documentId) => _runtime.EvaluateScript(webviewLabel, script, documentId);
    public void Exit(int exitCode = 0)
    {
        RetireCurrent();
        _runtime.Exit(exitCode);
    }

    private sealed class Session(ulong documentId)
    {
        internal ulong DocumentId { get; } = documentId;
        internal BlazorDispatcher Dispatcher { get; } = new();
        internal IBlazorPageHost? Host { get; set; }
        internal bool Active { get; set; } = true;
        internal Task<IBlazorPageHost> Prepared { get; set; } = null!;
    }
}
