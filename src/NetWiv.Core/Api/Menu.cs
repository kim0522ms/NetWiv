using System.Text.Json;

namespace NetWiv;

public sealed record MenuOptions(string? Id = null, IReadOnlyList<MenuItemBase>? Items = null);

public record MenuItemOptions(
    string Text,
    string? Id = null,
    bool? Enabled = null,
    string? Accelerator = null);

public sealed record CheckMenuItemOptions(
    string Text,
    string? Id = null,
    bool? Enabled = null,
    string? Accelerator = null,
    bool? Checked = null) : MenuItemOptions(Text, Id, Enabled, Accelerator);

public enum NativeIcon
{
    Add,
    Advanced,
    Bluetooth,
    Bookmarks,
    Caution,
    ColorPanel,
    ColumnView,
    Computer,
    EnterFullScreen,
    Everyone,
    ExitFullScreen,
    FlowView,
    Folder,
    FolderBurnable,
    FolderSmart,
    FollowLinkFreestanding,
    FontPanel,
    GoLeft,
    GoRight,
    Home,
    IChatTheater,
    IconView,
    Info,
    InvalidDataFreestanding,
    LeftFacingTriangle,
    ListView,
    LockLocked,
    LockUnlocked,
    MenuMixedState,
    MenuOnState,
    MobileMe,
    MultipleDocuments,
    Network,
    Path,
    PreferencesGeneral,
    QuickLook,
    RefreshFreestanding,
    Refresh,
    Remove,
    RevealFreestanding,
    RightFacingTriangle,
    Share,
    Slideshow,
    SmartBadge,
    StatusAvailable,
    StatusNone,
    StatusPartiallyAvailable,
    StatusUnavailable,
    StopProgressFreestanding,
    StopProgress,
    TrashEmpty,
    TrashFull,
    User,
    UserAccounts,
    UserGroup,
    UserGuest,
}

public sealed class MenuIcon
{
    private MenuIcon(Image? image, NativeIcon? nativeIcon)
    {
        Image = image;
        NativeIcon = nativeIcon;
    }

    internal Image? Image { get; }
    internal NativeIcon? NativeIcon { get; }

    public static MenuIcon FromImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new MenuIcon(image, null);
    }

    public static MenuIcon FromNativeIcon(NativeIcon icon) => new(null, icon);

    public static implicit operator MenuIcon(Image image) => FromImage(image);
    public static implicit operator MenuIcon(NativeIcon icon) => FromNativeIcon(icon);
}

public sealed record IconMenuItemOptions(
    string Text,
    string? Id = null,
    bool? Enabled = null,
    string? Accelerator = null,
    MenuIcon? Icon = null) : MenuItemOptions(Text, Id, Enabled, Accelerator);

public sealed record SubmenuOptions(
    string Text,
    string? Id = null,
    bool? Enabled = null,
    IReadOnlyList<MenuItemBase>? Items = null,
    MenuIcon? Icon = null);

public sealed record AboutMetadata(
    string? Name = null,
    string? Version = null,
    string? ShortVersion = null,
    IReadOnlyList<string>? Authors = null,
    string? Comments = null,
    string? Copyright = null,
    string? License = null,
    string? Website = null,
    string? WebsiteLabel = null,
    string? Credits = null,
    Image? Icon = null);

public enum PredefinedMenuItemType
{
    Separator,
    Copy,
    Cut,
    Paste,
    SelectAll,
    Undo,
    Redo,
    Minimize,
    Maximize,
    Fullscreen,
    Hide,
    HideOthers,
    ShowAll,
    CloseWindow,
    Quit,
    About,
    Services,
    BringAllToFront,
}

public sealed record PredefinedMenuItemOptions(
    PredefinedMenuItemType Item,
    string? Text = null,
    AboutMetadata? About = null);

public abstract class MenuItemBase : Resource
{
    protected MenuItemBase(AppHandle app, uint resourceId, string id, string kind)
        : base(app, resourceId)
    {
        Id = id;
        Kind = kind;
    }

    public string Id { get; }
    public string Kind { get; }

    internal void WriteReference(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(ResourceId);
        writer.WriteStringValue(Kind);
        writer.WriteEndArray();
    }

