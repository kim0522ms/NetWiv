using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Linux.Native;

namespace NetWiv.Platform.Linux;

internal sealed unsafe class LinuxNativeResources : IDisposable
{
    private readonly LinuxDesktopRuntime _runtime;
    private readonly Dictionary<int, MenuResource> _menus = [];
    private readonly Dictionary<int, TrayResource> _trays = [];
    private int _nextResourceId;

    internal LinuxNativeResources(LinuxDesktopRuntime runtime)
    {
        _runtime = runtime;
    }

    internal bool CanInvoke(string command) =>
        command.StartsWith("plugin:menu|", StringComparison.Ordinal) ||
        command.StartsWith("plugin:tray|", StringComparison.Ordinal) ||
        command == "plugin:resources|close";

    internal JsonElement Invoke(
        string command,
        JsonElement args,
        Func<string, nint> getWindowRoot,
        string? callerWebviewLabel)
    {
        return command switch
        {
            "plugin:menu|new" => CreateMenu(args, callerWebviewLabel),
            "plugin:menu|items" => MenuItems(args),
            "plugin:menu|append" => AppendMenuItems(args),
            "plugin:menu|prepend" => AddMenuItems(args, prepend: true),
            "plugin:menu|insert" => InsertMenuItems(args),
            "plugin:menu|remove" => RemoveMenuItem(args),
            "plugin:menu|remove_at" => RemoveMenuItemAt(args),
            "plugin:menu|get" => GetMenuItem(args),
            "plugin:menu|text" => MenuText(args),
            "plugin:menu|set_text" => SetMenuText(args),
            "plugin:menu|is_enabled" => IsMenuEnabled(args),
            "plugin:menu|set_enabled" => SetMenuEnabled(args),
            "plugin:menu|is_checked" => IsMenuChecked(args),
            "plugin:menu|set_checked" => SetMenuChecked(args),
            "plugin:menu|set_icon" => SetMenuIcon(args),
            "plugin:menu|set_as_window_menu" => SetWindowMenu(args, getWindowRoot),
            "plugin:tray|new" => CreateTray(args),
            "plugin:tray|get_by_id" => GetTrayById(args),
            "plugin:tray|remove_by_id" => RemoveTrayById(args),
            "plugin:tray|set_menu" => SetTrayMenu(args),
            "plugin:tray|set_title" => SetTrayTitle(args),
            "plugin:tray|set_visible" => SetTrayVisible(args),
            "plugin:tray|set_icon" => SetTrayIcon(args),
            "plugin:resources|close" => CloseResource(args),
            _ => throw new NotSupportedException($"Linux desktop command '{command}' is not implemented."),
        };
    }

    public void Dispose()
    {
        foreach (TrayResource tray in _trays.Values)
        {
            LinuxNative.AppIndicatorSetStatus(tray.Native, 0);
            LinuxNative.GObjectUnref(tray.Native);
            DeleteIconFile(tray.IconPath);
        }

        foreach (MenuResource menu in _menus.Values)
        {
            LinuxNative.GObjectUnref(menu.Widget);
            menu.FreeActivationHandle();
        }

        _trays.Clear();
        _menus.Clear();
    }

    private JsonElement CreateMenu(JsonElement args, string? callerWebviewLabel)
    {
        string kind = RequiredString(args, "kind");
        JsonElement options = args.TryGetProperty("options", out JsonElement supplied)
            ? supplied
            : default;
        uint? handler = GetChannelId(args, "handler");
        MenuResource menu = CreateMenuResource(kind, options, callerWebviewLabel, handler);
        return Pair(menu.Rid, menu.Id);
    }

