using System.Text.Json;

namespace NetWiv;

public partial class Window
{
    public IDisposable Listen(string eventName, Action<AppEvent> handler)
    {
        return App.Events.Listen(eventName, EventTarget, handler);
    }

    private Runtime.RuntimeEventTarget EventTarget => new(this is WebviewWindow ? "WebviewWindow" : "Window", Label);

    public IDisposable Once(string eventName, Action<AppEvent> handler) => App.Events.Once(eventName, EventTarget, handler);

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

    public async Task<double> ScaleFactorAsync(CancellationToken cancellationToken = default) =>
        (await InvokeWindowApiAsync("scale_factor", cancellationToken: cancellationToken)).GetDouble();

    public async Task<PhysicalPosition> InnerPositionAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalPosition(await InvokeWindowApiAsync("inner_position", cancellationToken: cancellationToken));

    public async Task<PhysicalPosition> OuterPositionAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalPosition(await InvokeWindowApiAsync("outer_position", cancellationToken: cancellationToken));

    public async Task<PhysicalSize> InnerSizeAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalSize(await InvokeWindowApiAsync("inner_size", cancellationToken: cancellationToken));

    public async Task<PhysicalSize> OuterSizeAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalSize(await InvokeWindowApiAsync("outer_size", cancellationToken: cancellationToken));

    public Task<bool> IsFullscreenAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_fullscreen", cancellationToken);

    public Task<bool> IsMinimizedAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_minimized", cancellationToken);

    public Task<bool> IsMaximizedAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_maximized", cancellationToken);

    public Task<bool> IsFocusedAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_focused", cancellationToken);

    public Task<bool> IsDecoratedAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_decorated", cancellationToken);

    public Task<bool> IsResizableAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_resizable", cancellationToken);

    public Task<bool> IsMaximizableAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_maximizable", cancellationToken);

    public Task<bool> IsMinimizableAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_minimizable", cancellationToken);

    public Task<bool> IsClosableAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_closable", cancellationToken);

    public Task<bool> IsVisibleAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_visible", cancellationToken);

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_enabled", cancellationToken);

    public Task<bool> IsAlwaysOnTopAsync(CancellationToken cancellationToken = default) =>
        ReadBooleanAsync("is_always_on_top", cancellationToken);

    public async Task<string> TitleAsync(CancellationToken cancellationToken = default) =>
        (await InvokeWindowApiAsync("title", cancellationToken: cancellationToken)).GetString()
            ?? throw new InvokeException("The window title response was not a string.");

