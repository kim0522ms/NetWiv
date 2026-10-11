using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NetWiv.Platform.Windows.Interop;
using NetWiv.Runtime;

namespace NetWiv.Platform.Windows.WebView2;

internal sealed class WebViewWindow
{
    private readonly WindowsDesktopRuntime _runtime;
    private ControllerCompletedHandler? _controllerCompleted;
    private AddScriptCompletedHandler? _scriptCompleted;
    private WebMessageReceivedHandler? _messageReceived;
    private WebResourceRequestedHandler? _resourceRequested;
    private EventRegistrationToken _messageToken;
    private EventRegistrationToken _resourceToken;

    internal WebViewWindow(
        WindowsDesktopRuntime runtime,
        string label,
        string url,
        string windowLabel,
        nint handle,
        WebviewOptions? placement = null)
    {
        _runtime = runtime;
        Label = label;
        Url = url;
        WindowLabel = windowLabel;
        Handle = handle;
        Placement = placement;
        if (placement is not null)
        {
            X = placement.X;
            Y = placement.Y;
            Width = placement.Width;
            Height = placement.Height;
        }
    }

    internal string Label { get; }
    internal string Url { get; }
    internal string WindowLabel { get; }
    internal nint Handle { get; }
    internal WebviewOptions? Placement { get; }
    internal double X { get; private set; }
    internal double Y { get; private set; }
    internal double Width { get; private set; }
    internal double Height { get; private set; }
    internal ICoreWebView2Controller? Controller { get; private set; }
    internal ICoreWebView2? WebView { get; private set; }

    internal void CreateController(ICoreWebView2Environment environment)
    {
        _controllerCompleted = new ControllerCompletedHandler(OnControllerCreated);
        NativeMethods.ThrowIfFailed(
            environment.CreateCoreWebView2Controller(Handle, _controllerCompleted),
            "ICoreWebView2Environment.CreateCoreWebView2Controller");
    }

    internal void Resize()
    {
        if (Controller is null || !NativeMethods.GetClientRect(Handle, out Rect bounds))
        {
            return;
        }

        NativeMethods.ThrowIfFailed(Controller.SetBounds(bounds), "ICoreWebView2Controller.put_Bounds");
        NativeMethods.ThrowIfFailed(
            Controller.NotifyParentWindowPositionChanged(),
            "ICoreWebView2Controller.NotifyParentWindowPositionChanged");
    }

    internal void Navigate(string uri)
    {
        ICoreWebView2 webView = WebView ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        NativeMethods.ThrowIfFailed(webView.Navigate(uri), "ICoreWebView2.Navigate");
    }

    internal void EvaluateScript(string script)
    {
        ICoreWebView2 webView = WebView ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        NativeMethods.ThrowIfFailed(webView.ExecuteScript(script, null), "ICoreWebView2.ExecuteScript");
    }

    internal void PostMessage(string json)
    {
        ICoreWebView2 webView = WebView ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        NativeMethods.ThrowIfFailed(webView.PostWebMessageAsJson(json), "ICoreWebView2.PostWebMessageAsJson");
    }

