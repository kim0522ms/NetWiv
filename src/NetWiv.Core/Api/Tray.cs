using System.Text.Json;

namespace NetWiv;

public enum MouseButtonState { Up, Down }
public enum MouseButton { Left, Right, Middle }
public enum TrayIconEventType { Click, DoubleClick, Enter, Move, Leave }

public sealed record TrayIconRect(PhysicalPosition<double> Position, PhysicalSize Size);

public sealed record TrayIconEvent(
    TrayIconEventType Type,
    string Id,
    PhysicalPosition<double> Position,
    TrayIconRect Rect,
    MouseButton? Button = null,
    MouseButtonState? ButtonState = null);

public sealed record TrayIconOptions(
    string? Id = null,
    MenuItemBase? Menu = null,
    Image? Icon = null,
    string? Tooltip = null,
    string? Title = null,
    string? TempDirectoryPath = null,
    bool? IconAsTemplate = null,
    bool? ShowMenuOnLeftClick = null,
    bool? Visible = null,
    string? LinuxIconName = null);

public sealed class TrayIcon : Resource
{
    internal TrayIcon(AppHandle app, uint resourceId, string id) : base(app, resourceId) => Id = id;

    public string Id { get; }

    public static async Task<TrayIcon?> GetByIdAsync(
        AppHandle app,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var result = await app.InvokeAsync("plugin:tray|get_by_id",
            ApiJson.Arguments(null, writer => writer.WriteString("id", id)), cancellationToken);
        return result.ValueKind == JsonValueKind.Null ? null : new TrayIcon(app, result.GetUInt32(), id);
    }

    public static async Task RemoveByIdAsync(
        AppHandle app,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await app.InvokeAsync("plugin:tray|remove_by_id",
            ApiJson.Arguments(null, writer => writer.WriteString("id", id)), cancellationToken);
    }

    public static async Task<TrayIcon> NewAsync(
        AppHandle app,
        TrayIconOptions? options = null,
        Channel<TrayIconEvent>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var result = await app.InvokeAsync("plugin:tray|new", ApiJson.Arguments(null, writer =>
        {
            writer.WritePropertyName("options");
            writer.WriteStartObject();
            WriteOptions(writer, options);
            writer.WriteEndObject();
            MenuJson.WriteChannel(writer, "handler", handler);
        }), cancellationToken);
        var pair = MenuJson.ReadResourcePair(result);
        return new TrayIcon(app, pair.ResourceId, pair.Id);
    }

    public Task SetIconAsync(Image? icon, CancellationToken cancellationToken = default) =>
        InvokeAsync("set_icon", writer => Image.WriteReference(writer, "icon", icon), cancellationToken);

    public Task SetMenuAsync(MenuItemBase? menu, CancellationToken cancellationToken = default) =>
        InvokeAsync("set_menu", writer =>
        {
            writer.WritePropertyName("menu");
            if (menu is null) writer.WriteNullValue();
            else menu.WriteReference(writer);
        }, cancellationToken);

    public Task SetTooltipAsync(string? tooltip, CancellationToken cancellationToken = default) =>
        InvokeNullableStringAsync("set_tooltip", "tooltip", tooltip, cancellationToken);

    public Task SetTitleAsync(string? title, CancellationToken cancellationToken = default) =>
        InvokeNullableStringAsync("set_title", "title", title, cancellationToken);

    public Task SetVisibleAsync(bool visible, CancellationToken cancellationToken = default) =>
        InvokeAsync("set_visible", writer => writer.WriteBoolean("visible", visible), cancellationToken);

    public Task SetTempDirectoryPathAsync(string? path, CancellationToken cancellationToken = default) =>
        InvokeNullableStringAsync("set_temp_dir_path", "path", path, cancellationToken);

    public Task SetIconAsTemplateAsync(bool asTemplate, CancellationToken cancellationToken = default) =>
        InvokeAsync("set_icon_as_template", writer => writer.WriteBoolean("asTemplate", asTemplate), cancellationToken);

    public Task SetIconWithTemplateAsync(
        Image? icon,
        bool asTemplate,
        CancellationToken cancellationToken = default) =>
        InvokeAsync("set_icon_with_as_template", writer =>
        {
            Image.WriteReference(writer, "icon", icon);
            writer.WriteBoolean("asTemplate", asTemplate);
        }, cancellationToken);

    public Task SetShowMenuOnLeftClickAsync(bool onLeft, CancellationToken cancellationToken = default) =>
        InvokeAsync("set_show_menu_on_left_click", writer => writer.WriteBoolean("onLeft", onLeft), cancellationToken);

    private Task InvokeNullableStringAsync(
        string command,
        string name,
        string? value,
        CancellationToken cancellationToken) =>
        InvokeAsync(command, writer =>
        {
            if (value is null) writer.WriteNull(name);
            else writer.WriteString(name, value);
        }, cancellationToken);

    private Task<JsonElement> InvokeAsync(
        string command,
        Action<Utf8JsonWriter> write,
        CancellationToken cancellationToken) =>
        App.InvokeAsync($"plugin:tray|{command}", ApiJson.Arguments(null, writer =>
        {
            writer.WriteNumber("rid", ResourceId);
            write(writer);
        }), cancellationToken).AsTask();

    private static void WriteOptions(Utf8JsonWriter writer, TrayIconOptions? options)
    {
        if (options is null) return;
        if (options.Id is not null) writer.WriteString("id", options.Id);
        if (options.Menu is not null)
        {
            writer.WritePropertyName("menu");
            options.Menu.WriteReference(writer);
        }
        if (options.Icon is not null) writer.WriteNumber("icon", options.Icon.ResourceId);
        if (options.Tooltip is not null) writer.WriteString("tooltip", options.Tooltip);
        if (options.Title is not null) writer.WriteString("title", options.Title);
        if (options.TempDirectoryPath is not null) writer.WriteString("tempDirPath", options.TempDirectoryPath);
        if (options.IconAsTemplate is { } template) writer.WriteBoolean("iconAsTemplate", template);
        if (options.ShowMenuOnLeftClick is { } left) writer.WriteBoolean("showMenuOnLeftClick", left);
        if (options.Visible is { } visible) writer.WriteBoolean("visible", visible);
        if (options.LinuxIconName is not null) writer.WriteString("iconName", options.LinuxIconName);
    }
}