    public async Task<Theme?> ThemeAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeWindowApiAsync("theme", cancellationToken: cancellationToken);
        return result.ValueKind == JsonValueKind.Null ? null : ApiJson.ReadTheme(result);
    }

    public Task CenterAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("center", cancellationToken: cancellationToken);

    public Task RequestUserAttentionAsync(
        UserAttentionType? requestType,
        CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("request_user_attention", writer =>
        {
            writer.WritePropertyName("value");
            if (requestType is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", requestType.Value.ToString());
            writer.WriteEndObject();
        }, cancellationToken);

    public Task SetResizableAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_resizable", value, cancellationToken);

    public Task SetEnabledAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_enabled", value, cancellationToken);

    public Task SetMaximizableAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_maximizable", value, cancellationToken);

    public Task SetMinimizableAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_minimizable", value, cancellationToken);

    public Task SetClosableAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_closable", value, cancellationToken);

    public Task SetTitleAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        return InvokeWindowApiAsync("set_title", writer => writer.WriteString("value", title), cancellationToken);
    }

    public Task MaximizeAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("maximize", cancellationToken: cancellationToken);

    public Task UnmaximizeAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("unmaximize", cancellationToken: cancellationToken);

    public Task ToggleMaximizeAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("toggle_maximize", cancellationToken: cancellationToken);

    public Task MinimizeAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("minimize", cancellationToken: cancellationToken);

    public Task UnminimizeAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("unminimize", cancellationToken: cancellationToken);

    public Task DestroyAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("destroy", cancellationToken: cancellationToken);

    public Task SetDecorationsAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_decorations", value, cancellationToken);

    public Task SetShadowAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_shadow", value, cancellationToken);

    public Task SetAlwaysOnTopAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_always_on_top", value, cancellationToken);

    public Task SetAlwaysOnBottomAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_always_on_bottom", value, cancellationToken);

    public Task SetContentProtectedAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_content_protected", value, cancellationToken);

    public Task SetSizeAsync(IDpiSize size, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_size", writer => ApiJson.WriteDpiSize(writer, "value", size), cancellationToken);

    public Task SetMinSizeAsync(IDpiSize? size, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_min_size", writer => ApiJson.WriteNullableDpiSize(writer, "value", size), cancellationToken);

    public Task SetMaxSizeAsync(IDpiSize? size, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_max_size", writer => ApiJson.WriteNullableDpiSize(writer, "value", size), cancellationToken);

    public Task SetSizeConstraintsAsync(
        WindowSizeConstraints? constraints,
        CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_size_constraints", writer =>
            ApiJson.WriteConstraints(writer, "value", constraints), cancellationToken);

    public Task SetPositionAsync(IDpiPosition position, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_position", writer => ApiJson.WriteDpiPosition(writer, "value", position), cancellationToken);

    public Task SetFullscreenAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_fullscreen", value, cancellationToken);

    public Task SetSimpleFullscreenAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_simple_fullscreen", value, cancellationToken);

    public Task SetFocusAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_focus", cancellationToken: cancellationToken);

    public Task SetFocusableAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_focusable", value, cancellationToken);

    public Task SetSkipTaskbarAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_skip_taskbar", value, cancellationToken);

    public Task SetCursorGrabAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_cursor_grab", value, cancellationToken);

    public Task SetCursorVisibleAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_cursor_visible", value, cancellationToken);

    public Task SetCursorIconAsync(CursorIcon icon, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_cursor_icon", writer =>
            writer.WriteString("value", ApiJson.CursorIconName(icon)), cancellationToken);

    public virtual Task SetBackgroundColorAsync(RgbaColor? color, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_background_color", writer =>
            ApiJson.WriteNullableColor(writer, "value", color), cancellationToken);

    public Task SetEffectsAsync(Effects? effects, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_effects", writer => ApiJson.WriteEffects(writer, "value", effects), cancellationToken);

    public Task ClearEffectsAsync(CancellationToken cancellationToken = default) => SetEffectsAsync(null, cancellationToken);

    public Task SetIconAsync(Image icon, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return InvokeWindowApiAsync("set_icon", writer => Image.WriteReference(writer, "value", icon), cancellationToken);
    }

    public Task SetIconAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return InvokeWindowApiAsync("set_icon", writer => writer.WriteString("value", path), cancellationToken);
    }

    public Task SetIconAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_icon", writer => Image.WriteBytes(writer, "value", bytes.Span), cancellationToken);

    public Task SetOverlayIconAsync(Image? icon = null, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_overlay_icon", writer => Image.WriteReference(writer, "value", icon), cancellationToken);

    public Task SetOverlayIconAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return InvokeWindowApiAsync("set_overlay_icon", writer => writer.WriteString("value", path), cancellationToken);
    }

    public Task SetOverlayIconAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_overlay_icon", writer => Image.WriteBytes(writer, "value", bytes.Span), cancellationToken);

    public async Task<Monitor?> CurrentMonitorAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadMonitor(await InvokeWindowApiAsync("current_monitor", cancellationToken: cancellationToken, includeLabel: false));

    public async Task<Monitor?> PrimaryMonitorAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadMonitor(await InvokeWindowApiAsync("primary_monitor", cancellationToken: cancellationToken, includeLabel: false));

    public async Task<Monitor?> MonitorFromPointAsync(double x, double y, CancellationToken cancellationToken = default) =>
        ApiJson.ReadMonitor(await InvokeWindowApiAsync("monitor_from_point", writer =>
        {
            writer.WriteNumber("x", x);
            writer.WriteNumber("y", y);
        }, cancellationToken, includeLabel: false));

    public async Task<IReadOnlyList<Monitor>> AvailableMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeWindowApiAsync("available_monitors", cancellationToken: cancellationToken, includeLabel: false);
        return result.EnumerateArray().Select(value => ApiJson.ReadMonitor(value)
            ?? throw new InvokeException("The monitor list contained null.")).ToArray();
    }

    public async Task<PhysicalPosition<double>> CursorPositionAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeWindowApiAsync("cursor_position", cancellationToken: cancellationToken, includeLabel: false);
        return new(result.GetProperty("x").GetDouble(), result.GetProperty("y").GetDouble());
    }

    public Task SetTitleBarStyleAsync(TitleBarStyle style, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_title_bar_style", writer => writer.WriteString("value", ApiJson.EnumName(style)), cancellationToken);

    public Task SetCursorPositionAsync(IDpiPosition position, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_cursor_position", writer =>
            ApiJson.WriteDpiPosition(writer, "value", position), cancellationToken);

    public Task SetIgnoreCursorEventsAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_ignore_cursor_events", value, cancellationToken);

    public Task StartDraggingAsync(CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("start_dragging", cancellationToken: cancellationToken);

    public Task StartResizeDraggingAsync(
        ResizeDirection direction,
        CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("start_resize_dragging", writer =>
            writer.WriteString("value", direction.ToString()), cancellationToken);

    public Task SetBadgeCountAsync(long? count, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_badge_count", writer =>
        {
            if (count is { } number) writer.WriteNumber("value", number);
            else writer.WriteNull("value");
        }, cancellationToken);

    public Task SetBadgeLabelAsync(string? value, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_badge_label", writer => writer.WriteString("value", value), cancellationToken);

    public Task SetProgressBarAsync(ProgressBarState state, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_progress_bar", writer =>
            ApiJson.WriteProgress(writer, "value", state), cancellationToken);

    public Task SetVisibleOnAllWorkspacesAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeBooleanValueAsync("set_visible_on_all_workspaces", value, cancellationToken);

    public Task SetThemeAsync(Theme? theme, CancellationToken cancellationToken = default) =>
        InvokeWindowApiAsync("set_theme", writer =>
            writer.WriteString("value", theme is null ? null : ApiJson.ThemeName(theme.Value)), cancellationToken);

    private async Task<bool> ReadBooleanAsync(string command, CancellationToken cancellationToken) =>
        (await InvokeWindowApiAsync(command, cancellationToken: cancellationToken)).GetBoolean();

    private Task<JsonElement> InvokeBooleanValueAsync(
        string command,
        bool value,
        CancellationToken cancellationToken) =>
        InvokeWindowApiAsync(command, writer => writer.WriteBoolean("value", value), cancellationToken);

    private Task<JsonElement> InvokeWindowApiAsync(
        string command,
        Action<Utf8JsonWriter>? write = null,
        CancellationToken cancellationToken = default,
        bool includeLabel = true) =>
        App.InvokeForTargetAsync(
            $"plugin:window|{command}",
            ApiJson.Arguments(includeLabel ? Label : null, write),
            Label,
            cancellationToken);
}