    protected internal Task<JsonElement> InvokeMenuAsync(
        string command,
        Action<Utf8JsonWriter>? write = null,
        CancellationToken cancellationToken = default) =>
        App.InvokeAsync($"plugin:menu|{command}", ApiJson.Arguments(null, writer =>
        {
            writer.WriteNumber("rid", ResourceId);
            writer.WriteString("kind", Kind);
            write?.Invoke(writer);
        }), cancellationToken).AsTask();

    internal static async Task<(uint ResourceId, string Id)> NewAsync(
        AppHandle app,
        string kind,
        Action<Utf8JsonWriter>? writeOptions,
        Channel<string>? handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        var result = await app.InvokeAsync("plugin:menu|new", ApiJson.Arguments(null, writer =>
        {
            writer.WriteString("kind", kind);
            writer.WritePropertyName("options");
            writer.WriteStartObject();
            writeOptions?.Invoke(writer);
            writer.WriteEndObject();
            MenuJson.WriteChannel(writer, "handler", handler);
        }), cancellationToken);
        return MenuJson.ReadResourcePair(result);
    }
}

public sealed class Menu : MenuItemBase
{
    internal Menu(AppHandle app, uint resourceId, string id) : base(app, resourceId, id, "Menu") { }

    public static async Task<Menu> NewAsync(
        AppHandle app,
        MenuOptions? options = null,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        var pair = await NewAsync(app, "Menu", writer => MenuJson.WriteMenuOptions(writer, options), handler, cancellationToken);
        return new Menu(app, pair.ResourceId, pair.Id);
    }

    public static async Task<Menu> DefaultAsync(AppHandle app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var pair = MenuJson.ReadResourcePair(await app.InvokeAsync(
            "plugin:menu|create_default", JsonValue.EmptyObject, cancellationToken));
        return new Menu(app, pair.ResourceId, pair.Id);
    }

    public Task AppendAsync(MenuItemBase item, CancellationToken cancellationToken = default) =>
        AppendAsync([item], cancellationToken);

    public Task AppendAsync(IReadOnlyList<MenuItemBase> items, CancellationToken cancellationToken = default) =>
        ContainerCommandAsync("append", items, null, cancellationToken);

    public Task PrependAsync(IReadOnlyList<MenuItemBase> items, CancellationToken cancellationToken = default) =>
        ContainerCommandAsync("prepend", items, null, cancellationToken);

    public Task InsertAsync(IReadOnlyList<MenuItemBase> items, uint position, CancellationToken cancellationToken = default) =>
        ContainerCommandAsync("insert", items, position, cancellationToken);

    public Task RemoveAsync(MenuItemBase item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return InvokeMenuAsync("remove", writer =>
        {
            writer.WritePropertyName("item");
            item.WriteReference(writer);
        }, cancellationToken);
    }

    public async Task<MenuItemBase?> RemoveAtAsync(uint position, CancellationToken cancellationToken = default) =>
        MenuJson.ReadMenuItem(App, await InvokeMenuAsync("remove_at", writer =>
            writer.WriteNumber("position", position), cancellationToken));

    public async Task<IReadOnlyList<MenuItemBase>> ItemsAsync(CancellationToken cancellationToken = default) =>
        MenuJson.ReadMenuItems(App, await InvokeMenuAsync("items", cancellationToken: cancellationToken));

    public async Task<MenuItemBase?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return MenuJson.ReadMenuItem(App, await InvokeMenuAsync("get", writer => writer.WriteString("id", id), cancellationToken));
    }

    public Task PopupAsync(
        IDpiPosition? position = null,
        Window? window = null,
        CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("popup", writer => MenuJson.WritePopup(writer, position, window), cancellationToken);

    public async Task<Menu?> SetAsAppMenuAsync(CancellationToken cancellationToken = default) =>
        MenuJson.ReadMenu(App, await App.InvokeAsync("plugin:menu|set_as_app_menu",
            ApiJson.Arguments(null, writer => writer.WriteNumber("rid", ResourceId)), cancellationToken));

    public async Task<Menu?> SetAsWindowMenuAsync(Window? window = null, CancellationToken cancellationToken = default) =>
        MenuJson.ReadMenu(App, await App.InvokeAsync("plugin:menu|set_as_window_menu", ApiJson.Arguments(null, writer =>
        {
            writer.WriteNumber("rid", ResourceId);
            if (window is null) writer.WriteNull("window");
            else writer.WriteString("window", window.Label);
        }), cancellationToken));

    private Task ContainerCommandAsync(
        string command,
        IReadOnlyList<MenuItemBase> items,
        uint? position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new ArgumentException("At least one menu item is required.", nameof(items));
        return InvokeMenuAsync(command, writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                ArgumentNullException.ThrowIfNull(item);
                item.WriteReference(writer);
            }
            writer.WriteEndArray();
            if (position is { } value) writer.WriteNumber("position", value);
        }, cancellationToken);
    }
}

