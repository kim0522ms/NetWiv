using System.Text.Json;

namespace NetWiv;

public sealed class WindowBuilder
{
    private readonly AppHandle _app;
    private readonly string _label;
    private string? _title;
    private double? _x;
    private double? _y;
    private double? _width;
    private double? _height;
    private double? _minWidth;
    private double? _minHeight;
    private double? _maxWidth;
    private double? _maxHeight;
    private bool? _center;
    private bool? _preventOverflow;
    private (uint Width, uint Height)? _preventOverflowMargin;
    private bool? _visible;
    private bool? _resizable;
    private bool? _maximizable;
    private bool? _minimizable;
    private bool? _closable;
    private bool? _decorations;
    private bool? _fullscreen;
    private bool? _focus;
    private bool? _focusable;
    private bool? _transparent;
    private bool? _maximized;
    private bool? _alwaysOnTop;
    private bool? _alwaysOnBottom;
    private bool? _contentProtected;
    private bool? _skipTaskbar;
    private bool? _shadow;
    private bool? _visibleOnAllWorkspaces;
    private Theme? _theme;
    private TitleBarStyle? _titleBarStyle;
    private Effects? _windowEffects;
    private RgbaColor? _backgroundColor;
    private BackgroundThrottlingPolicy? _backgroundThrottling;
    private bool? _javascriptDisabled;
    private ScrollBarStyle? _scrollBarStyle;
    private string? _parent;

    public WindowBuilder(AppHandle app, string label)
    {
        ArgumentNullException.ThrowIfNull(app);
        ValidateLabel(label);
        _app = app;
        _label = label;
    }

    public WindowBuilder Title(string title) { ArgumentNullException.ThrowIfNull(title); _title = title; return this; }
    public WindowBuilder Position(double x, double y)
    {
        DpiValidation.Finite(x, nameof(x));
        DpiValidation.Finite(y, nameof(y));
        _x = x;
        _y = y;
        return this;
    }
    public WindowBuilder InnerSize(double width, double height) { SetSize(width, height); return this; }
    public WindowBuilder MinInnerSize(double width, double height) { SetConstraintSize(width, height, true); return this; }
    public WindowBuilder MaxInnerSize(double width, double height) { SetConstraintSize(width, height, false); return this; }
    public WindowBuilder Centered(bool value = true) { _center = value; return this; }
    public WindowBuilder PreventOverflow(bool value = true) { _preventOverflow = value; _preventOverflowMargin = null; return this; }
    public WindowBuilder PreventOverflow(uint marginWidth, uint marginHeight) { _preventOverflow = null; _preventOverflowMargin = (marginWidth, marginHeight); return this; }
    public WindowBuilder Visible(bool value = true) { _visible = value; return this; }
    public WindowBuilder Resizable(bool value = true) { _resizable = value; return this; }
    public WindowBuilder Maximizable(bool value = true) { _maximizable = value; return this; }
    public WindowBuilder Minimizable(bool value = true) { _minimizable = value; return this; }
    public WindowBuilder Closable(bool value = true) { _closable = value; return this; }
    public WindowBuilder Decorations(bool value = true) { _decorations = value; return this; }
    public WindowBuilder Fullscreen(bool value = true) { _fullscreen = value; return this; }
    public WindowBuilder Focused(bool value = true) { _focus = value; return this; }
    public WindowBuilder Focusable(bool value = true) { _focusable = value; return this; }
    public WindowBuilder Transparent(bool value = true) { _transparent = value; return this; }
    public WindowBuilder Maximized(bool value = true) { _maximized = value; return this; }
    public WindowBuilder AlwaysOnTop(bool value = true) { _alwaysOnTop = value; return this; }
    public WindowBuilder AlwaysOnBottom(bool value = true) { _alwaysOnBottom = value; return this; }
    public WindowBuilder ContentProtected(bool value = true) { _contentProtected = value; return this; }
    public WindowBuilder SkipTaskbar(bool value = true) { _skipTaskbar = value; return this; }
    public WindowBuilder Shadow(bool value = true) { _shadow = value; return this; }
    public WindowBuilder VisibleOnAllWorkspaces(bool value = true) { _visibleOnAllWorkspaces = value; return this; }
    public WindowBuilder Theme(Theme value) { _theme = value; return this; }
    public WindowBuilder TitleBarStyle(TitleBarStyle value) { _titleBarStyle = value; return this; }
    public WindowBuilder WindowEffects(Effects value) { _windowEffects = value ?? throw new ArgumentNullException(nameof(value)); return this; }
    public WindowBuilder BackgroundColor(RgbaColor value) { _backgroundColor = value; return this; }
    public WindowBuilder BackgroundThrottling(BackgroundThrottlingPolicy value) { _backgroundThrottling = value; return this; }
    public WindowBuilder JavascriptDisabled(bool value = true) { _javascriptDisabled = value; return this; }
    public WindowBuilder ScrollBarStyle(ScrollBarStyle value) { _scrollBarStyle = value; return this; }
    public WindowBuilder Parent(Window? parent) { _parent = parent?.Label; return this; }
    public WindowBuilder ParentLabel(string? label)
    {
        if (label is not null) ValidateLabel(label);
        _parent = label;
        return this;
    }