    internal void SetVisible(bool visible)
    {
        ICoreWebView2Controller controller = Controller ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        NativeMethods.ThrowIfFailed(controller.SetIsVisible(visible ? 1 : 0));
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.SwShow : NativeMethods.SwHide);
    }

    internal void Focus()
    {
        ICoreWebView2Controller controller = Controller ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        _ = NativeMethods.SetFocus(Handle);
        NativeMethods.ThrowIfFailed(controller.MoveFocus(0));
    }

    internal void SetZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), "WebView zoom must be a positive finite number.");
        }

        ICoreWebView2Controller controller = Controller ?? throw new InvalidOperationException(
            $"WebView '{Label}' is not initialized.");
        NativeMethods.ThrowIfFailed(controller.SetZoomFactor(zoom));
    }

    internal void SetLogicalBounds(double? x, double? y, double? width, double? height)
    {
        if (Placement is null)
        {
            throw new InvalidOperationException($"Primary webview '{Label}' always follows its top-level window bounds.");
        }

        if (x is not null) X = x.Value;
        if (y is not null) Y = y.Value;
        if (width is not null) Width = width.Value;
        if (height is not null) Height = height.Value;
        _runtime.LayoutWebview(this);
    }

    internal void Close()
    {
        ICoreWebView2? webView = WebView;
        WebView = null;
        if (webView is not null)
        {
            if (_messageToken.Value != 0)
            {
                _ = webView.RemoveWebMessageReceived(_messageToken);
            }

            if (_resourceToken.Value != 0)
            {
                _ = webView.RemoveWebResourceRequested(_resourceToken);
                _ = webView.RemoveWebResourceRequestedFilter("*", WebResourceContext.All);
            }
        }

        ICoreWebView2Controller? controller = Controller;
        Controller = null;
        if (controller is not null)
        {
            _ = controller.Close();
        }

        _controllerCompleted = null;
        _scriptCompleted = null;
        _messageReceived = null;
        _resourceRequested = null;
        _messageToken = default;
        _resourceToken = default;
    }

    private int OnControllerCreated(int errorCode, ICoreWebView2Controller? controller)
    {
        _controllerCompleted = null;
        try
        {
            NativeMethods.ThrowIfFailed(errorCode, "WebView2 controller creation callback");
            Controller = controller ?? throw new InvalidOperationException("WebView2 returned a null controller.");
            NativeMethods.ThrowIfFailed(controller.GetCoreWebView2(out ICoreWebView2? webView));
            WebView = webView ?? throw new InvalidOperationException("WebView2 returned a null core webview.");

            Resize();
            if (Placement is not null)
            {
                _runtime.LayoutWebview(this);
            }
            _messageReceived = new WebMessageReceivedHandler(OnWebMessageReceived);
            NativeMethods.ThrowIfFailed(WebView.AddWebMessageReceived(_messageReceived, out _messageToken));

            _resourceRequested = new WebResourceRequestedHandler(OnWebResourceRequested);
            NativeMethods.ThrowIfFailed(WebView.AddWebResourceRequested(_resourceRequested, out _resourceToken));
            NativeMethods.ThrowIfFailed(WebView.AddWebResourceRequestedFilter("*", WebResourceContext.All));

            _scriptCompleted = new AddScriptCompletedHandler(OnInitializationScriptAdded);
            NativeMethods.ThrowIfFailed(WebView.AddScriptToExecuteOnDocumentCreated(
                BuildInitializationScript(),
                _scriptCompleted));
            return 0;
        }
        catch (Exception exception)
        {
            _runtime.Fail(exception);
            return Marshal.GetHRForException(exception);
        }
    }

    private string BuildInitializationScript()
    {
        string windowLabel = JsonEncodedText.Encode(WindowLabel).ToString();
        string webviewLabel = JsonEncodedText.Encode(Label).ToString();
        return $$"""
            (() => {
              const bridge = Object.freeze({
                os: 'windows',
                windowLabel: "{{windowLabel}}",
                webviewLabel: "{{webviewLabel}}",
                postMessage(message) {
                  chrome.webview.postMessage(typeof message === 'string' ? JSON.parse(message) : message);
                }
              });
              Object.defineProperty(globalThis, '__NETWIV_IPC__', {
                value: bridge, configurable: false, writable: false
              });
            })();
            {{_runtime.Context.InitializationScript}}
            """;
    }

    private int OnInitializationScriptAdded(int errorCode, nint scriptId)
    {
        _scriptCompleted = null;
        try
        {
            NativeMethods.ThrowIfFailed(errorCode, "AddScriptToExecuteOnDocumentCreated callback");
            if (Placement is not null)
            {
                _runtime.LayoutWebview(this);
            }
            NativeMethods.ThrowIfFailed(WebView!.Navigate(Url), "ICoreWebView2.Navigate");
            _runtime.EmitEvent("webview-ready", Label, "{}");
            return 0;
        }
        catch (Exception exception)
        {
            _runtime.Fail(exception);
            return Marshal.GetHRForException(exception);
        }
    }

    private int OnWebMessageReceived(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args)
    {
        nint sourcePointer = 0;
        nint jsonPointer = 0;
        try
        {
            NativeMethods.ThrowIfFailed(args.GetSource(out sourcePointer));
            NativeMethods.ThrowIfFailed(args.GetWebMessageAsJson(out jsonPointer));
            string source = Marshal.PtrToStringUni(sourcePointer)
                ?? throw new InvalidOperationException("WebView2 returned a null message source.");
            string json = Marshal.PtrToStringUni(jsonPointer)
                ?? throw new InvalidOperationException("WebView2 returned a null JSON message.");
            _runtime.Context.OnMessage(new RuntimeMessage(Label, new Uri(source), json, WindowLabel));
            return 0;
        }
        catch (Exception exception)
        {
            _runtime.Fail(exception);
            return Marshal.GetHRForException(exception);
        }
        finally
        {
            if (sourcePointer != 0) Marshal.FreeCoTaskMem(sourcePointer);
            if (jsonPointer != 0) Marshal.FreeCoTaskMem(jsonPointer);
        }
    }

    private int OnWebResourceRequested(ICoreWebView2 sender, ICoreWebView2WebResourceRequestedEventArgs args)
    {
        nint uriPointer = 0;
        nint methodPointer = 0;
        try
        {
            NativeMethods.ThrowIfFailed(args.GetRequest(out ICoreWebView2WebResourceRequest? request));
            if (request is null)
            {
                return 0;
            }

            NativeMethods.ThrowIfFailed(request.GetUri(out uriPointer));
            NativeMethods.ThrowIfFailed(request.GetMethod(out methodPointer));
            string? uriText = Marshal.PtrToStringUni(uriPointer);
            string method = Marshal.PtrToStringUni(methodPointer) ?? "GET";
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            {
                return 0;
            }

            if (!IsAssetUri(uri))
            {
                return 0;
            }

            NativeMethods.ThrowIfFailed(request.GetHeaders(out ICoreWebView2HttpRequestHeaders? requestHeaders));
            AssetResponse? asset = _runtime.Context.ResolveAsset(
                new AssetRequest(uri, method, ReadHeaders(requestHeaders)));
            if (asset is null)
            {
                return 0;
            }

            var stream = new ReadOnlyComStream(asset.Body);
            string headers = BuildResponseHeaders(asset);
            string reason = ReasonPhrase(asset.StatusCode);
            NativeMethods.ThrowIfFailed(_runtime.Environment!.CreateWebResourceResponse(
                stream,
                asset.StatusCode,
                reason,
                headers,
                out ICoreWebView2WebResourceResponse? response));
            if (response is null)
            {
                throw new InvalidOperationException("WebView2 returned a null web resource response.");
            }

            NativeMethods.ThrowIfFailed(args.SetResponse(response));
            return 0;
        }
        catch (Exception exception)
        {
            _runtime.Fail(exception);
            return Marshal.GetHRForException(exception);
        }
        finally
        {
            if (uriPointer != 0) Marshal.FreeCoTaskMem(uriPointer);
            if (methodPointer != 0) Marshal.FreeCoTaskMem(methodPointer);
        }
    }

    private static bool IsAssetUri(Uri uri) =>
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        && uri.Host.Equals("tauri.localhost", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ReadHeaders(ICoreWebView2HttpRequestHeaders? headers)
    {
        if (headers is null)
        {
            return EmptyHeaders.Instance;
        }

        NativeMethods.ThrowIfFailed(headers.GetIterator(out ICoreWebView2HttpHeadersCollectionIterator? iterator));
        if (iterator is null)
        {
            return EmptyHeaders.Instance;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        NativeMethods.ThrowIfFailed(iterator.GetHasCurrentHeader(out int hasCurrent));
        while (hasCurrent != 0)
        {
            nint namePointer = 0;
            nint valuePointer = 0;
            try
            {
                NativeMethods.ThrowIfFailed(iterator.GetCurrentHeader(out namePointer, out valuePointer));
                string name = Marshal.PtrToStringUni(namePointer) ?? string.Empty;
                string value = Marshal.PtrToStringUni(valuePointer) ?? string.Empty;
                if (result.TryGetValue(name, out string? existing))
                {
                    result[name] = $"{existing}, {value}";
                }
                else
                {
                    result.Add(name, value);
                }
            }
            finally
            {
                if (namePointer != 0) Marshal.FreeCoTaskMem(namePointer);
                if (valuePointer != 0) Marshal.FreeCoTaskMem(valuePointer);
            }

            NativeMethods.ThrowIfFailed(iterator.MoveNext(out hasCurrent));
        }

        return result;
    }

    private static string BuildResponseHeaders(AssetResponse asset)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, "Content-Type", asset.ContentType);
        AppendHeader(builder, "Content-Length", asset.Body.Length.ToString(CultureInfo.InvariantCulture));
        if (asset.Headers is not null)
        {
            foreach ((string name, string value) in asset.Headers)
            {
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AppendHeader(builder, name, value);
            }
        }

        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, string name, string value)
    {
        if (name.Contains('\r') || name.Contains('\n') || value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidOperationException("Asset response headers cannot contain CR or LF characters.");
        }

        builder.Append(name).Append(": ").Append(value).Append("\r\n");
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Response",
    };

    private sealed class EmptyHeaders : IReadOnlyDictionary<string, string>
    {
        internal static readonly EmptyHeaders Instance = new();
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public string this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        public bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            return false;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