    private MenuResource CreateMenuResource(
        string kind,
        JsonElement options,
        string? callerWebviewLabel,
        uint? inheritedHandler)
    {
        string id = OptionalString(options, "id") ?? $"menu-{_nextResourceId + 1}";
        string label = OptionalString(options, "text") ?? OptionalString(options, "label") ?? id;

        nint widget;
        nint shell;
        switch (kind)
        {
            case "Menu":
                widget = LinuxNative.GtkMenuBarNew();
                shell = widget;
                break;
            case "Submenu":
                widget = LinuxNative.GtkImageMenuItemNewWithLabel(label);
                shell = LinuxNative.GtkMenuNew();
                LinuxNative.GtkMenuItemSetSubmenu(widget, shell);
                break;
            case "Predefined" when options.TryGetProperty("item", out JsonElement item) &&
                item.ValueKind == JsonValueKind.String && item.GetString() == "Separator":
                widget = LinuxNative.GtkSeparatorMenuItemNew();
                shell = 0;
                break;
            case "MenuItem":
            case "Predefined":
                widget = LinuxNative.GtkMenuItemNewWithLabel(label);
                shell = 0;
                break;
            case "Icon":
                widget = LinuxNative.GtkImageMenuItemNewWithLabel(label);
                shell = 0;
                break;
            case "Check":
                widget = LinuxNative.GtkCheckMenuItemNewWithLabel(label);
                shell = 0;
                break;
            default:
                throw new NotSupportedException($"GTK3 menu kind '{kind}' is not implemented.");
        }

        if (widget == 0)
        {
            throw new InvalidOperationException($"GTK3 failed to create menu resource '{id}'.");
        }

        LinuxNative.GObjectRefSink(widget);
        int rid = ++_nextResourceId;
        MenuResource resource = new(rid, id, kind, widget, shell);
        _menus.Add(rid, resource);
        LinuxNative.GtkWidgetSetSensitive(
            widget, GetOptionalBoolean(options, "enabled", true) ? 1 : 0);
        if (kind == "Check")
        {
            LinuxNative.GtkCheckMenuItemSetActive(
                widget, GetOptionalBoolean(options, "checked", false) ? 1 : 0);
        }
        if (options.TryGetProperty("icon", out JsonElement icon) && icon.ValueKind != JsonValueKind.Null)
        {
            SetMenuImage(resource, ResolveImage(icon));
        }

        uint? ownHandler = GetChannelId(options, "handler") ?? inheritedHandler;
        if (shell == 0 && kind != "Predefined" && callerWebviewLabel is not null && ownHandler is not null)
        {
            MenuActivation activation = new(_runtime, callerWebviewLabel, ownHandler.Value, id);
            resource.ActivationHandle = GCHandle.Alloc(activation);
            LinuxNative.GSignalConnectData(
                widget,
                "activate",
                (delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnMenuActivated,
                GCHandle.ToIntPtr(resource.ActivationHandle),
                0,
                0);
        }

        if (shell != 0 && options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                MenuResource child;
                if (item.ValueKind == JsonValueKind.Array)
                {
                    child = GetMenu(item[0].GetInt32());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    string childKind = OptionalString(item, "kind")
                        ?? (item.TryGetProperty("items", out _) ? "Submenu"
                            : item.TryGetProperty("checked", out _) ? "Check"
                            : item.TryGetProperty("icon", out _) ? "Icon"
                            : "MenuItem");
                    child = CreateMenuResource(childKind, item, callerWebviewLabel, ownHandler);
                }
                else
                {
                    throw new ArgumentException("Inline menu items must be resource pairs or option objects.");
                }

                AppendMenuItem(resource, child);
            }
        }

        return resource;
    }

    private JsonElement AppendMenuItems(JsonElement args)
        => AddMenuItems(args, prepend: false);

    private JsonElement AddMenuItems(JsonElement args, bool prepend)
    {
        MenuResource parent = GetMenu(args.GetProperty("rid").GetInt32());
        if (parent.Shell == 0)
        {
            throw new InvalidOperationException($"Menu resource '{parent.Id}' cannot contain children.");
        }

        JsonElement[] references = args.GetProperty("items").EnumerateArray().ToArray();
        if (prepend)
        {
            Array.Reverse(references);
        }
        foreach (JsonElement reference in references)
        {
            int rid = reference.ValueKind == JsonValueKind.Array
                ? reference[0].GetInt32()
                : reference.GetProperty("rid").GetInt32();
            MenuResource child = GetMenu(rid);
            if (prepend)
            {
                LinuxNative.GtkMenuShellPrepend(parent.Shell, child.Widget);
                LinuxNative.GtkWidgetShow(child.Widget);
                parent.Children.Insert(0, child.Rid);
            }
            else
            {
                AppendMenuItem(parent, child);
            }
        }

        return Null();
    }