    public async Task<Window> BuildAsync(CancellationToken cancellationToken = default)
    {
        await _app.InvokeAsync("plugin:window|create", WriteArguments(null), cancellationToken);
        return _app.RegisterWindow(_label);
    }

    internal JsonElement WriteArguments(string? url) => ApiJson.Arguments(null, writer =>
    {
        writer.WritePropertyName("options");
        writer.WriteStartObject();
        writer.WriteString("label", _label);
        if (url is not null) writer.WriteString("url", url);
        if (_title is not null) writer.WriteString("title", _title);
        WriteNumber(writer, "x", _x);
        WriteNumber(writer, "y", _y);
        WriteNumber(writer, "width", _width);
        WriteNumber(writer, "height", _height);
        WriteNumber(writer, "minWidth", _minWidth);
        WriteNumber(writer, "minHeight", _minHeight);
        WriteNumber(writer, "maxWidth", _maxWidth);
        WriteNumber(writer, "maxHeight", _maxHeight);
        WriteBoolean(writer, "center", _center);
        if (_preventOverflowMargin is { } margin)
        {
            writer.WriteStartObject("preventOverflow");
            writer.WriteNumber("width", margin.Width);
            writer.WriteNumber("height", margin.Height);
            writer.WriteEndObject();
        }
        else WriteBoolean(writer, "preventOverflow", _preventOverflow);
        WriteBoolean(writer, "visible", _visible);
        WriteBoolean(writer, "resizable", _resizable);
        WriteBoolean(writer, "maximizable", _maximizable);
        WriteBoolean(writer, "minimizable", _minimizable);
        WriteBoolean(writer, "closable", _closable);
        WriteBoolean(writer, "decorations", _decorations);
        WriteBoolean(writer, "fullscreen", _fullscreen);
        WriteBoolean(writer, "focus", _focus);
        WriteBoolean(writer, "focusable", _focusable);
        WriteBoolean(writer, "transparent", _transparent);
        WriteBoolean(writer, "maximized", _maximized);
        WriteBoolean(writer, "alwaysOnTop", _alwaysOnTop);
        WriteBoolean(writer, "alwaysOnBottom", _alwaysOnBottom);
        WriteBoolean(writer, "contentProtected", _contentProtected);
        WriteBoolean(writer, "skipTaskbar", _skipTaskbar);
        WriteBoolean(writer, "shadow", _shadow);
        WriteBoolean(writer, "visibleOnAllWorkspaces", _visibleOnAllWorkspaces);
        if (_theme is { } theme) writer.WriteString("theme", ApiJson.EnumName(theme));
        if (_titleBarStyle is { } titleBarStyle) writer.WriteString("titleBarStyle", ApiJson.EnumName(titleBarStyle));
        if (_windowEffects is not null) ApiJson.WriteEffects(writer, "windowEffects", _windowEffects);
        if (_backgroundColor is { } backgroundColor) ApiJson.WriteColor(writer, "backgroundColor", backgroundColor);
        if (_backgroundThrottling is { } backgroundThrottling) writer.WriteString("backgroundThrottling", ApiJson.EnumName(backgroundThrottling));
        WriteBoolean(writer, "javascriptDisabled", _javascriptDisabled);
        if (_scrollBarStyle is { } scrollBarStyle) writer.WriteString("scrollBarStyle", ApiJson.EnumName(scrollBarStyle));
        if (_parent is not null) writer.WriteString("parent", _parent);
        writer.WriteEndObject();
    });

    private void SetSize(double width, double height)
    {
        DpiValidation.Finite(width, nameof(width));
        DpiValidation.Finite(height, nameof(height));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
    }