public class MenuItem : MenuItemBase
{
    internal MenuItem(AppHandle app, uint resourceId, string id, string kind = "MenuItem")
        : base(app, resourceId, id, kind) { }

    public static async Task<MenuItem> NewAsync(
        AppHandle app,
        MenuItemOptions options,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pair = await NewAsync(app, "MenuItem", writer => MenuJson.WriteItemOptions(writer, options), handler, cancellationToken);
        return new MenuItem(app, pair.ResourceId, pair.Id);
    }

    public async Task<string> TextAsync(CancellationToken cancellationToken = default) =>
        (await InvokeMenuAsync("text", cancellationToken: cancellationToken)).GetString()
        ?? throw new InvokeException("The menu text response was invalid.");

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_text", writer => writer.WriteString("text", text), cancellationToken);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        (await InvokeMenuAsync("is_enabled", cancellationToken: cancellationToken)).GetBoolean();

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_enabled", writer => writer.WriteBoolean("enabled", enabled), cancellationToken);

    public Task SetAcceleratorAsync(string? accelerator, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_accelerator", writer =>
        {
            if (accelerator is null) writer.WriteNull("accelerator");
            else writer.WriteString("accelerator", accelerator);
        }, cancellationToken);
}

public sealed class CheckMenuItem : MenuItem
{
    internal CheckMenuItem(AppHandle app, uint resourceId, string id) : base(app, resourceId, id, "Check") { }

    public static async Task<CheckMenuItem> NewAsync(
        AppHandle app,
        CheckMenuItemOptions options,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pair = await MenuItemBase.NewAsync(app, "Check", writer =>
        {
            MenuJson.WriteItemOptions(writer, options);
            if (options.Checked is { } value) writer.WriteBoolean("checked", value);
        }, handler, cancellationToken);
        return new CheckMenuItem(app, pair.ResourceId, pair.Id);
    }

    public async Task<bool> IsCheckedAsync(CancellationToken cancellationToken = default) =>
        (await InvokeMenuAsync("is_checked", cancellationToken: cancellationToken)).GetBoolean();

    public Task SetCheckedAsync(bool value, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_checked", writer => writer.WriteBoolean("checked", value), cancellationToken);
}

public sealed class IconMenuItem : MenuItem
{
    internal IconMenuItem(AppHandle app, uint resourceId, string id) : base(app, resourceId, id, "Icon") { }

    public static async Task<IconMenuItem> NewAsync(
        AppHandle app,
        IconMenuItemOptions options,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pair = await MenuItemBase.NewAsync(app, "Icon", writer =>
        {
            MenuJson.WriteItemOptions(writer, options);
            if (options.Icon is not null) MenuJson.WriteIcon(writer, "icon", options.Icon);
        }, handler, cancellationToken);
        return new IconMenuItem(app, pair.ResourceId, pair.Id);
    }

    public Task SetIconAsync(MenuIcon? icon, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_icon", writer => MenuJson.WriteIcon(writer, "icon", icon), cancellationToken);
}

public sealed class Submenu : MenuItem
{
    internal Submenu(AppHandle app, uint resourceId, string id) : base(app, resourceId, id, "Submenu") { }