    private JsonElement InsertMenuItems(JsonElement args)
    {
        MenuResource parent = GetContainer(args);
        int position = checked((int)args.GetProperty("position").GetUInt32());
        foreach (JsonElement reference in args.GetProperty("items").EnumerateArray())
        {
            MenuResource child = GetMenuReference(reference);
            int index = Math.Clamp(position++, 0, parent.Children.Count);
            LinuxNative.GtkMenuShellInsert(parent.Shell, child.Widget, index);
            LinuxNative.GtkWidgetShow(child.Widget);
            parent.Children.Insert(index, child.Rid);
        }
        return Null();
    }

    private JsonElement RemoveMenuItem(JsonElement args)
    {
        MenuResource parent = GetContainer(args);
        MenuResource child = GetMenuReference(args.GetProperty("item"));
        if (parent.Children.Remove(child.Rid))
        {
            LinuxNative.GtkContainerRemove(parent.Shell, child.Widget);
        }
        return Null();
    }

    private JsonElement RemoveMenuItemAt(JsonElement args)
    {
        MenuResource parent = GetContainer(args);
        int position = checked((int)args.GetProperty("position").GetUInt32());
        if ((uint)position >= (uint)parent.Children.Count)
        {
            return Null();
        }
        MenuResource child = GetMenu(parent.Children[position]);
        parent.Children.RemoveAt(position);
        LinuxNative.GtkContainerRemove(parent.Shell, child.Widget);
        return MenuTuple(child);
    }

    private JsonElement GetMenuItem(JsonElement args)
    {
        MenuResource parent = GetContainer(args);
        string id = RequiredString(args, "id");
        MenuResource? child = parent.Children.Select(GetMenu)
            .FirstOrDefault(value => value.Id == id);
        return child is null ? Null() : MenuTuple(child);
    }

    private JsonElement MenuText(JsonElement args) =>
        String(LinuxNative.Utf8(LinuxNative.GtkMenuItemGetLabel(GetMenuFromArgs(args).Widget)) ?? string.Empty);

    private JsonElement SetMenuText(JsonElement args)
    {
        LinuxNative.GtkMenuItemSetLabel(GetMenuFromArgs(args).Widget, RequiredString(args, "text"));
        return Null();
    }

    private JsonElement IsMenuEnabled(JsonElement args) =>
        Boolean(LinuxNative.GtkWidgetGetSensitive(GetMenuFromArgs(args).Widget) != 0);

    private JsonElement SetMenuEnabled(JsonElement args)
    {
        LinuxNative.GtkWidgetSetSensitive(
            GetMenuFromArgs(args).Widget, args.GetProperty("enabled").GetBoolean() ? 1 : 0);
        return Null();
    }

    private JsonElement IsMenuChecked(JsonElement args)
    {
        MenuResource menu = GetMenuFromArgs(args);
        EnsureCheck(menu);
        return Boolean(LinuxNative.GtkCheckMenuItemGetActive(menu.Widget) != 0);
    }

    private JsonElement SetMenuChecked(JsonElement args)
    {
        MenuResource menu = GetMenuFromArgs(args);
        EnsureCheck(menu);
        LinuxNative.GtkCheckMenuItemSetActive(
            menu.Widget, args.GetProperty("checked").GetBoolean() ? 1 : 0);
        return Null();
    }

    private JsonElement SetMenuIcon(JsonElement args)
    {
        MenuResource menu = GetMenuFromArgs(args);
        JsonElement icon = args.GetProperty("icon");
        if (icon.ValueKind == JsonValueKind.Null)
        {
            LinuxNative.GtkImageMenuItemSetImage(menu.Widget, 0);
        }
        else
        {
            SetMenuImage(menu, ResolveImage(icon));
        }
        return Null();
    }

