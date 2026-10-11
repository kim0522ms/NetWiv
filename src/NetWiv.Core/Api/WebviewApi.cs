using System.Text.Json;

namespace NetWiv;

public partial class Webview
{
    public IDisposable Listen(string eventName, Action<AppEvent> handler)
    {
        return App.Events.Listen(eventName, new("Webview", Label), handler);
    }

    public IDisposable Once(string eventName, Action<AppEvent> handler) => App.Events.Once(eventName, new("Webview", Label), handler);

    public Task EmitAsync(string eventName, JsonElement payload)
    {
        App.Emit(eventName, payload);
        return Task.CompletedTask;
    }

    public Task EmitToAsync(Runtime.RuntimeEventTarget target, string eventName, JsonElement payload)
    {
        App.Emit(eventName, payload, target);
        return Task.CompletedTask;
    }

    public async Task<PhysicalPosition> PositionAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalPosition(await InvokeWebviewApiAsync(
            "webview_position", cancellationToken: cancellationToken));

    public async Task<PhysicalSize> SizeAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalSize(await InvokeWebviewApiAsync(
            "webview_size", cancellationToken: cancellationToken));

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("webview_close", cancellationToken: cancellationToken);

    public Task SetSizeAsync(IDpiSize size, CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("set_webview_size", writer =>
            ApiJson.WriteDpiSize(writer, "value", size), cancellationToken);

    public Task SetPositionAsync(IDpiPosition position, CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("set_webview_position", writer =>
            ApiJson.WriteDpiPosition(writer, "value", position), cancellationToken);

    public Task SetFocusAsync(CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("set_webview_focus", cancellationToken: cancellationToken);

    public Task SetAutoResizeAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("set_webview_auto_resize", writer =>
            writer.WriteBoolean("value", value), cancellationToken);

    public Task HideAsync(CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("webview_hide", cancellationToken: cancellationToken);

    public Task ShowAsync(CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync("webview_show", cancellationToken: cancellationToken);

    public Task SetZoomAsync(double scaleFactor, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Zoom scale must be positive and finite.");
        }

        return InvokeWebviewApiAsync("set_webview_zoom", writer =>
            writer.WriteNumber("value", scaleFactor), cancellationToken);
    }

    public Task ReparentAsync(string windowLabel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windowLabel);
        return InvokeWebviewApiAsync("reparent", writer =>
            writer.WriteString("window", windowLabel), cancellationToken);
    }

    public Task ClearAllBrowsingDataAsync(CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync(
            "clear_all_browsing_data",
            cancellationToken: cancellationToken,
            includeLabel: false);

    public Task SetBackgroundColorAsync(
        RgbaColor? color,
        CancellationToken cancellationToken = default) =>
        InvokeWebviewApiAsync(
            "set_webview_background_color",
            writer => ApiJson.WriteNullableColor(writer, "value", color),
            cancellationToken);

    private Task<JsonElement> InvokeWebviewApiAsync(
        string command,
        Action<Utf8JsonWriter>? write = null,
        CancellationToken cancellationToken = default,
        bool includeLabel = true) =>
        App.InvokeForTargetAsync(
            $"plugin:webview|{command}",
            ApiJson.Arguments(includeLabel ? Label : null, write),
            Label,
            cancellationToken);
}
