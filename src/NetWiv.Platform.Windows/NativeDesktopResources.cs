using System.Runtime.InteropServices;
using System.Text.Json;
using NetWiv.Platform.Windows.Interop;
using NetWiv.Runtime;

namespace NetWiv.Platform.Windows;
internal sealed class NativeDesktopResources(WindowsDesktopRuntime runtime) : IDisposable
{
    private const uint MfPopup = 0x10, MfSeparator = 0x800, MfGrayed = 1, MfChecked = 8, MfByPosition = 0x400, MiimBitmap = 0x80;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NifMessage = 1, NifIcon = 2, NifTip = 4, NifShowTip = 0x80;
    private const uint MouseMove = 0x200, LDown = 0x201, LUp = 0x202, LDbl = 0x203, RDown = 0x204, RUp = 0x205, RDbl = 0x206, MDown = 0x207, MUp = 0x208, MDbl = 0x209;
    private readonly Dictionary<uint, Resource> _resources = [];
    private readonly Dictionary<uint, MenuItem> _commands = [];
    private readonly Queue<uint> _freeCommands = [];
    private readonly Dictionary<string, Tray> _trays = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, Menu> _windowMenus = [];
    private uint _nextRid, _nextCommand = 0x1000;
    internal JsonElement Invoke(string command, JsonElement args, RuntimeCommandContext context) => command switch
    {
        "plugin:resources|close" => Close(args.GetProperty("rid").GetUInt32()),
        "plugin:menu|new" => NewMenu(args, context),
        "plugin:menu|append" => AddItems(args, context, false, false),
        "plugin:menu|prepend" => AddItems(args, context, true, false),
        "plugin:menu|insert" => AddItems(args, context, false, true),
        "plugin:menu|remove" => Remove(args),
        "plugin:menu|remove_at" => RemoveAt(args),
        "plugin:menu|items" => Items(args),
        "plugin:menu|get" => Find(args),
        "plugin:menu|popup" => Popup(args, context),
        "plugin:menu|create_default" => DefaultMenu(),
        "plugin:menu|set_as_window_menu" => SetWindowMenu(args, context),
        "plugin:menu|set_as_app_menu" => SetAppMenu(args),
        "plugin:menu|text" => J(Get<MenuItem>(args).Text),
        "plugin:menu|set_text" => Mutate(args, i => i.Text = args.GetProperty("text").GetString() ?? ""),
        "plugin:menu|is_enabled" => J(Get<MenuItem>(args).Enabled),
        "plugin:menu|set_enabled" => Mutate(args, i => i.Enabled = args.GetProperty("enabled").GetBoolean()),
        "plugin:menu|is_checked" => J(Get<MenuItem>(args).Checked),
        "plugin:menu|set_checked" => Mutate(args, i =>
        {
            if (i.Kind != "Check")
                throw new InvalidOperationException("Only Check items have checked state.");
            i.Checked = args.GetProperty("checked").GetBoolean();
        }),
        "plugin:menu|set_icon" => SetItemIcon(args),
        "plugin:menu|set_accelerator" => throw new NotSupportedException("Windows menu accelerators are not implemented."),
        "plugin:menu|set_as_windows_menu_for_nsapp" or "plugin:menu|set_as_help_menu_for_nsapp" => Null(),
        "plugin:tray|new" => NewTray(args, context),
        "plugin:tray|get_by_id" => GetTray(args),
        "plugin:tray|remove_by_id" => RemoveTray(args),
        "plugin:tray|set_visible" => SetTrayVisible(args),
        "plugin:tray|set_tooltip" => SetTrayTooltip(args),
        "plugin:tray|set_icon" or "plugin:tray|set_icon_with_as_template" => SetTrayIcon(args),
        "plugin:tray|set_menu" => SetTrayMenu(args),
        "plugin:tray|set_show_menu_on_left_click" => SetTrayLeft(args),
        "plugin:tray|set_title" => Null(),
        "plugin:tray|set_temp_dir_path" => Null(),
        "plugin:tray|set_icon_as_template" => Null(),
        _ => throw new NotSupportedException($"Windows native resource command '{command}' is not implemented."),
    };
    internal void HandleMenuCommand(uint id)
    {
        if (!_commands.TryGetValue(id, out MenuItem? item))
            return;
        if (item.Kind == "Check")
        {
            item.Checked = !item.Checked;
            RebuildContaining(item);
        }

        item.Channel?.Send(Q(item.Id));
        runtime.EmitEvent("menu-action", item.Id, $"{{\"id\":{Q(item.Id)}}}");
        if (item.Predefined == "CloseWindow")
            NativeMethods.PostMessage(runtime.FirstTopLevelHandle, NativeMethods.WmClose, 0, 0);
        else if (item.Predefined == "Quit")
            runtime.Exit();
    }