    private void SetConstraintSize(double width, double height, bool minimum)
    {
        DpiValidation.Finite(width, nameof(width));
        DpiValidation.Finite(height, nameof(height));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (minimum) { _minWidth = width; _minHeight = height; }
        else { _maxWidth = width; _maxHeight = height; }
    }

    internal static void ValidateLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (!EventName.IsValid(label))
            throw new ArgumentException("Window label must include only alphanumeric characters, `-`, `/`, `:` and `_`.", nameof(label));
    }

    internal static void WriteNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number) writer.WriteNumber(name, number);
    }

    internal static void WriteBoolean(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } boolean) writer.WriteBoolean(name, boolean);
    }
}

public sealed class WebviewWindowBuilder
{
    private readonly AppHandle _app;
    private readonly string _label;
    private readonly WindowBuilder _window;
    private readonly WebviewOptionsState _webview = new();
    private string _url = "index.html";

    public WebviewWindowBuilder(AppHandle app, string label)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        WindowBuilder.ValidateLabel(label);
        _label = label;
        _window = new WindowBuilder(app, label);
    }

    public WebviewWindowBuilder Url(string url) { ArgumentException.ThrowIfNullOrWhiteSpace(url); _url = url; return this; }
    public WebviewWindowBuilder Title(string title) { _window.Title(title); return this; }
    public WebviewWindowBuilder Position(double x, double y) { _window.Position(x, y); return this; }
    public WebviewWindowBuilder InnerSize(double width, double height) { _window.InnerSize(width, height); return this; }
    public WebviewWindowBuilder MinInnerSize(double width, double height) { _window.MinInnerSize(width, height); return this; }
    public WebviewWindowBuilder MaxInnerSize(double width, double height) { _window.MaxInnerSize(width, height); return this; }
    public WebviewWindowBuilder Centered(bool value = true) { _window.Centered(value); return this; }
    public WebviewWindowBuilder PreventOverflow(bool value = true) { _window.PreventOverflow(value); return this; }
    public WebviewWindowBuilder PreventOverflow(uint marginWidth, uint marginHeight) { _window.PreventOverflow(marginWidth, marginHeight); return this; }
    public WebviewWindowBuilder Visible(bool value = true) { _window.Visible(value); return this; }
    public WebviewWindowBuilder Resizable(bool value = true) { _window.Resizable(value); return this; }
    public WebviewWindowBuilder Maximizable(bool value = true) { _window.Maximizable(value); return this; }
    public WebviewWindowBuilder Minimizable(bool value = true) { _window.Minimizable(value); return this; }
    public WebviewWindowBuilder Closable(bool value = true) { _window.Closable(value); return this; }
    public WebviewWindowBuilder Decorations(bool value = true) { _window.Decorations(value); return this; }
    public WebviewWindowBuilder Fullscreen(bool value = true) { _window.Fullscreen(value); return this; }
    public WebviewWindowBuilder Parent(Window? parent) { _window.Parent(parent); return this; }
    public WebviewWindowBuilder ParentLabel(string? label) { _window.ParentLabel(label); return this; }
    public WebviewWindowBuilder Transparent(bool value = true) { _window.Transparent(value); return this; }
    public WebviewWindowBuilder Focused(bool value = true) { _window.Focused(value); return this; }
    public WebviewWindowBuilder Focusable(bool value = true) { _window.Focusable(value); return this; }
    public WebviewWindowBuilder Maximized(bool value = true) { _window.Maximized(value); return this; }
    public WebviewWindowBuilder AlwaysOnTop(bool value = true) { _window.AlwaysOnTop(value); return this; }
    public WebviewWindowBuilder AlwaysOnBottom(bool value = true) { _window.AlwaysOnBottom(value); return this; }
    public WebviewWindowBuilder ContentProtected(bool value = true) { _window.ContentProtected(value); return this; }
    public WebviewWindowBuilder SkipTaskbar(bool value = true) { _window.SkipTaskbar(value); return this; }
    public WebviewWindowBuilder Shadow(bool value = true) { _window.Shadow(value); return this; }
    public WebviewWindowBuilder VisibleOnAllWorkspaces(bool value = true) { _window.VisibleOnAllWorkspaces(value); return this; }
    public WebviewWindowBuilder Theme(Theme value) { _window.Theme(value); return this; }
    public WebviewWindowBuilder TitleBarStyle(TitleBarStyle value) { _window.TitleBarStyle(value); return this; }
    public WebviewWindowBuilder WindowEffects(Effects value) { _window.WindowEffects(value); return this; }
    public WebviewWindowBuilder BackgroundColor(RgbaColor value) { _window.BackgroundColor(value); return this; }
    public WebviewWindowBuilder DragDropEnabled(bool value = true) { _webview.DragDropEnabled = value; return this; }
    public WebviewWindowBuilder UserAgent(string? value) { _webview.UserAgent = value; return this; }
    public WebviewWindowBuilder Incognito(bool value = true) { _webview.Incognito = value; return this; }
    public WebviewWindowBuilder ProxyUrl(string? value) { _webview.ProxyUrl = value; return this; }
    public WebviewWindowBuilder ZoomHotkeysEnabled(bool value = true) { _webview.ZoomHotkeysEnabled = value; return this; }
    public WebviewWindowBuilder UseHttpsScheme(bool value = true) { _webview.UseHttpsScheme = value; return this; }
    public WebviewWindowBuilder Devtools(bool value = true) { _webview.Devtools = value; return this; }
    public WebviewWindowBuilder BackgroundThrottling(BackgroundThrottlingPolicy value) { _webview.BackgroundThrottling = value; return this; }
    public WebviewWindowBuilder JavascriptDisabled(bool value = true) { _webview.JavascriptDisabled = value; return this; }
    public WebviewWindowBuilder DataDirectory(string? value) { _webview.DataDirectory = value; return this; }
    public WebviewWindowBuilder DataStoreIdentifier(DataStoreIdentifier? value) { _webview.DataStoreIdentifier = value?.ToArray(); return this; }
    public WebviewWindowBuilder ScrollBarStyle(ScrollBarStyle value) { _webview.ScrollBarStyle = value; return this; }
    public WebviewWindowBuilder GeneralAutofillEnabled(bool value = true) { _webview.GeneralAutofillEnabled = value; return this; }

    public async Task<WebviewWindow> BuildAsync(CancellationToken cancellationToken = default)
    {
        var baseArguments = _window.WriteArguments(_url);
        var arguments = MergeWebviewOptions(baseArguments);
        await _app.InvokeAsync("plugin:webview|create_webview_window", arguments, cancellationToken);
        return _app.RegisterWebviewWindow(_label);
    }

    private JsonElement MergeWebviewOptions(JsonElement source) => JsonValue.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WritePropertyName("options");
        writer.WriteStartObject();
        foreach (var property in source.GetProperty("options").EnumerateObject()) property.WriteTo(writer);
        _webview.Write(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });
}

