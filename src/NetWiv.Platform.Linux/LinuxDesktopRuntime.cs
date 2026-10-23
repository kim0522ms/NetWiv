using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWiv.Platform.Linux.Native;
using NetWiv.Runtime;

namespace NetWiv.Platform.Linux;

public sealed unsafe class LinuxDesktopRuntime : IDesktopRuntime
{
    private const string IpcHandlerName = "netwiv";
    private const string IpcSignalName = "script-message-received::netwiv";
    private const string AssetScheme = "tauri";
    private const int GPriorityDefault = 0;

    private readonly ConcurrentDictionary<string, WindowState> _windows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WebViewState> _webviews = new(StringComparer.Ordinal);
    private readonly List<GCHandle> _windowHandles = [];
    private readonly LinuxNativeResources _nativeResources;
    private RuntimeContext? _context;
    private GCHandle _runtimeHandle;
    private nint _webContext;
    private int _uiThreadId;
    private int _windowCount;
    private int _exitCode;
    private bool _running;

    public LinuxDesktopRuntime()
    {
        _nativeResources = new LinuxNativeResources(this);
    }

    public void Run(RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.ExitOnLastWindowClose) throw new NotSupportedException("Resident applications require DesktopRuntime.");
        if (context.AssetProtocolEnabled) throw new NotSupportedException("The scoped asset protocol requires DesktopRuntime.");
        if (context.Windows.Count == 0)
        {
            throw new ArgumentException("At least one window is required.", nameof(context));
        }

        if (_running)
        {
            throw new InvalidOperationException("The Linux desktop runtime is already running.");
        }

        if (LinuxNative.GtkInitCheck(0, 0) == 0)
        {
            throw new PlatformNotSupportedException(
                "GTK 3 could not connect to a display. Run under a graphical Linux session or WSLg.");
        }

        _context = context;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _exitCode = 0;
        _running = true;
        _runtimeHandle = GCHandle.Alloc(this);
        _webContext = LinuxNative.WebKitWebContextNew();
        if (_webContext == 0)
        {
            CleanupAfterRun();
            throw new InvalidOperationException("WebKitGTK failed to create a web context.");
        }

        try
        {
            LinuxNative.WebKitWebContextRegisterUriScheme(
                _webContext,
                AssetScheme,
                &OnUriSchemeRequest,
                GCHandle.ToIntPtr(_runtimeHandle),
                0);

            foreach (WindowOptions options in context.Windows)
            {
                CreateWindow(options);
            }

            context.OnReady?.Invoke();
            LinuxNative.GtkMain();
        }
        finally
        {
            CleanupAfterRun();
        }

