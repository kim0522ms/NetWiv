using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetWiv.Platform.Windows.Interop;
using NetWiv.Platform.Windows.WebView2;
using NetWiv.Runtime;

namespace NetWiv.Platform.Windows;

/// <summary>A NativeAOT-compatible Win32 and WebView2 desktop runtime.</summary>
public sealed class WindowsDesktopRuntime : IDesktopRuntime
{
    private const string WindowClassName = "NetWiv.NativeAot.WebView2.Window";
    private static WindowsDesktopRuntime? s_activeRuntime;

    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private readonly Dictionary<nint, WebViewWindow> _windowsByHandle = [];
    private readonly Dictionary<string, WebViewWindow> _windowsByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, string> _topLevelLabelsByHandle = [];
    private readonly Dictionary<string, nint> _topLevelHandlesByLabel = new(StringComparer.Ordinal);
    private readonly NativeDesktopResources _nativeResources;
    private EnvironmentCompletedHandler? _environmentCompleted;
    private Exception? _fatalError;
    private uint _uiThreadId;
    private int _exitCode;
    private bool _running;

    internal RuntimeContext Context { get; private set; } = null!;
    internal ICoreWebView2Environment? Environment { get; private set; }
    internal IEnumerable<nint> TopLevelHandles => _topLevelHandlesByLabel.Values;
    internal nint FirstTopLevelHandle => _topLevelLabelsByHandle.Keys.FirstOrDefault();

    public WindowsDesktopRuntime()
    {
        _nativeResources = new NativeDesktopResources(this);
    }