    public static async Task<Submenu> NewAsync(
        AppHandle app,
        SubmenuOptions options,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pair = await MenuItemBase.NewAsync(app, "Submenu", writer =>
        {
            MenuJson.WriteBaseOptions(writer, options.Id, options.Text, options.Enabled);
            MenuJson.WriteItems(writer, options.Items);
            if (options.Icon is not null) MenuJson.WriteIcon(writer, "icon", options.Icon);
        }, handler, cancellationToken);
        return new Submenu(app, pair.ResourceId, pair.Id);
    }

    public Task AppendAsync(IReadOnlyList<MenuItemBase> items, CancellationToken cancellationToken = default) =>
        MenuJson.ContainerCommandAsync(this, "append", items, null, cancellationToken);

    public Task PrependAsync(IReadOnlyList<MenuItemBase> items, CancellationToken cancellationToken = default) =>
        MenuJson.ContainerCommandAsync(this, "prepend", items, null, cancellationToken);

    public Task InsertAsync(IReadOnlyList<MenuItemBase> items, uint position, CancellationToken cancellationToken = default) =>
        MenuJson.ContainerCommandAsync(this, "insert", items, position, cancellationToken);

    public async Task<IReadOnlyList<MenuItemBase>> ItemsAsync(CancellationToken cancellationToken = default) =>
        MenuJson.ReadMenuItems(App, await InvokeMenuAsync("items", cancellationToken: cancellationToken));

    public Task PopupAsync(IDpiPosition? at = null, Window? window = null, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("popup", writer => MenuJson.WritePopup(writer, at, window), cancellationToken);

    public Task SetIconAsync(MenuIcon? icon, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_icon", writer => MenuJson.WriteIcon(writer, "icon", icon), cancellationToken);
}

public sealed class PredefinedMenuItem : MenuItemBase
{
    internal PredefinedMenuItem(AppHandle app, uint resourceId, string id) : base(app, resourceId, id, "Predefined") { }

    public static async Task<PredefinedMenuItem> NewAsync(
        AppHandle app,
        PredefinedMenuItemOptions options,
        Channel<string>? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pair = await MenuItemBase.NewAsync(app, "Predefined", writer =>
        {
            if (options.Text is not null) writer.WriteString("text", options.Text);
            MenuJson.WritePredefinedItem(writer, options);
        }, handler, cancellationToken);
        return new PredefinedMenuItem(app, pair.ResourceId, pair.Id);
    }

    public async Task<string> TextAsync(CancellationToken cancellationToken = default) =>
        (await InvokeMenuAsync("text", cancellationToken: cancellationToken)).GetString()
        ?? throw new InvokeException("The menu text response was invalid.");

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        InvokeMenuAsync("set_text", writer => writer.WriteString("text", text), cancellationToken);
}

internal static class MenuJson
{
    internal static void WriteMenuOptions(Utf8JsonWriter writer, MenuOptions? options)
    {
        if (options?.Id is not null) writer.WriteString("id", options.Id);
        WriteItems(writer, options?.Items);
    }

    internal static void WriteItemOptions(Utf8JsonWriter writer, MenuItemOptions options)
    {
        WriteBaseOptions(writer, options.Id, options.Text, options.Enabled);
        if (options.Accelerator is not null) writer.WriteString("accelerator", options.Accelerator);
    }

    internal static void WriteIcon(Utf8JsonWriter writer, string name, MenuIcon? icon)
    {
        if (icon is null)
        {
            writer.WriteNull(name);
        }
        else if (icon.Image is { } image)
        {
            writer.WriteNumber(name, image.ResourceId);
        }
        else if (icon.NativeIcon is { } nativeIcon)
        {
            writer.WriteString(name, nativeIcon.ToString());
        }
        else
        {
            throw new InvalidOperationException("The menu icon has no value.");
        }
    }

    internal static void WritePredefinedItem(Utf8JsonWriter writer, PredefinedMenuItemOptions options)
    {
        if (options.Item != PredefinedMenuItemType.About)
        {
            writer.WriteString("item", options.Item.ToString());
            return;
        }

        writer.WriteStartObject("item");
        writer.WritePropertyName("About");
        if (options.About is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            WriteAboutMetadata(writer, options.About);
        }
        writer.WriteEndObject();
    }

    private static void WriteAboutMetadata(Utf8JsonWriter writer, AboutMetadata metadata)
    {
        writer.WriteStartObject();
        WriteOptionalString(writer, "name", metadata.Name);
        WriteOptionalString(writer, "version", metadata.Version);
        WriteOptionalString(writer, "shortVersion", metadata.ShortVersion);
        if (metadata.Authors is not null)
        {
            writer.WriteStartArray("authors");
            foreach (var author in metadata.Authors)
            {
                ArgumentNullException.ThrowIfNull(author);
                writer.WriteStringValue(author);
            }
            writer.WriteEndArray();
        }
        WriteOptionalString(writer, "comments", metadata.Comments);
        WriteOptionalString(writer, "copyright", metadata.Copyright);
        WriteOptionalString(writer, "license", metadata.License);
        WriteOptionalString(writer, "website", metadata.Website);
        WriteOptionalString(writer, "websiteLabel", metadata.WebsiteLabel);
        WriteOptionalString(writer, "credits", metadata.Credits);
        if (metadata.Icon is not null) writer.WriteNumber("icon", metadata.Icon.ResourceId);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null) writer.WriteString(name, value);
    }

    internal static void WriteBaseOptions(Utf8JsonWriter writer, string? id, string text, bool? enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (id is not null) writer.WriteString("id", id);
        writer.WriteString("text", text);
        if (enabled is { } value) writer.WriteBoolean("enabled", value);
    }

    internal static void WriteItems(Utf8JsonWriter writer, IReadOnlyList<MenuItemBase>? items)
    {
        if (items is null) return;
        writer.WriteStartArray("items");
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.WriteReference(writer);
        }
        writer.WriteEndArray();
    }