    internal void HandleTrayMessage(uint rid, uint message)
    {
        if (!_resources.TryGetValue(rid, out Resource? value) || value is not Tray tray)
            return;
        string? payload = TrayPayload(tray, message);
        if (payload is not null)
        {
            tray.Channel?.Send(payload);
            runtime.EmitEvent("tray-action", tray.Id, payload);
        }

        if (tray.Menu is not null && (message == RUp || (message == LUp && tray.ShowOnLeft)))
            ShowPopup(tray.Menu, tray.Window, null);
    }

    internal void HandleWindowDestroyed(nint window)
    {
        if (_windowMenus.TryGetValue(window, out Menu? menu))
            Detach(window, menu, false);
    }

    public void Dispose()
    {
        foreach (uint rid in _resources.Keys.ToArray())
            CloseCore(rid);
        foreach ((nint window, Menu menu)in _windowMenus.ToArray())
            Detach(window, menu);
        _commands.Clear();
        _trays.Clear();
        _windowMenus.Clear();
    }

    private JsonElement NewMenu(JsonElement args, RuntimeCommandContext context)
    {
        string kind = args.GetProperty("kind").GetString() ?? "Menu";
        JsonElement options = args.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
        Channel? channel = Channel.Parse(runtime, args, "handler", context.WebviewLabel);
        if (kind != "Menu")
        {
            MenuItem item = CreateItem(options, kind, context, channel);
            return Pair(AddResource(item), item.Id);
        }

        var menu = new Menu(Optional(options, "id") ?? $"menu-{_nextRid + 1}", "Menu", channel);
        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (JsonElement value in items.EnumerateArray())
                Add(menu, ResolveItem(value, context));
        Rebuild(menu);
        return Pair(AddResource(menu), menu.Id);
    }