        if (_exitCode != 0)
        {
            Environment.ExitCode = _exitCode;
        }
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureRunning();
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            action();
            return;
        }

        GCHandle handle = GCHandle.Alloc(action);
        LinuxNative.GMainContextInvokeFull(
            0,
            GPriorityDefault,
            &OnDispatch,
            GCHandle.ToIntPtr(handle),
            &OnDispatchDestroyed);
    }

    public Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        CancellationToken cancellationToken = default)
        => InvokeAsyncCore(command, args, null, null, cancellationToken);

    public Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsAuthorized)
        {
            throw new UnauthorizedAccessException(
                $"Webview '{context.WebviewLabel}' is not trusted to invoke native Linux commands.");
        }

        return InvokeAsyncCore(
            command, args, context.WebviewLabel, context.WindowLabel, cancellationToken);
    }

    private Task<JsonElement> InvokeAsyncCore(
        string command,
        JsonElement args,
        string? callerLabel,
        string? callerWindowLabel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        EnsureRunning();
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<JsonElement> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<JsonElement>)state!).TrySetCanceled(), completion);

        Dispatch(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(
                    InvokeOnUiThread(command, args, callerLabel, callerWindowLabel));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                registration.Dispose();
            }
        });

        return completion.Task;
    }

    public void EvaluateScript(string webviewLabel, string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webviewLabel);
        ArgumentNullException.ThrowIfNull(script);
        Dispatch(() =>
        {
            WebViewState state = GetWebView(webviewLabel);
            LinuxNative.WebKitWebViewEvaluateJavaScript(
                state.WebView,
                script,
                -1,
                0,
                0,
                0,
                0,
                0);
        });
    }

    public void Exit(int exitCode = 0)
    {
        EnsureRunning();
        Dispatch(() =>
        {
            _exitCode = exitCode;
            foreach (WindowState state in _windows.Values)
            {
                LinuxNative.GtkWidgetDestroy(state.Window);
            }

            LinuxNative.GtkMainQuit();
        });
    }

    internal object? ResolveResource(uint resourceId) =>
        _context?.ResolveResource?.Invoke(resourceId);

    private void CreateWindow(WindowOptions options, bool visible = true)
    {
        if (string.IsNullOrWhiteSpace(options.Label))
        {
            throw new ArgumentException("Window labels cannot be empty.", nameof(options));
        }

        if (!_windows.TryAdd(options.Label, null!))
        {
            throw new ArgumentException($"Duplicate window label '{options.Label}'.", nameof(options));
        }

        nint window = LinuxNative.GtkWindowNew(0);
        nint rootBox = LinuxNative.GtkBoxNew(1, 0);
        nint container = LinuxNative.GtkFixedNew();
        if (window == 0 || rootBox == 0 || container == 0)
        {
            _windows.TryRemove(options.Label, out _);
            throw new InvalidOperationException($"Failed to create GTK window '{options.Label}'.");
        }

        WindowState state = new(this, options.Label, window, rootBox, container);
        _windows[options.Label] = state;
        _windowCount++;

        GCHandle stateHandle = GCHandle.Alloc(state);
        _windowHandles.Add(stateHandle);
        nint stateData = GCHandle.ToIntPtr(stateHandle);

        LinuxNative.GSignalConnectData(
            window,
            "destroy",
            (delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnWindowDestroyed,
            stateData,
            0,
            0);
        LinuxNative.GSignalConnectData(
            window,
            "delete-event",
            (delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&OnWindowDeleteRequested,
            stateData,
            0,
            0);
        LinuxNative.GSignalConnectData(
            container,
            "size-allocate",
            (delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnContainerSizeAllocated,
            stateData,
            0,
            0);
        LinuxNative.GtkWindowSetTitle(window, options.Title);
        LinuxNative.GtkWindowSetDefaultSize(
            window,
            CheckedDimension(options.Width, nameof(options.Width)),
            CheckedDimension(options.Height, nameof(options.Height)));
        LinuxNative.GtkContainerAdd(window, rootBox);
        LinuxNative.GtkBoxPackStart(rootBox, container, 1, 1, 0);

        IReadOnlyList<WebviewOptions> webviews = options.Webviews
            ?? [new WebviewOptions(
                options.Label,
                options.Url,
                0,
                0,
                options.Width,
                options.Height,
                options.ExtraConfig)];
        foreach (WebviewOptions webviewOptions in webviews)
        {
            CreateWebView(state, webviewOptions);
        }

        LinuxNative.GtkWidgetShowAll(window);
        if (!visible)
        {
            LinuxNative.GtkWidgetHide(window);
        }
        EmitEvent("window-created", options.Label);
    }

    private void CreateWebView(WindowState window, WebviewOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Label) || !_webviews.TryAdd(options.Label, null!))
        {
            throw new ArgumentException($"Duplicate or empty webview label '{options.Label}'.", nameof(options));
        }

        nint webView = LinuxNative.WebKitWebViewNewWithContext(_webContext);
        if (webView == 0)
        {
            _webviews.TryRemove(options.Label, out _);
            throw new InvalidOperationException($"Failed to create WebKitGTK webview '{options.Label}'.");
        }

        nint manager = LinuxNative.WebKitWebViewGetUserContentManager(webView);
        WebViewState state = new(
            this,
            window,
            options.Label,
            webView,
            manager,
            RandomNumberGenerator.GetHexString(32),
            CheckedCoordinate(options.X, nameof(options.X)),
            CheckedCoordinate(options.Y, nameof(options.Y)),
            CheckedDimension(options.Width, nameof(options.Width)),
            CheckedDimension(options.Height, nameof(options.Height)));
        _webviews[options.Label] = state;
        window.WebViews.Add(state);

        GCHandle stateHandle = GCHandle.Alloc(state);
        _windowHandles.Add(stateHandle);
        nint stateData = GCHandle.ToIntPtr(stateHandle);
        LinuxNative.GSignalConnectData(
            manager,
            IpcSignalName,
            (delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnScriptMessage,
            stateData,
            0,
            0);

        if (LinuxNative.WebKitUserContentManagerRegisterScriptMessageHandler(manager, IpcHandlerName) == 0)
        {
            throw new InvalidOperationException($"Failed to register IPC for '{options.Label}'.");
        }

        string initializationScript = BuildInitializationScript(
            _context!.InitializationScript,
            state.IpcKey,
            window.Label,
            state.Label);
        nint userScript = LinuxNative.WebKitUserScriptNew(initializationScript, 1, 0, 0, 0);
        if (userScript == 0)
        {
            throw new InvalidOperationException($"Failed to create the initialization script for '{options.Label}'.");
        }

        LinuxNative.WebKitUserContentManagerAddScript(manager, userScript);
        LinuxNative.WebKitUserScriptUnref(userScript);
        LinuxNative.GtkWidgetSetSizeRequest(
            webView,
            CheckedDimension(options.Width, nameof(options.Width)),
            CheckedDimension(options.Height, nameof(options.Height)));
        LinuxNative.GtkFixedPut(
            window.Container,
            webView,
            CheckedCoordinate(options.X, nameof(options.X)),
            CheckedCoordinate(options.Y, nameof(options.Y)));
        if (GetOptionalBoolean(options.ExtraConfig, "visible", true))
        {
            LinuxNative.GtkWidgetShow(webView);
        }
        LinuxNative.WebKitWebViewLoadUri(webView, ResolveInitialUrl(options.Url));
        EmitEvent("webview-created", options.Label);
    }

    private JsonElement InvokeOnUiThread(
        string command,
        JsonElement args,
        string? callerLabel,
        string? callerWindowLabel)
    {
        if (_nativeResources.CanInvoke(command))
        {
            return _nativeResources.Invoke(
                command, args, label => GetWindow(label).RootBox, callerLabel);
        }

        if (command == "plugin:webview|get_all_webviews")
        {
            return JsonWebviews(_webviews.Values.OrderBy(static value => value.Label, StringComparer.Ordinal));
        }

        if (command == "plugin:webview|create_webview_window")
        {
            JsonElement options = GetRequiredObject(args, "options");
            WindowOptions windowOptions = ParseWebviewWindowOptions(options);
            CreateWindow(windowOptions, GetOptionalBoolean(options, "visible", true));
            return JsonNull();
        }

        if (command == "plugin:webview|create_webview")
        {
            JsonElement options = args.TryGetProperty("options", out JsonElement nested)
                && nested.ValueKind == JsonValueKind.Object
                    ? nested
                    : args;
            string windowLabel = GetOptionalString(args, "windowLabel")
                ?? callerWindowLabel
                ?? (callerLabel is null ? null : GetWebView(callerLabel).Window.Label)
                ?? throw new ArgumentException("windowLabel is required.", nameof(args));
            CreateWebView(GetWindow(windowLabel), ParseWebviewOptions(options));
            return JsonNull();
        }

        if (command == "plugin:window|create")
        {
            JsonElement options = GetRequiredObject(args, "options");
            WindowOptions parsed = ParseWebviewWindowOptions(options);
            CreateWindow(parsed with { Webviews = [] }, GetOptionalBoolean(options, "visible", true));
            return JsonNull();
        }

        if (command.StartsWith("plugin:webview|", StringComparison.Ordinal))
        {
            return InvokeWebViewOnUiThread(
                command, args, GetOptionalString(args, "label") ?? callerLabel
                    ?? throw new ArgumentException("label is required.", nameof(args)));
        }

        if (command == "plugin:window|get_all_windows")
        {
            return JsonStringArray(_windows.Keys.Order(StringComparer.Ordinal));
        }

        WebViewState? callerWebView = callerLabel is null ? null : GetWebView(callerLabel);
        string label = GetOptionalString(args, "label")
            ?? callerWindowLabel
            ?? callerWebView?.Window.Label
            ?? throw new ArgumentException("label is required.", nameof(args));
        WindowState state = GetWindow(label);

        switch (command)
        {
            case "plugin:window|set_title":
                LinuxNative.GtkWindowSetTitle(state.Window, GetRequiredString(args, "value"));
                return JsonNull();
            case "plugin:window|show":
                LinuxNative.GtkWidgetShow(state.Window);
                return JsonNull();
            case "plugin:window|hide":
                LinuxNative.GtkWidgetHide(state.Window);
                return JsonNull();
            case "plugin:window|close":
                RequestWindowClose(state);
                return JsonNull();
            case "plugin:window|destroy":
                LinuxNative.GtkWidgetDestroy(state.Window);
                return JsonNull();
            case "plugin:window|maximize":
                LinuxNative.GtkWindowMaximize(state.Window);
                state.Maximized = true;
                return JsonNull();
            case "plugin:window|unmaximize":
                LinuxNative.GtkWindowUnmaximize(state.Window);
                state.Maximized = false;
                return JsonNull();
            case "plugin:window|minimize":
                LinuxNative.GtkWindowIconify(state.Window);
                state.Minimized = true;
                return JsonNull();
            case "plugin:window|unminimize":
                LinuxNative.GtkWindowDeiconify(state.Window);
                state.Minimized = false;
                return JsonNull();
            case "plugin:window|toggle_maximize":
                if (HasWindowState(state, 1 << 2))
                {
                    LinuxNative.GtkWindowUnmaximize(state.Window);
                }
                else
                {
                    LinuxNative.GtkWindowMaximize(state.Window);
                }
                return JsonNull();
            case "plugin:window|is_maximized":
                return JsonBoolean(HasWindowState(state, 1 << 2));
            case "plugin:window|is_minimized":
                return JsonBoolean(HasWindowState(state, 1 << 1));
            case "plugin:window|fullscreen":
                LinuxNative.GtkWindowFullscreen(state.Window);
                state.Fullscreen = true;
                return JsonNull();
            case "plugin:window|unfullscreen":
                LinuxNative.GtkWindowUnfullscreen(state.Window);
                state.Fullscreen = false;
                return JsonNull();
            case "plugin:window|set_fullscreen":
                state.Fullscreen = GetRequiredBoolean(args, "value");
                if (state.Fullscreen) LinuxNative.GtkWindowFullscreen(state.Window);
                else LinuxNative.GtkWindowUnfullscreen(state.Window);
                return JsonNull();
            case "plugin:window|is_fullscreen":
                return JsonBoolean(HasWindowState(state, 1 << 4));
            case "plugin:window|set_focus":
                LinuxNative.GtkWindowPresent(state.Window);
                LinuxNative.GtkWidgetGrabFocus(callerWebView?.WebView ?? state.WebViews[0].WebView);
                return JsonNull();
            case "plugin:window|is_visible":
                return JsonBoolean(LinuxNative.GtkWidgetGetVisible(state.Window) != 0);
            case "plugin:window|is_focused":
                return JsonBoolean(LinuxNative.GtkWindowIsActive(state.Window) != 0);
            case "plugin:window|title":
                return JsonString(LinuxNative.Utf8(LinuxNative.GtkWindowGetTitle(state.Window)) ?? string.Empty);
            case "plugin:window|scale_factor":
                return JsonNumber(LinuxNative.GtkWidgetGetScaleFactor(state.Window));
            case "plugin:window|is_resizable":
                return JsonBoolean(LinuxNative.GtkWindowGetResizable(state.Window) != 0);
            case "plugin:window|set_resizable":
                LinuxNative.GtkWindowSetResizable(state.Window, GetRequiredBoolean(args, "value") ? 1 : 0);
                return JsonNull();
            case "plugin:window|is_decorated":
                return JsonBoolean(LinuxNative.GtkWindowGetDecorated(state.Window) != 0);
            case "plugin:window|set_decorations":
                LinuxNative.GtkWindowSetDecorated(state.Window, GetRequiredBoolean(args, "value") ? 1 : 0);
                return JsonNull();
            case "plugin:window|set_always_on_top":
                LinuxNative.GtkWindowSetKeepAbove(state.Window, GetRequiredBoolean(args, "value") ? 1 : 0);
                return JsonNull();
            case "plugin:window|set_size":
                (int newWidth, int newHeight) = GetSize(args.GetProperty("value"));
                LinuxNative.GtkWindowResize(state.Window, newWidth, newHeight);
                return JsonNull();
            case "plugin:window|set_position":
                (int newX, int newY) = GetPosition(args.GetProperty("value"));
                LinuxNative.GtkWindowMove(state.Window, newX, newY);
                return JsonNull();
            case "plugin:window|inner_position":
            case "plugin:window|outer_position":
                LinuxNative.GtkWindowGetPosition(state.Window, out int x, out int y);
                return JsonPosition(x, y);
            case "plugin:window|inner_size":
            case "plugin:window|outer_size":
                LinuxNative.GtkWindowGetSize(state.Window, out int width, out int height);
                return JsonSize(width, height);
            default:
                throw new NotSupportedException(
                    $"Linux desktop command '{command}' is not implemented.");
        }
    }

    private JsonElement InvokeWebViewOnUiThread(string command, JsonElement args, string label)
    {
        WebViewState state = GetWebView(label);
        switch (command)
        {
            case "plugin:webview|set_webview_focus":
                LinuxNative.GtkWindowPresent(state.Window.Window);
                LinuxNative.GtkWidgetGrabFocus(state.WebView);
                return JsonNull();
            case "plugin:webview|webview_hide":
                LinuxNative.GtkWidgetHide(state.WebView);
                return JsonNull();
            case "plugin:webview|webview_show":
                LinuxNative.GtkWidgetShow(state.WebView);
                return JsonNull();
            case "plugin:webview|set_webview_zoom":
                LinuxNative.WebKitWebViewSetZoomLevel(state.WebView, GetRequiredDouble(args, "value"));
                return JsonNull();
            case "plugin:webview|webview_size":
                return JsonSize(state.Width, state.Height);
            case "plugin:webview|webview_position":
                return JsonPosition(state.X, state.Y);
            case "plugin:webview|set_webview_size":
                (state.Width, state.Height) = GetSize(args.GetProperty("value"));
                LinuxNative.GtkWidgetSetSizeRequest(state.WebView, state.Width, state.Height);
                return JsonNull();
            case "plugin:webview|set_webview_position":
                (state.X, state.Y) = GetPosition(args.GetProperty("value"));
                LinuxNative.GtkFixedMove(state.Window.Container, state.WebView, state.X, state.Y);
                return JsonNull();
            case "plugin:webview|reparent":
                WindowState destination = GetWindow(GetRequiredString(args, "window"));
                if (!ReferenceEquals(destination, state.Window))
                {
                    state.Window.WebViews.Remove(state);
                    LinuxNative.GtkWidgetReparent(state.WebView, destination.Container);
                    state.Window = destination;
                    destination.WebViews.Add(state);
                    LinuxNative.GtkFixedMove(destination.Container, state.WebView, state.X, state.Y);
                }
                return JsonNull();
            case "plugin:webview|set_webview_auto_resize":
                state.AutoResize = GetRequiredBoolean(args, "value");
                if (state.AutoResize)
                {
                    ResizeWebViewToContainer(state);
                }
                return JsonNull();
            case "plugin:webview|webview_close":
                LinuxNative.WebKitUserContentManagerUnregisterScriptMessageHandler(
                    state.UserContentManager, IpcHandlerName);
                LinuxNative.GtkWidgetDestroy(state.WebView);
                _webviews.TryRemove(state.Label, out _);
                state.Window.WebViews.Remove(state);
                EmitEvent("webview-destroyed", state.Label);
                return JsonNull();
            default:
                throw new NotSupportedException(
                    $"Linux desktop command '{command}' is not implemented.");
        }
    }

    private void HandleScriptMessage(WebViewState state, nint result)
    {
        nint value = LinuxNative.WebKitJavaScriptResultGetJsValue(result);
        nint textPointer = LinuxNative.JscValueToString(value);
        try
        {
            string envelopeJson = LinuxNative.Utf8(textPointer) ?? "null";
            using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
            JsonElement root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("key", out JsonElement key) ||
                key.ValueKind != JsonValueKind.String ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(key.GetString()!),
                    Encoding.UTF8.GetBytes(state.IpcKey)) ||
                !root.TryGetProperty("payload", out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.String)
            {
                throw new UnauthorizedAccessException("Rejected an unauthenticated WebKit IPC message.");
            }

            string json = payload.GetString()!;
            string sourceText = LinuxNative.Utf8(LinuxNative.WebKitWebViewGetUri(state.WebView))
                ?? "about:blank";
            Uri source = Uri.TryCreate(sourceText, UriKind.Absolute, out Uri? parsed)
                ? parsed
                : new Uri("about:blank");
            _context!.OnMessage(new RuntimeMessage(state.Label, source, json, state.Window.Label));
        }
        finally
        {
            if (textPointer != 0)
            {
                LinuxNative.GFree(textPointer);
            }
        }
    }

    private void HandleUriSchemeRequest(nint request)
    {
        AssetResponse response;
        try
        {
            string uriText = LinuxNative.Utf8(LinuxNative.WebKitUriSchemeRequestGetUri(request))
                ?? "tauri://localhost/invalid";
            string method = LinuxNative.Utf8(LinuxNative.WebKitUriSchemeRequestGetHttpMethod(request))
                ?? "GET";
            Uri uri = new(uriText, UriKind.Absolute);
            response = _context!.ResolveAsset(
                new AssetRequest(uri, method, ReadRequestHeaders(request)))
                ?? new AssetResponse(404, "text/plain; charset=utf-8", "Not Found"u8.ToArray());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux asset resolver failed: {exception}");
            response = new AssetResponse(
                500,
                "text/plain; charset=utf-8",
                "Internal Server Error"u8.ToArray());
        }

        if (response.StatusCode is < 100 or > 599 || string.IsNullOrWhiteSpace(response.ContentType))
        {
            Console.Error.WriteLine("NetWiv Linux asset resolver returned an invalid status or content type.");
            response = new AssetResponse(
                500,
                "text/plain; charset=utf-8",
                "Internal Server Error"u8.ToArray());
        }

        byte[] body = response.Body.ToArray();
        nint data = body.Length == 0 ? Marshal.AllocHGlobal(1) : Marshal.AllocHGlobal(body.Length);
        if (body.Length != 0)
        {
            Marshal.Copy(body, 0, data, body.Length);
        }

        nint stream = LinuxNative.GMemoryInputStreamNewFromData(data, body.Length, &OnAssetBufferDestroyed);
        nint nativeResponse = LinuxNative.WebKitUriSchemeResponseNew(stream, body.Length);
        nint responseHeaders = 0;
        try
        {
            LinuxNative.WebKitUriSchemeResponseSetStatus(nativeResponse, (uint)response.StatusCode, null);
            LinuxNative.WebKitUriSchemeResponseSetContentType(nativeResponse, response.ContentType);
            if (response.Headers is { Count: > 0 })
            {
                responseHeaders = LinuxNative.SoupMessageHeadersNew(1);
                foreach ((string name, string value) in response.Headers)
                {
                    LinuxNative.SoupMessageHeadersAppend(responseHeaders, name, value);
                }

                LinuxNative.WebKitUriSchemeResponseSetHttpHeaders(nativeResponse, responseHeaders);
            }

            LinuxNative.WebKitUriSchemeRequestFinishWithResponse(request, nativeResponse);
        }
        finally
        {
            if (responseHeaders != 0) LinuxNative.SoupMessageHeadersUnref(responseHeaders);
            LinuxNative.GObjectUnref(nativeResponse);
            LinuxNative.GObjectUnref(stream);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadRequestHeaders(nint request)
    {
        nint nativeHeaders = LinuxNative.WebKitUriSchemeRequestGetHttpHeaders(request);
        if (nativeHeaders == 0)
        {
            return EmptyHeaders.Instance;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        GCHandle handle = GCHandle.Alloc(headers);
        try
        {
            LinuxNative.SoupMessageHeadersForeach(
                nativeHeaders, &OnRequestHeader, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return headers;
    }

    private void HandleWindowDestroyed(WindowState state)
    {
        if (!_windows.TryRemove(state.Label, out _))
        {
            return;
        }

        foreach (WebViewState webview in state.WebViews)
        {
            _webviews.TryRemove(webview.Label, out _);
            LinuxNative.WebKitUserContentManagerUnregisterScriptMessageHandler(
                webview.UserContentManager, IpcHandlerName);
            EmitEvent("webview-destroyed", webview.Label);
        }
        EmitEvent("window-destroyed", state.Label);
        _windowCount--;
        if (_windowCount == 0)
        {
            LinuxNative.GtkMainQuit();
        }
    }

    private void EmitEvent(string name, string? label)
    {
        _context!.OnEvent(new RuntimeEvent(name, label, JsonNull()));
    }

    private bool RequestWindowClose(WindowState state)
    {
        if (_context!.HasEventListeners?.Invoke("tauri://close-requested", state.Label) == true)
        {
            EmitEvent("tauri://close-requested", state.Label);
            return true;
        }

        LinuxNative.GtkWidgetDestroy(state.Window);
        return false;
    }

    private void CleanupAfterRun()
    {
        foreach (WindowState window in _windows.Values.ToArray())
        {
            LinuxNative.GtkWidgetDestroy(window.Window);
        }

        _running = false;
        _nativeResources.Dispose();
        _windows.Clear();
        _webviews.Clear();
        foreach (GCHandle handle in _windowHandles)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        _windowHandles.Clear();
        if (_webContext != 0)
        {
            LinuxNative.GObjectUnref(_webContext);
            _webContext = 0;
        }

        if (_runtimeHandle.IsAllocated)
        {
            _runtimeHandle.Free();
        }

        _context = null;
        _uiThreadId = 0;
        _windowCount = 0;
    }

    private static string BuildInitializationScript(
        string userScript,
        string ipcKey,
        string windowLabel,
        string webviewLabel) =>
        "(() => { const ipcKey = '" + ipcKey + "';" +
        "const bridge = Object.freeze({os:'linux',windowLabel:" + JsString(windowLabel) +
        ",webviewLabel:" + JsString(webviewLabel) + ",postMessage(message) {" +
        "globalThis.webkit.messageHandlers.netwiv.postMessage(JSON.stringify({" +
        "key:ipcKey,payload:typeof message==='string'?message:JSON.stringify(message)" +
        "}));}});Object.defineProperty(globalThis,'__NETWIV_IPC__'," +
        "{value:bridge,configurable:false,writable:false});})();\n" + userScript;

    private static string JsString(string value) =>
        "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private static string ResolveInitialUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute)
            ? absolute.AbsoluteUri
            : new Uri(new Uri("tauri://localhost/"), url.TrimStart('/')).AbsoluteUri;

    private static WindowOptions ParseWebviewWindowOptions(JsonElement options)
    {
        string label = GetRequiredString(options, "label");
        string title = GetOptionalString(options, "title") ?? label;
        string url = GetOptionalString(options, "url") ?? "index.html";
        double width = GetOptionalDouble(options, "width", 800);
        double height = GetOptionalDouble(options, "height", 600);
        return new WindowOptions(label, title, url, width, height, options.Clone());
    }

    private static WebviewOptions ParseWebviewOptions(JsonElement options)
    {
        string label = GetRequiredString(options, "label");
        string url = GetOptionalString(options, "url") ?? "index.html";
        double x = GetOptionalDouble(options, "x", 0);
        double y = GetOptionalDouble(options, "y", 0);
        double width = GetOptionalDouble(options, "width", 400);
        double height = GetOptionalDouble(options, "height", 300);
        return new WebviewOptions(label, url, x, y, width, height, options.Clone());
    }

    private WindowState GetWindow(string label) =>
        _windows.TryGetValue(label, out WindowState? state)
            ? state
            : throw new KeyNotFoundException($"No Linux webview has label '{label}'.");

    private WebViewState GetWebView(string label) =>
        _webviews.TryGetValue(label, out WebViewState? state)
            ? state
            : throw new KeyNotFoundException($"No Linux webview has label '{label}'.");

    private void EnsureRunning()
    {
        if (!_running)
        {
            throw new InvalidOperationException("The Linux desktop runtime is not running.");
        }
    }

    private static int CheckedDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0 || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(name, "Window dimensions must be positive finite values.");
        }

        return checked((int)Math.Round(value));
    }

    private static int CheckedCoordinate(double value, string name)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(name, "Webview coordinates must be finite 32-bit values.");
        }

        return checked((int)Math.Round(value));
    }

    private static bool HasWindowState(WindowState state, int flag)
    {
        nint nativeWindow = LinuxNative.GtkWidgetGetWindow(state.Window);
        return nativeWindow != 0 && (LinuxNative.GdkWindowGetState(nativeWindow) & flag) != 0;
    }

    private static void ResizeWebViewToContainer(WebViewState state)
    {
        int width = Math.Max(1, LinuxNative.GtkWidgetGetAllocatedWidth(state.Window.Container) - state.X);
        int height = Math.Max(1, LinuxNative.GtkWidgetGetAllocatedHeight(state.Window.Container) - state.Y);
        state.Width = width;
        state.Height = height;
        LinuxNative.GtkWidgetSetSizeRequest(state.WebView, width, height);
    }

    private static string GetRequiredString(JsonElement args, string propertyName)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        throw new ArgumentException($"Command argument '{propertyName}' must be a string.", nameof(args));
    }

    private static bool GetRequiredBoolean(JsonElement args, string propertyName)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        throw new ArgumentException($"Command argument '{propertyName}' must be a boolean.", nameof(args));
    }

    private static double GetRequiredDouble(JsonElement args, string propertyName)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        throw new ArgumentException($"Command argument '{propertyName}' must be a number.", nameof(args));
    }

    private static JsonElement GetRequiredObject(JsonElement args, string propertyName)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        throw new ArgumentException($"Command argument '{propertyName}' must be an object.", nameof(args));
    }

    private static string? GetOptionalString(JsonElement args, string propertyName) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetOptionalBoolean(JsonElement args, string propertyName, bool defaultValue) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    private static double GetOptionalDouble(JsonElement args, string propertyName, double defaultValue)
    {
        if (!args.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) &&
            double.IsFinite(number))
        {
            return number;
        }

        throw new ArgumentException($"Command argument '{propertyName}' must be a finite number.", nameof(args));
    }

    private static (int Width, int Height) GetSize(JsonElement value)
    {
        JsonElement data = GetDpiData(value);
        return (
            CheckedDimension(data.GetProperty("width").GetDouble(), "width"),
            CheckedDimension(data.GetProperty("height").GetDouble(), "height"));
    }

    private static (int X, int Y) GetPosition(JsonElement value)
    {
        JsonElement data = GetDpiData(value);
        return (
            CheckedCoordinate(data.GetProperty("x").GetDouble(), "x"),
            CheckedCoordinate(data.GetProperty("y").GetDouble(), "y"));
    }

    private static JsonElement GetDpiData(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A Tauri DPI value must be an object.", nameof(value));
        }

        if (value.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
        {
            return data;
        }

        if (value.TryGetProperty("Logical", out JsonElement logical) && logical.ValueKind == JsonValueKind.Object)
        {
            return logical;
        }

        if (value.TryGetProperty("Physical", out JsonElement physical) && physical.ValueKind == JsonValueKind.Object)
        {
            return physical;
        }

        return value;
    }

    private static JsonElement JsonNull() => JsonDocument.Parse("null").RootElement.Clone();

    private static JsonElement JsonBoolean(bool value) =>
        JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();

    private static JsonElement JsonSize(int width, int height) =>
        JsonDocument.Parse($"{{\"width\":{width},\"height\":{height}}}").RootElement.Clone();

    private static JsonElement JsonPosition(int x, int y) =>
        JsonDocument.Parse($"{{\"x\":{x},\"y\":{y}}}").RootElement.Clone();

    private static JsonElement JsonString(string value) =>
        JsonDocument.Parse(JsString(value)).RootElement.Clone();

    private static JsonElement JsonNumber(double value) =>
        JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();

    private static JsonElement JsonStringArray(IEnumerable<string> values)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (string value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement JsonWebviews(IEnumerable<WebViewState> webviews)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (WebViewState webview in webviews)
            {
                writer.WriteStartObject();
                writer.WriteString("windowLabel", webview.Window.Label);
                writer.WriteString("label", webview.Label);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnDispatch(nint data)
    {
        try
        {
            ((Action)GCHandle.FromIntPtr(data).Target!).Invoke();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux dispatch failed: {exception}");
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDispatchDestroyed(nint data)
    {
        GCHandle handle = GCHandle.FromIntPtr(data);
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWindowDestroyed(nint widget, nint data)
    {
        try
        {
            WindowState state = (WindowState)GCHandle.FromIntPtr(data).Target!;
            LinuxDesktopRuntime runtime = state.Owner;
            runtime.HandleWindowDestroyed(state);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux window cleanup failed: {exception}");
            LinuxNative.GtkMainQuit();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnWindowDeleteRequested(nint widget, nint windowEvent, nint data)
    {
        try
        {
            WindowState state = (WindowState)GCHandle.FromIntPtr(data).Target!;
            return state.Owner.RequestWindowClose(state) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux close-request callback failed: {exception}");
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnContainerSizeAllocated(nint widget, nint allocation, nint data)
    {
        try
        {
            WindowState state = (WindowState)GCHandle.FromIntPtr(data).Target!;
            foreach (WebViewState webview in state.WebViews)
            {
                if (webview.AutoResize)
                {
                    ResizeWebViewToContainer(webview);
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux auto-resize callback failed: {exception}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnScriptMessage(nint manager, nint result, nint data)
    {
        try
        {
            WebViewState state = (WebViewState)GCHandle.FromIntPtr(data).Target!;
            state.Owner.HandleScriptMessage(state, result);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux IPC callback failed: {exception}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUriSchemeRequest(nint request, nint userData)
    {
        try
        {
            LinuxDesktopRuntime runtime = (LinuxDesktopRuntime)GCHandle.FromIntPtr(userData).Target!;
            runtime.HandleUriSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux asset callback failed: {exception}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAssetBufferDestroyed(nint data) => Marshal.FreeHGlobal(data);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRequestHeader(nint name, nint value, nint userData)
    {
        Dictionary<string, string> headers =
            (Dictionary<string, string>)GCHandle.FromIntPtr(userData).Target!;
        string? headerName = LinuxNative.Utf8(name);
        if (headerName is not null)
        {
            headers[headerName] = LinuxNative.Utf8(value) ?? string.Empty;
        }
    }

    private sealed class WindowState(
        LinuxDesktopRuntime owner,
        string label,
        nint window,
        nint rootBox,
        nint container)
    {
        internal string Label { get; } = label;
        internal nint Window { get; } = window;
        internal nint RootBox { get; } = rootBox;
        internal nint Container { get; } = container;
        internal LinuxDesktopRuntime Owner { get; } = owner;
        internal List<WebViewState> WebViews { get; } = [];
        internal bool Maximized { get; set; }
        internal bool Minimized { get; set; }
        internal bool Fullscreen { get; set; }
    }

    private sealed class WebViewState(
        LinuxDesktopRuntime owner,
        WindowState window,
        string label,
        nint webView,
        nint userContentManager,
        string ipcKey,
        int x,
        int y,
        int width,
        int height)
    {
        internal LinuxDesktopRuntime Owner { get; } = owner;
        internal WindowState Window { get; set; } = window;
        internal string Label { get; } = label;
        internal nint WebView { get; } = webView;
        internal nint UserContentManager { get; } = userContentManager;
        internal string IpcKey { get; } = ipcKey;
        internal int X { get; set; } = x;
        internal int Y { get; set; } = y;
        internal int Width { get; set; } = width;
        internal int Height { get; set; } = height;
        internal bool AutoResize { get; set; }
    }

    private sealed class EmptyHeaders : IReadOnlyDictionary<string, string>
    {
        internal static EmptyHeaders Instance { get; } = new();
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public string this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        public bool TryGetValue(string key, out string value)
        {
            value = null!;
            return false;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