    internal static void WriteChannel<T>(Utf8JsonWriter writer, string name, Channel<T>? channel)
    {
        if (channel is null) writer.WriteNull(name);
        else writer.WriteString(name, $"__CHANNEL__:{channel.Id}");
    }

    internal static (uint ResourceId, string Id) ReadResourcePair(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() < 2)
            throw new InvokeException("The native resource response was invalid.");
        return (result[0].GetUInt32(), result[1].GetString()
            ?? throw new InvokeException("The native resource id was invalid."));
    }

    internal static MenuItemBase? ReadMenuItem(AppHandle app, JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null) return null;
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() < 3)
            throw new InvokeException("The menu item response was invalid.");
        var rid = result[0].GetUInt32();
        var id = result[1].GetString() ?? throw new InvokeException("The menu item id was invalid.");
        var kind = result[2].GetString() ?? throw new InvokeException("The menu item kind was invalid.");
        return kind switch
        {
            "Menu" => new Menu(app, rid, id),
            "Submenu" => new Submenu(app, rid, id),
            "Check" => new CheckMenuItem(app, rid, id),
            "Predefined" => new PredefinedMenuItem(app, rid, id),
            "Icon" => new IconMenuItem(app, rid, id),
            "MenuItem" => new MenuItem(app, rid, id),
            _ => new MenuItem(app, rid, id, kind),
        };
    }

    internal static IReadOnlyList<MenuItemBase> ReadMenuItems(AppHandle app, JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array) throw new InvokeException("The menu items response was invalid.");
        var items = new List<MenuItemBase>(result.GetArrayLength());
        foreach (var value in result.EnumerateArray())
            items.Add(ReadMenuItem(app, value) ?? throw new InvokeException("A menu item response was null."));
        return items;
    }

    internal static Menu? ReadMenu(AppHandle app, JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null) return null;
        var pair = ReadResourcePair(result);
        return new Menu(app, pair.ResourceId, pair.Id);
    }

    internal static void WritePopup(Utf8JsonWriter writer, IDpiPosition? at, Window? window)
    {
        if (window is null) writer.WriteNull("window");
        else writer.WriteString("window", window.Label);
        if (at is null) writer.WriteNull("at");
        else ApiJson.WriteDpiPosition(writer, "at", at);
    }

    internal static Task ContainerCommandAsync(
        MenuItemBase container,
        string command,
        IReadOnlyList<MenuItemBase> items,
        uint? position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        return container.InvokeMenuAsync(command, writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in items) item.WriteReference(writer);
            writer.WriteEndArray();
            if (position is { } value) writer.WriteNumber("position", value);
        }, cancellationToken);
    }
}