    public unsafe void Run(RuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.AssetProtocolEnabled) throw new NotSupportedException("The scoped asset protocol requires DesktopRuntime.");
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WindowsDesktopRuntime requires Windows.");
        }

        if (context.Windows.Count == 0)
        {
            throw new ArgumentException("At least one window is required.", nameof(context));
        }

        if (Interlocked.CompareExchange(ref s_activeRuntime, this, null) is not null)
        {
            throw new InvalidOperationException("Only one WindowsDesktopRuntime can own the Win32 event loop.");
        }

        ValidateWindows(context.Windows);
        Context = context;
        _uiThreadId = NativeMethods.GetCurrentThreadId();
        _running = true;

        bool comInitialized = false;
        try
        {
            int initializeResult = NativeMethods.CoInitializeEx(0, NativeMethods.CoinitApartmentThreaded);
            NativeMethods.ThrowIfFailed(initializeResult, "CoInitializeEx(COINIT_APARTMENTTHREADED)");
            comInitialized = true;
            _ = NativeMethods.SetProcessDpiAwarenessContext(new nint(-4));
            RegisterWindowClass();
            CreateWindows(context.Windows);
            CreateWebViewEnvironment();
            context.OnReady?.Invoke();
            RunMessageLoop();
        }
        finally
        {
            _nativeResources.Dispose();
            foreach (WebViewWindow window in _windowsByHandle.Values.ToArray())
            {
                window.Close();
                _ = NativeMethods.DestroyWindow(window.Handle);
            }

            _windowsByHandle.Clear();
            _windowsByLabel.Clear();
            _topLevelLabelsByHandle.Clear();
            _topLevelHandlesByLabel.Clear();
            Environment = null;
            _environmentCompleted = null;
            _running = false;
            _uiThreadId = 0;
            if (comInitialized)
            {
                NativeMethods.CoUninitialize();
            }
            Interlocked.CompareExchange(ref s_activeRuntime, null, this);
        }

        if (_fatalError is not null)
        {
            throw new InvalidOperationException("The Windows desktop event loop failed.", _fatalError);
        }

        System.Environment.ExitCode = _exitCode;
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_running)
        {
            throw new InvalidOperationException("The Windows desktop event loop is not running.");
        }

        if (NativeMethods.GetCurrentThreadId() == _uiThreadId)
        {
            action();
            return;
        }

        _dispatchQueue.Enqueue(action);
        if (!NativeMethods.PostThreadMessage(_uiThreadId, NativeMethods.WmAppDispatch, 0, 0))
        {
            throw NativeMethods.Win32Error("PostThreadMessageW");
        }
    }

    public Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        CancellationToken cancellationToken = default)
    {
        string label = RequireString(args, "label");
        var context = new RuntimeCommandContext(label, new Uri("netwiv://host-api/"), true);
        return InvokeAsync(command, args, context, cancellationToken);
    }

    public Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<JsonElement>(cancellationToken);
        }

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<JsonElement>)state!).TrySetCanceled(),
            completion);
        try
        {
            Dispatch(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(InvokeOnUiThread(command, args, context));
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled(cancellationToken);
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
        }
        catch (Exception exception)
        {
            registration.Dispose();
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    public void EvaluateScript(string webviewLabel, string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webviewLabel);
        ArgumentNullException.ThrowIfNull(script);
        Dispatch(() => GetWindow(webviewLabel).EvaluateScript(script));
    }

    public void Exit(int exitCode = 0)
    {
        if (!_running)
        {
            _exitCode = exitCode;
            return;
        }

        Dispatch(() => CloseAllWindows(exitCode));
    }

    internal void EmitEvent(string eventName, string? label, string payloadJson)
    {
        using JsonDocument document = JsonDocument.Parse(payloadJson);
        Context.OnEvent(new RuntimeEvent(eventName, label, document.RootElement.Clone()));
    }

    internal void Fail(Exception exception)
    {
        _fatalError ??= exception;
        if (_running)
        {
            CloseAllWindows(1);
        }
    }

    private static void ValidateWindows(IReadOnlyList<WindowOptions> windows)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (WindowOptions window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Label))
            {
                throw new ArgumentException("Every window must have a non-empty label.", nameof(windows));
            }

            if (!labels.Add(window.Label))
            {
                throw new ArgumentException($"Duplicate window label '{window.Label}'.", nameof(windows));
            }

            if (!Uri.TryCreate(ResolveUrl(window.Url), UriKind.Absolute, out _))
            {
                throw new ArgumentException($"Window '{window.Label}' has an invalid absolute URL.", nameof(windows));
            }

            if (!double.IsFinite(window.Width) || !double.IsFinite(window.Height)
                || window.Width <= 0 || window.Height <= 0)
            {
                throw new ArgumentException($"Window '{window.Label}' must have positive finite dimensions.", nameof(windows));
            }

            if (window.Webviews is null)
            {
                continue;
            }

            foreach (WebviewOptions webview in window.Webviews)
            {
                ValidateWebview(webview, labels, nameof(windows));
            }
        }
    }

    private static void ValidateWebview(WebviewOptions webview, HashSet<string> labels, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(webview.Label) || !labels.Add(webview.Label))
        {
            throw new ArgumentException($"Webview label '{webview.Label}' is empty or duplicated.", argumentName);
        }

        if (!Uri.TryCreate(ResolveUrl(webview.Url), UriKind.Absolute, out _))
        {
            throw new ArgumentException($"Webview '{webview.Label}' has an invalid URL.", argumentName);
        }

        if (!double.IsFinite(webview.X) || !double.IsFinite(webview.Y)
            || !double.IsFinite(webview.Width) || !double.IsFinite(webview.Height)
            || webview.Width <= 0 || webview.Height <= 0)
        {
            throw new ArgumentException($"Webview '{webview.Label}' has invalid bounds.", argumentName);
        }
    }

    private unsafe void RegisterWindowClass()
    {
        fixed (char* className = WindowClassName)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Style = NativeMethods.CsOwnDc,
                WindowProcedure = &WindowProcedure,
                Instance = NativeMethods.GetModuleHandle(null),
                ClassName = className,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            if (atom == 0 && Marshal.GetLastPInvokeError() != NativeMethods.ErrorClassAlreadyExists)
            {
                throw NativeMethods.Win32Error("RegisterClassExW");
            }
        }
    }

    private void CreateWindows(IReadOnlyList<WindowOptions> options)
    {
        foreach (WindowOptions option in options)
        {
            CreateTopLevelWindow(option, true);
        }
    }

    private void CreateTopLevelWindow(WindowOptions option, bool visible)
    {
        if (_topLevelHandlesByLabel.ContainsKey(option.Label) || _windowsByLabel.ContainsKey(option.Label))
        {
            throw new InvalidOperationException($"A window or webview with label '{option.Label}' already exists.");
        }

        if (!double.IsFinite(option.Width) || !double.IsFinite(option.Height)
            || option.Width <= 0 || option.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(option), "Window dimensions must be positive finite numbers.");
        }

        int width = checked((int)Math.Clamp(Math.Round(option.Width), 1, int.MaxValue));
        int height = checked((int)Math.Clamp(Math.Round(option.Height), 1, int.MaxValue));
        nint handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            option.Title,
            NativeMethods.WsOverlappedWindow | (visible ? NativeMethods.WsVisible : 0),
            unchecked((int)NativeMethods.CwUseDefault),
            unchecked((int)NativeMethods.CwUseDefault),
            width,
            height,
            0,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (handle == 0)
        {
            throw NativeMethods.Win32Error("CreateWindowExW");
        }

        _topLevelLabelsByHandle.Add(handle, option.Label);
        _topLevelHandlesByLabel.Add(option.Label, handle);
        if (visible)
        {
            _ = NativeMethods.ShowWindow(handle, NativeMethods.SwShow);
            _ = NativeMethods.UpdateWindow(handle);
        }
        EmitEvent("window-created", option.Label, "{}");

        if (option.Webviews is { Count: > 0 })
        {
            foreach (WebviewOptions webview in option.Webviews)
            {
                CreateChildWebView(option.Label, webview);
            }
        }
        else
        {
            var window = new WebViewWindow(this, option.Label, ResolveUrl(option.Url), option.Label, handle);
            _windowsByHandle.Add(handle, window);
            _windowsByLabel.Add(option.Label, window);
            if (Environment is not null)
            {
                window.CreateController(Environment);
            }
        }
    }

    private void CreateChildWebView((string WindowLabel, WebviewOptions Webview) request) =>
        CreateChildWebView(request.WindowLabel, request.Webview);

    private void CreateChildWebView(string windowLabel, WebviewOptions webview)
    {
        if (_windowsByLabel.ContainsKey(webview.Label))
        {
            throw new InvalidOperationException($"A webview with label '{webview.Label}' already exists.");
        }

        if (!_topLevelHandlesByLabel.TryGetValue(windowLabel, out nint parent))
        {
            throw new KeyNotFoundException($"No top-level window has label '{windowLabel}'.");
        }

        var existingLabels = new HashSet<string>(_windowsByLabel.Keys, StringComparer.Ordinal);
        ValidateWebview(webview, existingLabels, nameof(webview));
        int x = ScaleForDpi(webview.X, parent);
        int y = ScaleForDpi(webview.Y, parent);
        int width = ScaleForDpi(webview.Width, parent);
        int height = ScaleForDpi(webview.Height, parent);
        nint handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            webview.Label,
            NativeMethods.WsChild | NativeMethods.WsVisible,
            x,
            y,
            width,
            height,
            parent,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (handle == 0)
        {
            throw NativeMethods.Win32Error("CreateWindowExW(child webview host)");
        }

        var child = new WebViewWindow(
            this,
            webview.Label,
            ResolveUrl(webview.Url),
            windowLabel,
            handle,
            webview);
        _windowsByHandle.Add(handle, child);
        _windowsByLabel.Add(webview.Label, child);
        _ = NativeMethods.ShowWindow(handle, NativeMethods.SwShow);
        _ = NativeMethods.UpdateWindow(handle);
        if (Environment is not null)
        {
            child.CreateController(Environment);
        }

        EmitEvent("webview-created", webview.Label, "{}");
    }

    private static (string WindowLabel, WebviewOptions Webview) ParseWebviewOptions(
        JsonElement args,
        string defaultWindowLabel)
    {
        string windowLabel = args.TryGetProperty("windowLabel", out JsonElement windowProperty)
            && windowProperty.ValueKind == JsonValueKind.String
            && windowProperty.GetString() is { Length: > 0 } requestedWindow
                ? requestedWindow
                : defaultWindowLabel;
        JsonElement options = args.TryGetProperty("options", out JsonElement nestedOptions)
            && nestedOptions.ValueKind == JsonValueKind.Object
                ? nestedOptions
                : args;
        string label = RequireString(options, "label");
        string url = RequireString(options, "url");
        double x = GetFiniteNumber(options, "x", 0);
        double y = GetFiniteNumber(options, "y", 0);
        double width = GetFiniteNumber(options, "width", 400);
        double height = GetFiniteNumber(options, "height", 300);
        JsonElement extraConfig = options.TryGetProperty("extraConfig", out JsonElement extra)
            ? extra.Clone()
            : options.Clone();
        return (windowLabel, new WebviewOptions(label, url, x, y, width, height, extraConfig));
    }

    private static double GetFiniteNumber(JsonElement args, string name, double defaultValue)
    {
        if (!args.TryGetProperty(name, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out double value)
            || !double.IsFinite(value))
        {
            throw new ArgumentException($"Command argument '{name}' must be a finite number.", nameof(args));
        }

        return value;
    }

    private static string ResolveUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute)
            && absolute.Scheme.Equals("tauri", StringComparison.OrdinalIgnoreCase)
            && absolute.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://tauri.localhost{absolute.PathAndQuery}{absolute.Fragment}";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        string path = url.Replace('\\', '/').TrimStart('/');
        return $"https://tauri.localhost/{path}";
    }

    private static int ScaleForDpi(double value, nint parent)
    {
        uint dpi = NativeMethods.GetDpiForWindow(parent);
        double scaled = value * (dpi == 0 ? 1d : dpi / 96d);
        return checked((int)Math.Clamp(Math.Round(scaled), int.MinValue, int.MaxValue));
    }

    private void LayoutChildWebviews(string windowLabel)
    {
        if (!_topLevelHandlesByLabel.TryGetValue(windowLabel, out nint parent))
        {
            return;
        }

        foreach (WebViewWindow child in _windowsByLabel.Values)
        {
            if (child.WindowLabel != windowLabel || child.Placement is null)
            {
                continue;
            }

            LayoutWebview(child);
        }
    }

    internal void LayoutWebview(WebViewWindow child)
    {
        if (!_topLevelHandlesByLabel.TryGetValue(child.WindowLabel, out nint parent))
        {
            throw new KeyNotFoundException($"No top-level window has label '{child.WindowLabel}'.");
        }

        if (!NativeMethods.SetWindowPos(
                child.Handle,
                0,
                ScaleForDpi(child.X, parent),
                ScaleForDpi(child.Y, parent),
                ScaleForDpi(child.Width, parent),
                ScaleForDpi(child.Height, parent),
                NativeMethods.SwpNoActivate))
        {
            throw NativeMethods.Win32Error("SetWindowPos(webview host)");
        }
        child.Resize();
    }

    private void SetTopLevelWindowSize(string windowLabel, double width, double height)
    {
        if (!_topLevelHandlesByLabel.TryGetValue(windowLabel, out nint handle))
        {
            throw new KeyNotFoundException($"No top-level window has label '{windowLabel}'.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive.");
        }

        if (!NativeMethods.GetWindowRect(handle, out Rect bounds))
        {
            throw NativeMethods.Win32Error("GetWindowRect");
        }

        if (!NativeMethods.SetWindowPos(
                handle,
                0,
                bounds.Left,
                bounds.Top,
                ScaleForDpi(width, handle),
                ScaleForDpi(height, handle),
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate))
        {
            throw NativeMethods.Win32Error("SetWindowPos");
        }
    }

    private void CreateWebViewEnvironment()
    {
        string userDataFolder = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "NetWiv",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);
        _environmentCompleted = new EnvironmentCompletedHandler(OnEnvironmentCreated);
        NativeMethods.ThrowIfFailed(WebView2Loader.CreateCoreWebView2EnvironmentWithOptions(
            null,
            userDataFolder,
            0,
            _environmentCompleted));
    }

    private int OnEnvironmentCreated(int errorCode, ICoreWebView2Environment? environment)
    {
        _environmentCompleted = null;
        try
        {
            NativeMethods.ThrowIfFailed(errorCode, "WebView2 environment creation callback");
            Environment = environment ?? throw new InvalidOperationException("WebView2 returned a null environment.");
            foreach (WebViewWindow window in _windowsByHandle.Values)
            {
                window.CreateController(environment);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return Marshal.GetHRForException(exception);
        }
    }

    private void RunMessageLoop()
    {
        while (true)
        {
            int result = NativeMethods.GetMessage(out Message message, 0, 0, 0);
            if (result == 0)
            {
                return;
            }

            if (result < 0)
            {
                throw NativeMethods.Win32Error("GetMessageW");
            }

            if (message.HWnd == 0 && message.Value == NativeMethods.WmAppDispatch)
            {
                DrainDispatchQueue();
                continue;
            }

            _ = NativeMethods.TranslateMessage(message);
            _ = NativeMethods.DispatchMessage(message);
        }
    }

    private JsonElement InvokeOnUiThread(string command, JsonElement args, RuntimeCommandContext context)
    {
        if (!context.IsAuthorized)
        {
            throw new UnauthorizedAccessException(
                $"Native command '{command}' was rejected for untrusted origin '{context.Source}'.");
        }

        if (command == "plugin:window|get_all_windows")
        {
            return JsonStringArray(_topLevelHandlesByLabel.Keys.Order(StringComparer.Ordinal));
        }

        if (command.StartsWith("plugin:menu|", StringComparison.Ordinal)
            || command.StartsWith("plugin:tray|", StringComparison.Ordinal)
            || command == "plugin:resources|close")
        {
            return _nativeResources.Invoke(command, args, context);
        }

        if (command == "plugin:webview|get_all_webviews")
        {
            return JsonWebviews(_windowsByLabel.Values.OrderBy(static value => value.Label, StringComparer.Ordinal));
        }

        if (command == "plugin:webview|create_webview_window")
        {
            WindowOptions options = ParseWebviewWindowOptions(args);
            CreateTopLevelWindow(options, GetOptionalBoolean(args.GetProperty("options"), "visible", true));
            return JsonNull();
        }

        if (command == "plugin:webview|create_webview")
        {
            string callerWindow = GetWindow(context.WebviewLabel).WindowLabel;
            CreateChildWebView(ParseWebviewOptions(args, callerWindow));
            return JsonNull();
        }

        if (command.StartsWith("plugin:webview|", StringComparison.Ordinal))
        {
            string webviewLabel = GetOptionalString(args, "label") ?? context.WebviewLabel;
            return InvokeWebviewCommand(command, args, GetWindow(webviewLabel));
        }

        string callerWindowLabel = GetWindow(context.WebviewLabel).WindowLabel;
        string windowLabel = GetOptionalString(args, "label") ?? callerWindowLabel;
        if (!_topLevelHandlesByLabel.TryGetValue(windowLabel, out nint topLevelHandle))
        {
            throw new KeyNotFoundException($"No top-level window has label '{windowLabel}'.");
        }

        switch (command)
        {
            case "window.setTitle":
                SetWindowTitle(topLevelHandle, RequireString(args, "title"));
                return JsonNull();
            case "plugin:window|set_title":
                SetWindowTitle(topLevelHandle, RequireString(args, "value"));
                return JsonNull();
            case "window.show":
            case "plugin:window|show":
                _ = NativeMethods.ShowWindow(topLevelHandle, NativeMethods.SwShow);
                return JsonNull();
            case "window.hide":
            case "plugin:window|hide":
                _ = NativeMethods.ShowWindow(topLevelHandle, NativeMethods.SwHide);
                return JsonNull();
            case "window.minimize":
            case "plugin:window|minimize":
                _ = NativeMethods.ShowWindow(topLevelHandle, NativeMethods.SwMinimize);
                return JsonNull();
            case "window.maximize":
            case "plugin:window|maximize":
                _ = NativeMethods.ShowWindow(topLevelHandle, NativeMethods.SwMaximize);
                return JsonNull();
            case "plugin:window|unmaximize":
            case "plugin:window|unminimize":
                _ = NativeMethods.ShowWindow(topLevelHandle, 9);
                return JsonNull();
            case "plugin:window|toggle_maximize":
                _ = NativeMethods.ShowWindow(
                    topLevelHandle,
                    NativeMethods.IsZoomed(topLevelHandle) ? 9 : NativeMethods.SwMaximize);
                return JsonNull();
            case "plugin:window|is_maximized":
                return JsonBoolean(NativeMethods.IsZoomed(topLevelHandle));
            case "plugin:window|is_minimized":
                return JsonBoolean(NativeMethods.IsIconic(topLevelHandle));
            case "plugin:window|set_focus":
                _ = NativeMethods.SetForegroundWindow(topLevelHandle);
                return JsonNull();
            case "plugin:window|is_visible":
                return JsonBoolean(NativeMethods.IsWindowVisible(topLevelHandle));
            case "plugin:window|is_focused":
                return JsonBoolean(NativeMethods.GetForegroundWindow() == topLevelHandle);
            case "plugin:window|title":
                return JsonString(GetWindowTitle(topLevelHandle));
            case "plugin:window|scale_factor":
                return JsonNumber(NativeMethods.GetDpiForWindow(topLevelHandle) / 96d);
            case "plugin:window|is_resizable":
                return JsonBoolean(HasWindowStyle(topLevelHandle, NativeMethods.WsThickFrame));
            case "plugin:window|set_resizable":
                SetWindowStyle(topLevelHandle, NativeMethods.WsThickFrame, GetRequiredBoolean(args, "value"));
                return JsonNull();
            case "plugin:window|is_decorated":
                return JsonBoolean(HasWindowStyle(topLevelHandle, NativeMethods.WsCaption));
            case "plugin:window|set_decorations":
                SetWindowStyle(
                    topLevelHandle,
                    NativeMethods.WsCaption | NativeMethods.WsThickFrame,
                    GetRequiredBoolean(args, "value"));
                return JsonNull();
            case "plugin:window|set_always_on_top":
                if (!NativeMethods.SetWindowPos(
                        topLevelHandle,
                        GetRequiredBoolean(args, "value") ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
                        0,
                        0,
                        0,
                        0,
                        0x0001 | 0x0002 | NativeMethods.SwpNoActivate))
                {
                    throw NativeMethods.Win32Error("SetWindowPos(always-on-top)");
                }
                return JsonNull();
            case "window.setSize":
                SetTopLevelWindowSize(
                    windowLabel,
                    GetFiniteNumber(args, "width", 800),
                    GetFiniteNumber(args, "height", 600));
                return JsonNull();
            case "plugin:window|set_size":
                (double width, double height, bool logicalSize) = GetDpiSize(args.GetProperty("value"));
                SetTopLevelWindowSize(windowLabel, logicalSize ? width : width * 96d / NativeMethods.GetDpiForWindow(topLevelHandle), logicalSize ? height : height * 96d / NativeMethods.GetDpiForWindow(topLevelHandle));
                return JsonNull();
            case "plugin:window|set_position":
                (double x, double y, bool logicalPosition) = GetDpiPosition(args.GetProperty("value"));
                SetTopLevelWindowPosition(topLevelHandle, x, y, logicalPosition);
                return JsonNull();
            case "plugin:window|inner_position":
                var clientOrigin = new Point();
                if (!NativeMethods.ClientToScreen(topLevelHandle, ref clientOrigin))
                {
                    throw NativeMethods.Win32Error("ClientToScreen");
                }
                return JsonPosition(clientOrigin.X, clientOrigin.Y);
            case "plugin:window|outer_position":
                NativeMethods.GetWindowRect(topLevelHandle, out Rect outerPosition);
                return JsonPosition(outerPosition.Left, outerPosition.Top);
            case "plugin:window|inner_size":
                NativeMethods.GetClientRect(topLevelHandle, out Rect innerSize);
                return JsonSize(innerSize.Right, innerSize.Bottom);
            case "plugin:window|outer_size":
                NativeMethods.GetWindowRect(topLevelHandle, out Rect outerSize);
                return JsonSize(outerSize.Right - outerSize.Left, outerSize.Bottom - outerSize.Top);
            case "window.close":
            case "plugin:window|close":
                _ = NativeMethods.PostMessage(topLevelHandle, NativeMethods.WmClose, 0, 0);
                return JsonNull();
            case "plugin:window|destroy":
                _ = NativeMethods.DestroyWindow(topLevelHandle);
                return JsonNull();
            case "webview.navigate":
                GetWindow(context.WebviewLabel).Navigate(RequireString(args, "url"));
                return JsonNull();
            case "webview.postMessage":
                GetWindow(context.WebviewLabel).PostMessage(args.TryGetProperty("message", out JsonElement message)
                    ? message.GetRawText()
                    : args.GetRawText());
                return JsonNull();
            default:
                throw new NotSupportedException(
                    $"Windows native window command '{command}' is not implemented.");
        }
    }

    private static string RequireString(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new ArgumentException($"Command argument '{name}' must be a non-empty string.", nameof(args));
    }

    private JsonElement InvokeWebviewCommand(string command, JsonElement args, WebViewWindow webview)
    {
        switch (command)
        {
            case "plugin:webview|set_webview_focus":
                _ = NativeMethods.SetForegroundWindow(_topLevelHandlesByLabel[webview.WindowLabel]);
                webview.Focus();
                return JsonNull();
            case "plugin:webview|webview_hide":
                webview.SetVisible(false);
                return JsonNull();
            case "plugin:webview|webview_show":
                webview.SetVisible(true);
                return JsonNull();
            case "plugin:webview|set_webview_zoom":
                webview.SetZoom(GetFiniteNumber(args, "value", 1));
                return JsonNull();
            case "plugin:webview|webview_size":
                if (!NativeMethods.GetClientRect(webview.Handle, out Rect size))
                {
                    throw NativeMethods.Win32Error("GetClientRect(webview)");
                }
                return JsonSize(size.Right, size.Bottom);
            case "plugin:webview|webview_position":
                var origin = new Point();
                if (!NativeMethods.ClientToScreen(webview.Handle, ref origin))
                {
                    throw NativeMethods.Win32Error("ClientToScreen(webview)");
                }
                nint parent = _topLevelHandlesByLabel[webview.WindowLabel];
                if (!NativeMethods.ScreenToClient(parent, ref origin))
                {
                    throw NativeMethods.Win32Error("ScreenToClient(webview parent)");
                }
                return JsonPosition(origin.X, origin.Y);
            case "plugin:webview|set_webview_size":
                (double width, double height, bool logical) = GetDpiSize(args.GetProperty("value"));
                double dpi = NativeMethods.GetDpiForWindow(_topLevelHandlesByLabel[webview.WindowLabel]);
                webview.SetLogicalBounds(null, null, logical ? width : width * 96d / dpi, logical ? height : height * 96d / dpi);
                return JsonNull();
            case "plugin:webview|set_webview_position":
                (double x, double y, bool logicalPosition) = GetDpiPosition(args.GetProperty("value"));
                double positionDpi = NativeMethods.GetDpiForWindow(_topLevelHandlesByLabel[webview.WindowLabel]);
                webview.SetLogicalBounds(logicalPosition ? x : x * 96d / positionDpi, logicalPosition ? y : y * 96d / positionDpi, null, null);
                return JsonNull();
            case "plugin:webview|webview_close":
                CloseWebview(webview);
                return JsonNull();
            default:
                throw new NotSupportedException($"Windows native webview command '{command}' is not implemented.");
        }
    }

    private void CloseWebview(WebViewWindow webview)
    {
        _windowsByHandle.Remove(webview.Handle);
        _windowsByLabel.Remove(webview.Label);
        webview.Close();
        if (webview.Placement is not null)
        {
            _ = NativeMethods.DestroyWindow(webview.Handle);
        }
        EmitEvent("webview-destroyed", webview.Label, "{}");
    }

    private static WindowOptions ParseWebviewWindowOptions(JsonElement args)
    {
        JsonElement options = args.GetProperty("options");
        string label = RequireString(options, "label");
        string title = GetOptionalString(options, "title") ?? label;
        string url = GetOptionalString(options, "url") ?? "index.html";
        double width = GetFiniteNumber(options, "width", 800);
        double height = GetFiniteNumber(options, "height", 600);
        return new WindowOptions(label, title, url, width, height, options.Clone());
    }

    private static string? GetOptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetOptionalBoolean(JsonElement args, string name, bool defaultValue) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out JsonElement property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;

    private static bool GetRequiredBoolean(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out JsonElement property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        throw new ArgumentException($"Command argument '{name}' must be a boolean.", nameof(args));
    }

    private static (double Width, double Height, bool Logical) GetDpiSize(JsonElement value)
    {
        bool logical = value.TryGetProperty("Logical", out JsonElement logicalValue);
        JsonElement data = logical
            ? logicalValue
            : value.TryGetProperty("Physical", out JsonElement physicalValue)
                ? physicalValue
                : value.TryGetProperty("data", out JsonElement nestedValue)
                    ? nestedValue
                    : value;
        return (
            GetFiniteNumber(data, "width", double.NaN),
            GetFiniteNumber(data, "height", double.NaN),
            logical);
    }

    private static (double X, double Y, bool Logical) GetDpiPosition(JsonElement value)
    {
        bool logical = value.TryGetProperty("Logical", out JsonElement logicalValue);
        JsonElement data = logical
            ? logicalValue
            : value.TryGetProperty("Physical", out JsonElement physicalValue)
                ? physicalValue
                : value.TryGetProperty("data", out JsonElement nestedValue)
                    ? nestedValue
                    : value;
        return (
            GetFiniteNumber(data, "x", double.NaN),
            GetFiniteNumber(data, "y", double.NaN),
            logical);
    }

    private static void SetWindowTitle(nint handle, string title)
    {
        if (!NativeMethods.SetWindowText(handle, title))
        {
            throw NativeMethods.Win32Error("SetWindowTextW");
        }
    }

    private static unsafe string GetWindowTitle(nint handle)
    {
        int length = NativeMethods.GetWindowTextLength(handle);
        Span<char> buffer = length < 512 ? stackalloc char[length + 1] : new char[length + 1];
        fixed (char* text = buffer)
        {
            int copied = NativeMethods.GetWindowText(handle, text, buffer.Length);
            return new string(text, 0, copied);
        }
    }

    private static bool HasWindowStyle(nint handle, uint style) =>
        ((nuint)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle) & style) == style;

    private static void SetWindowStyle(nint handle, uint style, bool enabled)
    {
        nuint current = (nuint)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle);
        nuint updated = enabled ? current | style : current & ~style;
        _ = NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlStyle, (nint)updated);
        if (!NativeMethods.SetWindowPos(
                handle,
                0,
                0,
                0,
                0,
                0,
                0x0001 | 0x0002 | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged))
        {
            throw NativeMethods.Win32Error("SetWindowPos(frame-changed)");
        }
    }

    private static void SetTopLevelWindowPosition(nint handle, double x, double y, bool logical)
    {
        double scale = logical ? NativeMethods.GetDpiForWindow(handle) / 96d : 1d;
        if (!NativeMethods.SetWindowPos(
                handle,
                0,
                checked((int)Math.Round(x * scale)),
                checked((int)Math.Round(y * scale)),
                0,
                0,
                0x0001 | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate))
        {
            throw NativeMethods.Win32Error("SetWindowPos(position)");
        }
    }

    private static JsonElement JsonNull() => JsonDocument.Parse("null").RootElement.Clone();

    private static JsonElement JsonBoolean(bool value) =>
        JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();

    private static JsonElement JsonNumber(double value) =>
        JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();

    private static JsonElement JsonString(string value) =>
        JsonDocument.Parse("\"" + JsonEncodedText.Encode(value) + "\"").RootElement.Clone();

    private static JsonElement JsonSize(int width, int height) =>
        JsonDocument.Parse($"{{\"width\":{width},\"height\":{height}}}").RootElement.Clone();

    private static JsonElement JsonPosition(int x, int y) =>
        JsonDocument.Parse($"{{\"x\":{x},\"y\":{y}}}").RootElement.Clone();

    private static JsonElement JsonStringArray(IEnumerable<string> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (string value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement JsonWebviews(IEnumerable<WebViewWindow> webviews)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (WebViewWindow webview in webviews)
            {
                writer.WriteStartObject();
                writer.WriteString("windowLabel", webview.WindowLabel);
                writer.WriteString("label", webview.Label);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private WebViewWindow GetWindow(string label) =>
        _windowsByLabel.TryGetValue(label, out WebViewWindow? window)
            ? window
            : throw new KeyNotFoundException($"No window has label '{label}'.");

    internal string GetWindowLabel(string webviewLabel) => GetWindow(webviewLabel).WindowLabel;

    internal bool HasWebview(string webviewLabel) => _windowsByLabel.ContainsKey(webviewLabel);

    internal nint GetTopLevelHandle(string windowLabel) =>
        _topLevelHandlesByLabel.TryGetValue(windowLabel, out nint handle)
            ? handle
            : throw new KeyNotFoundException($"No top-level window has label '{windowLabel}'.");

    private void DrainDispatchQueue()
    {
        while (_dispatchQueue.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return;
            }
        }
    }

    private void CloseAllWindows(int exitCode)
    {
        _exitCode = exitCode;
        foreach (nint handle in _topLevelLabelsByHandle.Keys.ToArray())
        {
            _ = NativeMethods.DestroyWindow(handle);
        }

        if (_topLevelLabelsByHandle.Count == 0)
        {
            NativeMethods.PostQuitMessage(exitCode);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint WindowProcedure(nint handle, uint message, nuint wParam, nint lParam)
    {
        WindowsDesktopRuntime? runtime = s_activeRuntime;
        if (runtime is null)
        {
            return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
        }

        try
        {
            switch (message)
            {
                case NativeMethods.WmCommand:
                    runtime._nativeResources.HandleMenuCommand((uint)(wParam & 0xffff));
                    return 0;
                case NativeMethods.WmAppTray:
                    runtime._nativeResources.HandleTrayMessage((uint)wParam, (uint)lParam);
                    return 0;
                case NativeMethods.WmSize:
                    if (runtime._windowsByHandle.TryGetValue(handle, out WebViewWindow? resized))
                    {
                        resized.Resize();
                    }
                    if (runtime._topLevelLabelsByHandle.TryGetValue(handle, out string? resizedLabel))
                    {
                        runtime.LayoutChildWebviews(resizedLabel);
                        runtime.EmitEvent("window-resized", resizedLabel, "{}");
                    }
                    return 0;
                case NativeMethods.WmDpiChanged:
                    if (lParam != 0)
                    {
                        Rect suggested = *(Rect*)lParam;
                        _ = NativeMethods.SetWindowPos(
                            handle,
                            0,
                            suggested.Left,
                            suggested.Top,
                            suggested.Right - suggested.Left,
                            suggested.Bottom - suggested.Top,
                            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
                    }
                    if (runtime._topLevelLabelsByHandle.TryGetValue(handle, out string? dpiLabel))
                    {
                        runtime.LayoutChildWebviews(dpiLabel);
                    }
                    return 0;
                case NativeMethods.WmClose:
                    if (runtime._topLevelLabelsByHandle.TryGetValue(handle, out string? closingLabel)
                        && runtime.Context.HasEventListeners?.Invoke("tauri://close-requested", closingLabel) == true)
                    {
                        runtime.EmitEvent("tauri://close-requested", closingLabel, "null");
                        return 0;
                    }
                    _ = NativeMethods.DestroyWindow(handle);
                    return 0;
                case NativeMethods.WmDestroy:
                    if (runtime._windowsByHandle.Remove(handle, out WebViewWindow? closed))
                    {
                        runtime._windowsByLabel.Remove(closed.Label);
                        closed.Close();
                        runtime.EmitEvent("webview-destroyed", closed.Label, "{}");
                    }

                    if (runtime._topLevelLabelsByHandle.Remove(handle, out string? topLevelLabel))
                    {
                        runtime._nativeResources.HandleWindowDestroyed(handle);
                        runtime._topLevelHandlesByLabel.Remove(topLevelLabel);
                        runtime.EmitEvent("window-destroyed", topLevelLabel, "{}");
                    }

                    if (runtime._topLevelLabelsByHandle.Count == 0)
                    {
                        NativeMethods.PostQuitMessage(runtime._exitCode);
                    }
                    return 0;
                default:
                    return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
            }
        }
        catch (Exception exception)
        {
            runtime._fatalError ??= exception;
            NativeMethods.PostQuitMessage(1);
            return 0;
        }
    }
}