    private static void AppendMenuItem(MenuResource parent, MenuResource child)
    {
        LinuxNative.GtkMenuShellAppend(parent.Shell, child.Widget);
        LinuxNative.GtkWidgetShow(child.Widget);
        parent.Children.Add(child.Rid);
    }

    private JsonElement MenuItems(JsonElement args)
    {
        MenuResource menu = GetMenu(args.GetProperty("rid").GetInt32());
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            foreach (int childRid in menu.Children)
            {
                MenuResource child = GetMenu(childRid);
                writer.WriteStartArray();
                writer.WriteNumberValue(child.Rid);
                writer.WriteStringValue(child.Id);
                writer.WriteStringValue(child.Kind);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private JsonElement SetWindowMenu(JsonElement args, Func<string, nint> getWindowRoot)
    {
        MenuResource menu = GetMenu(args.GetProperty("rid").GetInt32());
        string window = OptionalString(args, "window") ?? throw new ArgumentException("window is required.");
        nint root = getWindowRoot(window);
        LinuxNative.GtkBoxPackStart(root, menu.Widget, 0, 0, 0);
        LinuxNative.GtkBoxReorderChild(root, menu.Widget, 0);
        LinuxNative.GtkWidgetShow(menu.Widget);
        return Null();
    }

    private JsonElement CreateTray(JsonElement args)
    {
        JsonElement options = args.GetProperty("options");
        string id = OptionalString(options, "id") ?? $"tray-{_nextResourceId + 1}";
        string? iconPath = null;
        string iconName;
        if (options.TryGetProperty("icon", out JsonElement icon) && icon.ValueKind != JsonValueKind.Null)
        {
            iconPath = SaveTrayIcon(ResolveImage(icon));
            iconName = iconPath;
        }
        else
        {
            iconName = OptionalString(options, "iconName") ?? "application-x-executable";
        }
        nint indicator = LinuxNative.AppIndicatorNew(id, iconName, 0);
        if (indicator == 0)
        {
            DeleteIconFile(iconPath);
            throw new InvalidOperationException($"Ayatana AppIndicator failed to create tray '{id}'.");
        }

        int rid = ++_nextResourceId;
        _trays.Add(rid, new TrayResource(rid, id, indicator) { IconPath = iconPath });
        if (OptionalString(options, "title") is { } title)
        {
            LinuxNative.AppIndicatorSetTitle(indicator, title);
        }

        if (options.TryGetProperty("menu", out JsonElement menu) && menu.ValueKind == JsonValueKind.Array)
        {
            LinuxNative.AppIndicatorSetMenu(indicator, GetMenu(menu[0].GetInt32()).Shell);
        }

        LinuxNative.AppIndicatorSetStatus(indicator, 1);
        return Pair(rid, id);
    }

    private JsonElement GetTrayById(JsonElement args)
    {
        string id = RequiredString(args, "id");
        TrayResource? tray = _trays.Values.FirstOrDefault(value => value.Id == id);
        return tray is null ? Null() : Number(tray.Rid);
    }

    private JsonElement RemoveTrayById(JsonElement args)
    {
        string id = RequiredString(args, "id");
        TrayResource? tray = _trays.Values.FirstOrDefault(value => value.Id == id);
        if (tray is not null)
        {
            _trays.Remove(tray.Rid);
            LinuxNative.AppIndicatorSetStatus(tray.Native, 0);
            LinuxNative.GObjectUnref(tray.Native);
            DeleteIconFile(tray.IconPath);
        }

        return Null();
    }

    private JsonElement SetTrayMenu(JsonElement args)
    {
        TrayResource tray = GetTray(args.GetProperty("rid").GetInt32());
        JsonElement menu = args.GetProperty("menu");
        if (menu.ValueKind == JsonValueKind.Null)
        {
            throw new NotSupportedException("Ayatana AppIndicator cannot remove a menu after it is set.");
        }

        LinuxNative.AppIndicatorSetMenu(tray.Native, GetMenu(menu[0].GetInt32()).Shell);
        return Null();
    }

    private JsonElement SetTrayTitle(JsonElement args)
    {
        TrayResource tray = GetTray(args.GetProperty("rid").GetInt32());
        LinuxNative.AppIndicatorSetTitle(tray.Native, OptionalString(args, "title") ?? string.Empty);
        return Null();
    }

    private JsonElement SetTrayVisible(JsonElement args)
    {
        TrayResource tray = GetTray(args.GetProperty("rid").GetInt32());
        LinuxNative.AppIndicatorSetStatus(tray.Native, args.GetProperty("visible").GetBoolean() ? 1 : 0);
        return Null();
    }

    private JsonElement SetTrayIcon(JsonElement args)
    {
        TrayResource tray = GetTray(args.GetProperty("rid").GetInt32());
        string path = SaveTrayIcon(ResolveImage(args.GetProperty("icon")));
        string? previous = tray.IconPath;
        tray.IconPath = path;
        LinuxNative.AppIndicatorSetIconFull(tray.Native, path, tray.Id);
        DeleteIconFile(previous);
        return Null();
    }

    private JsonElement CloseResource(JsonElement args)
    {
        int rid = args.GetProperty("rid").GetInt32();
        if (_trays.Remove(rid, out TrayResource? tray))
        {
            LinuxNative.AppIndicatorSetStatus(tray.Native, 0);
            LinuxNative.GObjectUnref(tray.Native);
            DeleteIconFile(tray.IconPath);
            return Null();
        }

        if (_menus.Remove(rid, out MenuResource? menu))
        {
            LinuxNative.GObjectUnref(menu.Widget);
            menu.FreeActivationHandle();
            return Null();
        }

        throw new KeyNotFoundException($"No Linux native resource has rid {rid}.");
    }

    private MenuResource GetMenu(int rid) =>
        _menus.TryGetValue(rid, out MenuResource? menu)
            ? menu
            : throw new KeyNotFoundException($"No Linux menu resource has rid {rid}.");

    private MenuResource GetMenuFromArgs(JsonElement args) =>
        GetMenu(args.GetProperty("rid").GetInt32());

    private MenuResource GetContainer(JsonElement args)
    {
        MenuResource menu = GetMenuFromArgs(args);
        if (menu.Shell == 0)
        {
            throw new InvalidOperationException($"Menu resource '{menu.Id}' cannot contain children.");
        }
        return menu;
    }

    private MenuResource GetMenuReference(JsonElement reference) =>
        GetMenu(reference.ValueKind == JsonValueKind.Array
            ? reference[0].GetInt32()
            : reference.GetProperty("rid").GetInt32());

    private static void EnsureCheck(MenuResource menu)
    {
        if (menu.Kind != "Check")
        {
            throw new InvalidOperationException($"Menu resource '{menu.Id}' is not a check item.");
        }
    }

    private ImageData ResolveImage(JsonElement value)
    {
        uint rid = value.ValueKind == JsonValueKind.Number
            ? value.GetUInt32()
            : value.ValueKind == JsonValueKind.Array
                ? value[0].GetUInt32()
                : value.GetProperty("rid").GetUInt32();
        return _runtime.ResolveResource(rid) as ImageData
            ?? throw new KeyNotFoundException($"No image resource has rid {rid}.");
    }

    private static nint CreatePixbuf(ImageData image)
    {
        int width = checked((int)image.Width);
        int height = checked((int)image.Height);
        if (width <= 0 || height <= 0 || image.Rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException("ImageData must contain width * height * 4 RGBA bytes.");
        }

        nint pixbuf = LinuxNative.GdkPixbufNew(0, 1, 8, width, height);
        if (pixbuf == 0)
        {
            throw new InvalidOperationException("GdkPixbuf failed to allocate an RGBA image.");
        }

        int rowStride = LinuxNative.GdkPixbufGetRowstride(pixbuf);
        byte[] source = image.Rgba.ToArray();
        nint pixels = LinuxNative.GdkPixbufGetPixels(pixbuf);
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(source, y * width * 4, pixels + y * rowStride, width * 4);
        }
        return pixbuf;
    }

    private static void SetMenuImage(MenuResource menu, ImageData imageData)
    {
        nint pixbuf = CreatePixbuf(imageData);
        try
        {
            nint image = LinuxNative.GtkImageNewFromPixbuf(pixbuf);
            LinuxNative.GtkImageMenuItemSetImage(menu.Widget, image);
            LinuxNative.GtkWidgetShow(image);
        }
        finally
        {
            LinuxNative.GObjectUnref(pixbuf);
        }
    }

    private static string SaveTrayIcon(ImageData imageData)
    {
        nint pixbuf = CreatePixbuf(imageData);
        string path = Path.Combine(Path.GetTempPath(), $"netwiv-tray-{Guid.NewGuid():N}.png");
        try
        {
            if (LinuxNative.GdkPixbufSaveV(pixbuf, path, "png", 0, 0, out nint error) == 0)
            {
                if (error != 0) LinuxNative.GErrorFree(error);
                throw new InvalidOperationException("GdkPixbuf failed to encode a tray icon.");
            }
            return path;
        }
        catch
        {
            DeleteIconFile(path);
            throw;
        }
        finally
        {
            LinuxNative.GObjectUnref(pixbuf);
        }
    }

    private static void DeleteIconFile(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private TrayResource GetTray(int rid) =>
        _trays.TryGetValue(rid, out TrayResource? tray)
            ? tray
            : throw new KeyNotFoundException($"No Linux tray resource has rid {rid}.");

    private static string RequiredString(JsonElement value, string property) =>
        OptionalString(value, property) ?? throw new ArgumentException($"{property} is required.");

    private static string? OptionalString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static bool GetOptionalBoolean(JsonElement value, string property, bool defaultValue) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : defaultValue;

    private static uint? GetChannelId(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(property, out JsonElement item))
        {
            return null;
        }

        if (item.ValueKind == JsonValueKind.Number && item.TryGetUInt32(out uint numeric))
        {
            return numeric;
        }

        const string prefix = "__CHANNEL__:";
        string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        return text is not null && text.StartsWith(prefix, StringComparison.Ordinal) &&
            uint.TryParse(text.AsSpan(prefix.Length), out uint channel)
                ? channel
                : null;
    }

    private static JsonElement Pair(int rid, string id) => Parse($"[{rid},\"{JsonEncodedText.Encode(id)}\"]");
    private static JsonElement MenuTuple(MenuResource menu) =>
        Parse($"[{menu.Rid},\"{JsonEncodedText.Encode(menu.Id)}\",\"{JsonEncodedText.Encode(menu.Kind)}\"]");
    private static JsonElement Number(int value) => Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static JsonElement String(string value) => Parse($"\"{JsonEncodedText.Encode(value)}\"");
    private static JsonElement Boolean(bool value) => Parse(value ? "true" : "false");
    private static JsonElement Null() => Parse("null");
    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMenuActivated(nint widget, nint data)
    {
        try
        {
            MenuActivation activation = (MenuActivation)GCHandle.FromIntPtr(data).Target!;
            long index = Interlocked.Increment(ref activation.Index) - 1;
            activation.Runtime.EvaluateScript(
                activation.WebviewLabel,
                $"window.__TAURI_INTERNALS__.runCallback({activation.ChannelId}," +
                $"{{message:\"{JsonEncodedText.Encode(activation.ItemId)}\",index:{index}}});");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NetWiv Linux menu callback failed: {exception}");
        }
    }

    private sealed record MenuResource(int Rid, string Id, string Kind, nint Widget, nint Shell)
    {
        internal List<int> Children { get; } = [];
        internal GCHandle ActivationHandle { get; set; }

        internal void FreeActivationHandle()
        {
            if (ActivationHandle.IsAllocated)
            {
                ActivationHandle.Free();
            }
        }
    }
    private sealed class MenuActivation(
        LinuxDesktopRuntime runtime,
        string webviewLabel,
        uint channelId,
        string itemId)
    {
        internal LinuxDesktopRuntime Runtime { get; } = runtime;
        internal string WebviewLabel { get; } = webviewLabel;
        internal uint ChannelId { get; } = channelId;
        internal string ItemId { get; } = itemId;
        internal long Index;
    }
    private sealed record TrayResource(int Rid, string Id, nint Native)
    {
        internal string? IconPath { get; set; }
    }
}