public sealed class WebviewBuilder
{
    private readonly AppHandle _app;
    private readonly Window _window;
    private readonly string _label;
    private string _url = "index.html";
    private double _x;
    private double _y;
    private double _width = 400;
    private double _height = 300;
    private bool? _visible;
    private bool? _autoResize;
    private readonly WebviewOptionsState _options = new();

    public WebviewBuilder(AppHandle app, Window window, string label)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        WindowBuilder.ValidateLabel(label);
        _label = label;
    }

    public WebviewBuilder Url(string url) { ArgumentException.ThrowIfNullOrWhiteSpace(url); _url = url; return this; }
    public WebviewBuilder Bounds(double x, double y, double width, double height)
    {
        DpiValidation.Finite(x, nameof(x));
        DpiValidation.Finite(y, nameof(y));
        DpiValidation.Finite(width, nameof(width));
        DpiValidation.Finite(height, nameof(height));
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        if (_width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (_height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return this;
    }
    public WebviewBuilder Visible(bool value = true) { _visible = value; return this; }
    public WebviewBuilder AutoResize(bool value = true) { _autoResize = value; return this; }
    public WebviewBuilder Transparent(bool value = true) { _options.Transparent = value; return this; }
    public WebviewBuilder Focused(bool value = true) { _options.Focus = value; return this; }
    public WebviewBuilder DragDropEnabled(bool value = true) { _options.DragDropEnabled = value; return this; }
    public WebviewBuilder UserAgent(string? value) { _options.UserAgent = value; return this; }
    public WebviewBuilder Incognito(bool value = true) { _options.Incognito = value; return this; }
    public WebviewBuilder ProxyUrl(string? value) { _options.ProxyUrl = value; return this; }
    public WebviewBuilder ZoomHotkeysEnabled(bool value = true) { _options.ZoomHotkeysEnabled = value; return this; }
    public WebviewBuilder UseHttpsScheme(bool value = true) { _options.UseHttpsScheme = value; return this; }
    public WebviewBuilder Devtools(bool value = true) { _options.Devtools = value; return this; }
    public WebviewBuilder BackgroundColor(RgbaColor value) { _options.BackgroundColor = value; return this; }
    public WebviewBuilder BackgroundThrottling(BackgroundThrottlingPolicy value) { _options.BackgroundThrottling = value; return this; }
    public WebviewBuilder JavascriptDisabled(bool value = true) { _options.JavascriptDisabled = value; return this; }
    public WebviewBuilder DataDirectory(string? value) { _options.DataDirectory = value; return this; }
    public WebviewBuilder DataStoreIdentifier(DataStoreIdentifier? value) { _options.DataStoreIdentifier = value?.ToArray(); return this; }
    public WebviewBuilder ScrollBarStyle(ScrollBarStyle value) { _options.ScrollBarStyle = value; return this; }
    public WebviewBuilder GeneralAutofillEnabled(bool value = true) { _options.GeneralAutofillEnabled = value; return this; }

    public async Task<Webview> BuildAsync(CancellationToken cancellationToken = default)
    {
        var arguments = ApiJson.Arguments(null, writer =>
        {
            writer.WriteString("windowLabel", _window.Label);
            writer.WritePropertyName("options");
            writer.WriteStartObject();
            writer.WriteString("label", _label);
            writer.WriteString("url", _url);
            writer.WriteNumber("x", _x);
            writer.WriteNumber("y", _y);
            writer.WriteNumber("width", _width);
            writer.WriteNumber("height", _height);
            WindowBuilder.WriteBoolean(writer, "visible", _visible);
            WindowBuilder.WriteBoolean(writer, "autoResize", _autoResize);
            _options.Write(writer);
            writer.WriteEndObject();
        });
        await _app.InvokeAsync("plugin:webview|create_webview", arguments, cancellationToken);
        return _app.RegisterWebview(_label, _window.Label);
    }
}

internal sealed class WebviewOptionsState
{
    public bool? Transparent { get; set; }
    public bool? Focus { get; set; }
    public bool? DragDropEnabled { get; set; }
    public string? UserAgent { get; set; }
    public bool? Incognito { get; set; }
    public string? ProxyUrl { get; set; }
    public bool? ZoomHotkeysEnabled { get; set; }
    public bool? UseHttpsScheme { get; set; }
    public bool? Devtools { get; set; }
    public RgbaColor? BackgroundColor { get; set; }
    public BackgroundThrottlingPolicy? BackgroundThrottling { get; set; }
    public bool? JavascriptDisabled { get; set; }
    public string? DataDirectory { get; set; }
    public byte[]? DataStoreIdentifier { get; set; }
    public ScrollBarStyle? ScrollBarStyle { get; set; }
    public bool? GeneralAutofillEnabled { get; set; }

    public void Write(Utf8JsonWriter writer)
    {
        WindowBuilder.WriteBoolean(writer, "transparent", Transparent);
        WindowBuilder.WriteBoolean(writer, "focus", Focus);
        WindowBuilder.WriteBoolean(writer, "dragDropEnabled", DragDropEnabled);
        if (UserAgent is not null) writer.WriteString("userAgent", UserAgent);
        WindowBuilder.WriteBoolean(writer, "incognito", Incognito);
        if (ProxyUrl is not null) writer.WriteString("proxyUrl", ProxyUrl);
        WindowBuilder.WriteBoolean(writer, "zoomHotkeysEnabled", ZoomHotkeysEnabled);
        WindowBuilder.WriteBoolean(writer, "useHttpsScheme", UseHttpsScheme);
        WindowBuilder.WriteBoolean(writer, "devtools", Devtools);
        if (BackgroundColor is { } color) ApiJson.WriteColor(writer, "backgroundColor", color);
        if (BackgroundThrottling is { } throttling) writer.WriteString("backgroundThrottling", ApiJson.EnumName(throttling));
        WindowBuilder.WriteBoolean(writer, "javascriptDisabled", JavascriptDisabled);
        if (DataDirectory is not null) writer.WriteString("dataDirectory", DataDirectory);
        if (DataStoreIdentifier is { } identifier)
        {
            writer.WriteStartArray("dataStoreIdentifier");
            foreach (var value in identifier) writer.WriteNumberValue(value);
            writer.WriteEndArray();
        }
        if (ScrollBarStyle is { } scrollBarStyle) writer.WriteString("scrollBarStyle", ApiJson.EnumName(scrollBarStyle));
        WindowBuilder.WriteBoolean(writer, "generalAutofillEnabled", GeneralAutofillEnabled);
    }
}