    private MenuItem ResolveItem(JsonElement value, RuntimeCommandContext context)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return Get<MenuItem>(value[0].GetUInt32());
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Menu items must be options or resource tuples.");
        string kind = value.TryGetProperty("item", out _) ? "Predefined" : value.TryGetProperty("items", out _) ? "Submenu" : value.TryGetProperty("icon", out _) ? "Icon" : value.TryGetProperty("checked", out _) ? "Check" : "MenuItem";
        return CreateItem(value, kind, context, Channel.Parse(runtime, value, "handler", context.WebviewLabel));
    }

    private MenuItem CreateItem(JsonElement options, string kind, RuntimeCommandContext context, Channel? channel)
    {
        string? predefined = kind == "Predefined" ? PredefinedKind(options) : null;
        uint command = AllocateMenuCommand();
        string id = Optional(options, "id") ?? $"menu-item-{command}";
        var item = new MenuItem(
            id,
            kind,
            Optional(options, "text") ?? PredefinedText(predefined) ?? id,
            Optional(options, "enabled", true),
            Optional(options, "checked", false),
            command,
            channel,
            predefined);
        _commands.Add(item.Command, item);
        if (kind == "Submenu")
        {
            item.Submenu = new Menu(id, "Submenu", null);
            if (options.TryGetProperty("items", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in children.EnumerateArray())
                    Add(item.Submenu, ResolveItem(child, context));
            Rebuild(item.Submenu);
        }

        if (options.TryGetProperty("icon", out var icon))
            item.Bitmap = MenuBitmap(icon);
        return item;
    }

    private JsonElement AddItems(JsonElement args, RuntimeCommandContext context, bool prepend, bool insert)
    {
        Menu menu = GetMenu(args);
        int position = prepend ? 0 : insert ? checked((int)args.GetProperty("position").GetUInt32()) : menu.Items.Count;
        if ((uint)position > (uint)menu.Items.Count)
            throw new ArgumentOutOfRangeException("position");
        foreach (JsonElement value in args.GetProperty("items").EnumerateArray())
            Insert(menu, position++, ResolveItem(value, context));
        RebuildAttached(menu);
        return Null();
    }

    private JsonElement Remove(JsonElement args)
    {
        Menu menu = GetMenu(args);
        MenuItem item = Get<MenuItem>(args.GetProperty("item")[0].GetUInt32());
        int at = menu.Items.IndexOf(item);
        if (at >= 0)
            RemoveItem(menu, at);
        return Null();
    }

    private JsonElement RemoveAt(JsonElement args)
    {
        Menu menu = GetMenu(args);
        int at = checked((int)args.GetProperty("position").GetUInt32());
        if ((uint)at >= (uint)menu.Items.Count)
            return Null();
        MenuItem item = menu.Items[at];
        RemoveItem(menu, at);
        return Triple(AddResource(item), item.Id, item.Kind);
    }

    private JsonElement Items(JsonElement args)
    {
        Menu menu = GetMenu(args);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (MenuItem i in menu.Items)
            {
                w.WriteStartArray();
                w.WriteNumberValue(AddResource(i));
                w.WriteStringValue(i.Id);
                w.WriteStringValue(i.Kind);
                w.WriteEndArray();
            }

            w.WriteEndArray();
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private JsonElement Find(JsonElement args)
    {
        MenuItem? item = FindItem(GetMenu(args), args.GetProperty("id").GetString() ?? "");
        return item is null ? Null() : Triple(AddResource(item), item.Id, item.Kind);
    }

    private JsonElement Popup(JsonElement args, RuntimeCommandContext context)
    {
        string label = Optional(args, "window") ?? context.WindowLabel ?? runtime.GetWindowLabel(context.WebviewLabel);
        ShowPopup(GetMenu(args), runtime.GetTopLevelHandle(label), args.TryGetProperty("at", out var at) ? at : null);
        return Null();
    }

    private JsonElement DefaultMenu()
    {
        var menu = new Menu("default", "Menu", null);
        var file = new MenuItem("file", "Submenu", "File", true, false, AllocateMenuCommand(), null, null)
        {
            Submenu = new Menu("file", "Submenu", null)
        };
        _commands.Add(file.Command, file);
        var close = new MenuItem("close", "Predefined", "Close", true, false, AllocateMenuCommand(), null, "CloseWindow");
        _commands.Add(close.Command, close);
        Add(file.Submenu, close);
        Rebuild(file.Submenu);
        Add(menu, file);
        Rebuild(menu);
        return Pair(AddResource(menu), menu.Id);
    }

    private JsonElement SetWindowMenu(JsonElement args, RuntimeCommandContext context)
    {
        Menu menu = Get<Menu>(args);
        string label = Optional(args, "window") ?? context.WindowLabel ?? runtime.GetWindowLabel(context.WebviewLabel);
        Menu? old = Attach(runtime.GetTopLevelHandle(label), menu);
        return old is null ? Null() : Pair(AddResource(old), old.Id);
    }

    private JsonElement SetAppMenu(JsonElement args)
    {
        Menu menu = Get<Menu>(args);
        Menu? old = null;
        foreach (nint window in runtime.TopLevelHandles)
            old ??= Attach(window, menu);
        return old is null ? Null() : Pair(AddResource(old), old.Id);
    }

    private JsonElement Mutate(JsonElement args, Action<MenuItem> mutation)
    {
        MenuItem item = Get<MenuItem>(args);
        mutation(item);
        RebuildContaining(item);
        return Null();
    }

    private JsonElement SetItemIcon(JsonElement args)
    {
        MenuItem item = Get<MenuItem>(args);
        if (item.Kind is not ("Icon" or "Submenu"))
            throw new InvalidOperationException("Only Icon and Submenu items support icons.");
        if (item.Bitmap != 0)
            NativeMethods.DeleteObject(item.Bitmap);
        item.Bitmap = args.TryGetProperty("icon", out var i) ? MenuBitmap(i) : 0;
        RebuildContaining(item);
        return Null();
    }

    private JsonElement NewTray(JsonElement args, RuntimeCommandContext context)
    {
        JsonElement o = args.GetProperty("options");
        string id = Optional(o, "id") ?? $"tray-{_nextRid + 1}";
        if (_trays.ContainsKey(id))
            throw new InvalidOperationException($"Tray '{id}' already exists.");
        var tray = new Tray(id, runtime.FirstTopLevelHandle, o.TryGetProperty("icon", out var i) ? TrayIcon(i) : 0, Optional(o, "tooltip") ?? "", Channel.Parse(runtime, args, "handler", context.WebviewLabel))
        {
            ShowOnLeft = Optional(o, "showMenuOnLeftClick", true)
        };
        tray.Rid = AddResource(tray);
        _trays.Add(id, tray);
        if (o.TryGetProperty("menu", out var m) && m.ValueKind != JsonValueKind.Null)
            SetTrayMenuRef(tray, GetMenu(m[0].GetUInt32()));
        if (Optional(o, "visible", true))
            Visibility(tray, true);
        return Pair(tray.Rid, id);
    }

    private JsonElement GetTray(JsonElement args)
    {
        string id = args.GetProperty("id").GetString() ?? "";
        return _trays.TryGetValue(id, out Tray? t) ? Number(AddResource(t)) : Null();
    }

    private JsonElement RemoveTray(JsonElement args)
    {
        string id = args.GetProperty("id").GetString() ?? "";
        if (_trays.TryGetValue(id, out Tray? tray))
            foreach (uint rid in _resources.Where(x => ReferenceEquals(x.Value, tray)).Select(x => x.Key).ToArray())
                CloseCore(rid);
        return Null();
    }

    private JsonElement SetTrayVisible(JsonElement args)
    {
        Visibility(Get<Tray>(args), args.GetProperty("visible").GetBoolean());
        return Null();
    }

    private JsonElement SetTrayTooltip(JsonElement args)
    {
        Tray t = Get<Tray>(args);
        t.Tooltip = Optional(args, "tooltip") ?? "";
        if (t.Visible)
            Notify(t, NimModify);
        return Null();
    }

    private JsonElement SetTrayIcon(JsonElement args)
    {
        Tray t = Get<Tray>(args);
        nint old = t.Icon;
        t.Icon = args.TryGetProperty("icon", out var i) ? TrayIcon(i) : 0;
        if (t.Visible)
            Notify(t, NimModify);
        if (old != 0)
            NativeMethods.DestroyIcon(old);
        return Null();
    }

    private JsonElement SetTrayMenu(JsonElement args)
    {
        Tray t = Get<Tray>(args);
        SetTrayMenuRef(t, args.TryGetProperty("menu", out var m) && m.ValueKind != JsonValueKind.Null ? GetMenu(m[0].GetUInt32()) : null);
        return Null();
    }

    private JsonElement SetTrayLeft(JsonElement args)
    {
        Get<Tray>(args).ShowOnLeft = args.GetProperty("onLeft").GetBoolean();
        return Null();
    }

    private JsonElement Close(uint rid)
    {
        if (!CloseCore(rid))
            throw new KeyNotFoundException($"Native resource {rid} does not exist.");
        return Null();
    }

    private bool CloseCore(uint rid)
    {
        if (!_resources.Remove(rid, out Resource? r))
            return false;
        r.Refs--;
        TryDispose(r);
        return true;
    }

    private uint AddResource(Resource r)
    {
        uint id;
        do
        {
            id = ++_nextRid;
        }
        while (id == 0 || id >= 0x80000000 || _resources.ContainsKey(id));
        _resources.Add(id, r);
        r.Refs++;
        return id;
    }

    private uint AllocateMenuCommand()
    {
        if (_freeCommands.TryDequeue(out uint command))
        {
            return command;
        }

        if (_nextCommand > 0xEFFF)
        {
            throw new InvalidOperationException("The Win32 menu command id range is exhausted.");
        }

        return _nextCommand++;
    }

    private static void Add(Menu m, MenuItem i) => Insert(m, m.Items.Count, i);
    private static void Insert(Menu m, int at, MenuItem i)
    {
        m.Items.Insert(at, i);
        i.Parents++;
    }

    private void RemoveItem(Menu m, int at)
    {
        MenuItem i = m.Items[at];
        m.Items.RemoveAt(at);
        i.Parents--;
        RebuildAttached(m);
        TryDispose(i);
    }

    private void TryDispose(Resource r)
    {
        if (r.Refs != 0 || r.Parents != 0 || r.Disposed)
            return;
        r.Disposed = true;
        r.Channel?.End();
        if (r is Tray t)
        {
            _trays.Remove(t.Id);
            DisposeTray(t);
        }
        else if (r is Menu m)
        {
            foreach (var pair in m.Attached.ToArray())
                Detach(pair.Key, m);
            DisposeMenu(m);
        }
        else if (r is MenuItem i)
        {
            _commands.Remove(i.Command);
            _freeCommands.Enqueue(i.Command);
            if (i.Bitmap != 0)
                NativeMethods.DeleteObject(i.Bitmap);
            if (i.Submenu is not null)
                DisposeMenu(i.Submenu);
        }
    }

    private void DisposeMenu(Menu m)
    {
        RemoveNative(m.Handle);
        foreach (MenuItem i in m.Items)
        {
            i.Parents--;
            TryDispose(i);
        }

        m.Items.Clear();
        NativeMethods.DestroyMenu(m.Handle);
    }

    private void DisposeTray(Tray t)
    {
        if (t.Visible)
        {
            Notify(t, NimDelete);
            t.Visible = false;
        }

        if (t.Icon != 0)
        {
            NativeMethods.DestroyIcon(t.Icon);
            t.Icon = 0;
        }

        SetTrayMenuRef(t, null);
    }

    private void SetTrayMenuRef(Tray t, Menu? m)
    {
        if (ReferenceEquals(t.Menu, m))
            return;
        if (t.Menu is not null)
        {
            t.Menu.Parents--;
            TryDispose(t.Menu);
        }

        t.Menu = m;
        if (m is not null)
            m.Parents++;
    }

    private Menu? Attach(nint window, Menu menu)
    {
        Menu? old = _windowMenus.GetValueOrDefault(window);
        if (old is not null)
            Detach(window, old);
        nint h = Clone(menu, true);
        if (!NativeMethods.SetMenu(window, h) || !NativeMethods.DrawMenuBar(window))
        {
            NativeMethods.DestroyMenu(h);
            throw NativeMethods.Win32Error("SetMenu/DrawMenuBar");
        }

        menu.Attached.Add(window, h);
        menu.Parents++;
        _windowMenus[window] = menu;
        return old;
    }

    private void Detach(nint window, Menu menu, bool windowAlive = true)
    {
        if (!menu.Attached.Remove(window, out nint h))
            return;
        if (windowAlive)
        {
            NativeMethods.SetMenu(window, 0);
            NativeMethods.DrawMenuBar(window);
        }

        NativeMethods.DestroyMenu(h);
        _windowMenus.Remove(window);
        menu.Parents--;
        TryDispose(menu);
    }

    private void RebuildContaining(MenuItem item)
    {
        foreach (Menu m in AllMenus().Where(m => m.Items.Contains(item)).ToArray())
            RebuildAttached(m);
    }

    private IEnumerable<Menu> AllMenus()
    {
        var seen = new HashSet<Menu>(ReferenceEqualityComparer.Instance);
        foreach (Resource r in _resources.Values)
        {
            Menu? root = r as Menu ?? (r as MenuItem)?.Submenu;
            if (root is not null)
                foreach (Menu m in Tree(root, seen))
                    yield return m;
        }
    }

    private static IEnumerable<Menu> Tree(Menu m, HashSet<Menu> seen)
    {
        if (!seen.Add(m))
            yield break;
        yield return m;
        foreach (MenuItem i in m.Items)
            if (i.Submenu is not null)
                foreach (Menu n in Tree(i.Submenu, seen))
                    yield return n;
    }

    private void RebuildAttached(Menu m)
    {
        Rebuild(m);
        foreach (var pair in m.Attached.ToArray())
        {
            nint h = Clone(m, true);
            if (!NativeMethods.SetMenu(pair.Key, h) || !NativeMethods.DrawMenuBar(pair.Key))
            {
                NativeMethods.DestroyMenu(h);
                throw NativeMethods.Win32Error("SetMenu/DrawMenuBar");
            }

            m.Attached[pair.Key] = h;
            NativeMethods.DestroyMenu(pair.Value);
        }
    }

    private static void Rebuild(Menu m)
    {
        RemoveNative(m.Handle);
        foreach (MenuItem i in m.Items)
            Append(m.Handle, i, i.Submenu?.Handle ?? 0);
    }

    private static nint Clone(Menu m, bool bar)
    {
        nint h = bar ? NativeMethods.CreateMenu() : NativeMethods.CreatePopupMenu();
        if (h == 0)
            throw NativeMethods.Win32Error("CreateMenu");
        try
        {
            foreach (MenuItem i in m.Items)
                Append(h, i, i.Submenu is null ? 0 : Clone(i.Submenu, false));
            return h;
        }
        catch
        {
            NativeMethods.DestroyMenu(h);
            throw;
        }
    }

    private static void Append(nint menu, MenuItem i, nint submenu)
    {
        bool sep = i.Kind == "Predefined" && i.Predefined == "Separator";
        uint flags = sep ? MfSeparator : 0;
        if (!i.Enabled)
            flags |= MfGrayed;
        if (i.Checked)
            flags |= MfChecked;
        if (submenu != 0)
            flags |= MfPopup;
        if (!NativeMethods.AppendMenu(menu, flags, submenu == 0 ? i.Command : (nuint)submenu, sep ? null : i.Text))
            throw NativeMethods.Win32Error("AppendMenuW");
        if (i.Bitmap != 0)
        {
            var info = new MenuItemInfo
            {
                Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                Mask = MiimBitmap,
                ItemBitmap = i.Bitmap
            };
            if (!NativeMethods.SetMenuItemInfo(menu, (uint)(NativeMethods.GetMenuItemCount(menu) - 1), true, in info))
                throw NativeMethods.Win32Error("SetMenuItemInfoW");
        }
    }

    private static void RemoveNative(nint menu)
    {
        for (int n = NativeMethods.GetMenuItemCount(menu); n > 0; n--)
            if (!NativeMethods.RemoveMenu(menu, (uint)(n - 1), MfByPosition))
                throw NativeMethods.Win32Error("RemoveMenu");
    }

    private static void ShowPopup(Menu menu, nint window, JsonElement? at)
    {
        Point p;
        if (at is JsonElement v && v.ValueKind == JsonValueKind.Object)
        {
            JsonElement q = v.TryGetProperty("Physical", out var x) ? x : v.TryGetProperty("Logical", out var l) ? l : v;
            p = new()
            {
                X = (int)Math.Round(q.GetProperty("x").GetDouble()),
                Y = (int)Math.Round(q.GetProperty("y").GetDouble())
            };
            if (!NativeMethods.ClientToScreen(window, ref p))
                throw NativeMethods.Win32Error("ClientToScreen");
        }
        else if (!NativeMethods.GetCursorPos(out p))
            throw NativeMethods.Win32Error("GetCursorPos");
        NativeMethods.SetForegroundWindow(window);
        NativeMethods.TrackPopupMenu(menu.Handle, 0, p.X, p.Y, window, 0);
    }

    private static string? TrayPayload(Tray t, uint msg)
    {
        string type, button = "", state = "";
        switch (msg)
        {
            case MouseMove:
                type = "Move";
                break;
            case LDown:
                type = "Click";
                button = "Left";
                state = "Down";
                break;
            case LUp:
                type = "Click";
                button = "Left";
                state = "Up";
                break;
            case LDbl:
                type = "DoubleClick";
                button = "Left";
                break;
            case RDown:
                type = "Click";
                button = "Right";
                state = "Down";
                break;
            case RUp:
                type = "Click";
                button = "Right";
                state = "Up";
                break;
            case RDbl:
                type = "DoubleClick";
                button = "Right";
                break;
            case MDown:
                type = "Click";
                button = "Middle";
                state = "Down";
                break;
            case MUp:
                type = "Click";
                button = "Middle";
                state = "Up";
                break;
            case MDbl:
                type = "DoubleClick";
                button = "Middle";
                break;
            default:
                return null;
        }

        NativeMethods.GetCursorPos(out Point p);
        var ni = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Window = t.Window,
            Id = t.Rid
        };
        NativeMethods.ShellNotifyIconGetRect(in ni, out Rect r);
        string extra = button.Length == 0 ? "" : state.Length == 0 ? $",\"button\":{Q(button)}" : $",\"button\":{Q(button)},\"buttonState\":{Q(state)}";
        return $"{{\"type\":{Q(type)},\"id\":{Q(t.Id)},\"position\":{{\"x\":{p.X},\"y\":{p.Y}}},\"rect\":{{\"position\":{{\"Physical\":{{\"x\":{r.Left},\"y\":{r.Top}}}}},\"size\":{{\"Physical\":{{\"width\":{Math.Max(0, r.Right - r.Left)},\"height\":{Math.Max(0, r.Bottom - r.Top)}}}}}}}{extra}}}";
    }

    private static unsafe void Notify(Tray t, uint op)
    {
        var d = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = t.Window,
            Id = t.Rid,
            Flags = NifMessage | NifShowTip | (t.Icon == 0 ? 0 : NifIcon) | (t.Tooltip.Length == 0 ? 0 : NifTip),
            CallbackMessage = NativeMethods.WmAppTray,
            Icon = t.Icon
        };
        char* tip = d.Tip;
        t.Tooltip.AsSpan(0, Math.Min(127, t.Tooltip.Length)).CopyTo(new Span<char>(tip, 128));
        if (!NativeMethods.ShellNotifyIcon(op, ref d))
            throw NativeMethods.Win32Error("Shell_NotifyIconW");
    }

    private void Visibility(Tray t, bool visible)
    {
        if (t.Visible == visible)
            return;
        Notify(t, visible ? NimAdd : NimDelete);
        t.Visible = visible;
    }

    private nint TrayIcon(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint rid))
            throw new ArgumentException("Tray icons must be image resource ids.");
        return MakeIcon(Image(rid));
    }

    private nint MenuBitmap(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.String)
            return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint rid))
            throw new ArgumentException("Menu icons must be image resource ids.");
        NetWiv.ImageData image = Image(rid);
        byte[] bgra = Bgra(image);
        unsafe
        {
            fixed (byte* p = bgra)
            {
                nint h = NativeMethods.CreateBitmap((int)image.Width, (int)image.Height, 1, 32, p);
                return h != 0 ? h : throw NativeMethods.Win32Error("CreateBitmap");
            }
        }
    }

    private NetWiv.ImageData Image(uint rid) => runtime.Context.ResolveResource?.Invoke(rid)is NetWiv.ImageData image ? image : throw new KeyNotFoundException($"Image resource {rid} does not exist.");
    private static unsafe nint MakeIcon(NetWiv.ImageData image)
    {
        byte[] color = Bgra(image), mask = new byte[((int)image.Width + 15) / 16 * 2 * (int)image.Height];
        fixed (byte* m = mask)
        fixed (byte* c = color)
        {
            nint h = NativeMethods.CreateIcon(0, (int)image.Width, (int)image.Height, 1, 32, m, c);
            return h != 0 ? h : throw NativeMethods.Win32Error("CreateIcon");
        }
    }

    private static byte[] Bgra(NetWiv.ImageData image)
    {
        int w = checked((int)image.Width), h = checked((int)image.Height);
        if (w <= 0 || h <= 0 || image.Rgba.Length != checked(w * h * 4))
            throw new ArgumentException("Image data does not match dimensions.");
        byte[] b = new byte[image.Rgba.Length];
        ReadOnlySpan<byte> r = image.Rgba.Span;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4, d = ((h - 1 - y) * w + x) * 4;
                b[d] = r[s + 2];
                b[d + 1] = r[s + 1];
                b[d + 2] = r[s];
                b[d + 3] = r[s + 3];
            }

        return b;
    }

    private T Get<T>(JsonElement args)
        where T : Resource => Get<T>(args.GetProperty("rid").GetUInt32());
    private T Get<T>(uint rid)
        where T : Resource => _resources.TryGetValue(rid, out Resource? r) && r is T t ? t : throw new KeyNotFoundException($"Resource {rid} has the wrong type or is closed.");
    private Menu GetMenu(JsonElement args) => GetMenu(args.GetProperty("rid").GetUInt32());
    private Menu GetMenu(uint rid) => _resources.TryGetValue(rid, out Resource? r) ? r switch
    {
        Menu m => m,
        MenuItem { Submenu: not null } i => i.Submenu,
        _ => throw new InvalidOperationException("Resource is not a Menu or Submenu.")} : throw new KeyNotFoundException($"Resource {rid} is closed.");
    private static MenuItem? FindItem(Menu m, string id)
    {
        foreach (MenuItem i in m.Items)
        {
            if (i.Id == id)
                return i;
            if (i.Submenu is not null && FindItem(i.Submenu, id)is { } n)
                return n;
        }

        return null;
    }

    private static string? Optional(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Optional(JsonElement o, string n, bool d) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : d;
    private static string? PredefinedKind(JsonElement o)
    {
        if (!o.TryGetProperty("item", out var i))
            return null;
        if (i.ValueKind == JsonValueKind.String)
            return i.GetString();
        if (i.ValueKind == JsonValueKind.Object)
            foreach (var p in i.EnumerateObject())
                return p.Name;
        return null;
    }

    private static string? PredefinedText(string? s) => s switch
    {
        "Separator" => "",
        "SelectAll" => "Select All",
        "CloseWindow" => "Close",
        null => null,
        _ => s
    };
    private static string Q(string s) => "\"" + JsonEncodedText.Encode(s) + "\"";
    private static JsonElement J(string s) => JsonDocument.Parse(Q(s)).RootElement.Clone();
    private static JsonElement J(bool b) => JsonDocument.Parse(b ? "true" : "false").RootElement.Clone();
    private static JsonElement Null() => JsonDocument.Parse("null").RootElement.Clone();
    private static JsonElement Number(uint n) => JsonDocument.Parse(n.ToString()).RootElement.Clone();
    private static JsonElement Pair(uint r, string id) => JsonDocument.Parse($"[{r},{Q(id)}]").RootElement.Clone();
    private static JsonElement Triple(uint r, string id, string k) => JsonDocument.Parse($"[{r},{Q(id)},{Q(k)}]").RootElement.Clone();
    private abstract class Resource(string id, string kind, Channel? channel)
    {
        internal string Id { get; } = id;
        internal string Kind { get; } = kind;
        internal Channel? Channel { get; } = channel;

        internal int Refs, Parents;
        internal bool Disposed;
    }

    private sealed class Menu(string id, string kind, Channel? channel) : Resource(id, kind, channel)
    {
        internal nint Handle { get; } = Create();
        internal List<MenuItem> Items { get; } = [];
        internal Dictionary<nint, nint> Attached { get; } = [];

        private static nint Create()
        {
            nint h = NativeMethods.CreatePopupMenu();
            return h != 0 ? h : throw NativeMethods.Win32Error("CreatePopupMenu");
        }
    }

    private sealed class MenuItem(string id, string kind, string text, bool enabled, bool check, uint command, Channel? channel, string? predefined) : Resource(id, kind, channel)
    {
        internal string Text = text;
        internal bool Enabled = enabled, Checked = check;
        internal uint Command { get; } = command;
        internal string? Predefined { get; } = predefined;

        internal Menu? Submenu;
        internal nint Bitmap;
    }

    private sealed class Tray(string id, nint window, nint icon, string tooltip, Channel? channel) : Resource(id, "TrayIcon", channel)
    {
        internal uint Rid;
        internal nint Window { get; } = window;

        internal nint Icon = icon;
        internal string Tooltip = tooltip;
        internal bool Visible, ShowOnLeft;
        internal Menu? Menu;
    }

    private sealed class Channel(WindowsDesktopRuntime owner, string label, uint id)
    {
        private long _index;
        private bool _ended;
        internal static Channel? Parse(WindowsDesktopRuntime owner, JsonElement o, string name, string label)
        {
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
                return null;
            string s = v.GetString() ?? "";
            const string p = "__CHANNEL__:";
            return s.StartsWith(p, StringComparison.Ordinal) && uint.TryParse(s.AsSpan(p.Length), out uint id) ? new(owner, label, id) : throw new ArgumentException($"'{name}' is not a Tauri channel.");
        }

        internal void Send(string json)
        {
            if (!_ended)
                owner.EvaluateScript(label, $"window.__TAURI_INTERNALS__.runCallback({id},{{\"message\":{json},\"index\":{_index++}}});");
        }

        internal void End()
        {
            if (_ended)
                return;
            _ended = true;
            if (owner.HasWebview(label))
                owner.EvaluateScript(label, $"window.__TAURI_INTERNALS__.runCallback({id},{{\"end\":true,\"index\":{_index}}});");
        }
    }
}
