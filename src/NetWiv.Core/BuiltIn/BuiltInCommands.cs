// Path behavior adapted from Tauri's path/plugin.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: MIT OR Apache-2.0
using System.Text.Json;

namespace NetWiv;

internal static class BuiltInCommands
{
    public static IReadOnlyList<PermissionDefinition> Permissions { get; } = CreatePermissions();

    public static void Register(AppHandle app, CommandRegistry registry)
    {
        registry.Register("plugin:app|name", (_, _, _) => Value(app.Metadata.Name));
        registry.Register("plugin:app|version", (_, _, _) => Value(app.Metadata.Version));
        registry.Register("plugin:app|tauri_version", (_, _, _) => throw new InvokeException("NetWiv does not embed the TAURI framework; its version is unavailable."));
        registry.Register("plugin:app|identifier", (_, _, _) => Value(app.Metadata.Identifier));
        ApplicationCommands.Register(app, registry);
        registry.Register("plugin:resources|close", async (context, args, cancellationToken) =>
        {
            var rid = args.GetProperty("rid").GetUInt32();
            if (app.Resources.Close(rid, context.IsTrusted ? null : context.WebviewLabel, context.DocumentId)) return JsonValue.Null;
            if (rid >= 0x80000000) throw new InvokeException($"The resource id {rid} is invalid.");
            return await app.InvokeRuntimeDirectAsync("plugin:resources|close", args, context, cancellationToken);
        });
        RegisterPathCommands(registry);
        RegisterEventCommands(app, registry);
        RegisterImageCommands(app, registry);
    }

    private static void RegisterPathCommands(CommandRegistry registry)
    {
        registry.Register("plugin:path|resolve_directory", async (context, args, cancellationToken) =>
        {
            var directory = args.GetProperty("directory").GetInt32();
            string? relative = args.TryGetProperty("path", out var path) && path.ValueKind != JsonValueKind.Null
                ? CommandArguments.String(args, "resolve_directory", "path") : null;
            return JsonValue.From(await context.App.ResolveDirectoryAsync(directory, relative, cancellationToken));
        });
        registry.Register("plugin:path|resolve", (_, args, _) => Value(JsonValue.From(ResolvePaths(args))));
        registry.Register("plugin:path|join", (_, args, _) => Value(JsonValue.From(JoinPaths(args))));
        registry.Register("plugin:path|normalize", (_, args, _) => Value(JsonValue.From(NormalizePath(ReadPath(args)))));
        registry.Register("plugin:path|dirname", (_, args, _) =>
        {
            var parent = LexicalParent(ReadPath(args));
            return parent is null ? throw new InvokeException("path does not have a parent") : Value(JsonValue.From(parent));
        });
        registry.Register("plugin:path|extname", (_, args, _) =>
        {
            var name = LexicalFileName(ReadPath(args));
            var dot = name?.LastIndexOf('.') ?? -1;
            return dot <= 0 ? throw new InvokeException("path does not have an extension") : Value(JsonValue.From(name![(dot + 1)..]));
        });
        registry.Register("plugin:path|basename", (_, args, _) =>
        {
            var value = LexicalFileName(ReadPath(args)) ?? throw new InvokeException("path does not have a basename");
            if (args.TryGetProperty("ext", out var ext) && ext.ValueKind == JsonValueKind.String &&
                value.EndsWith(ext.GetString()!, StringComparison.Ordinal)) value = value[..^ext.GetString()!.Length];
            return Value(JsonValue.From(value));
        });
        registry.Register("plugin:path|is_absolute", (_, args, _) => Value(JsonValue.From(Path.IsPathFullyQualified(ReadPath(args)))));
    }

    private static void RegisterImageCommands(AppHandle app, CommandRegistry registry)
    {
        registry.Register("plugin:image|new", (_, args, _) =>
        {
            var width = args.GetProperty("width").GetUInt32();
            var height = args.GetProperty("height").GetUInt32();
            var rgba = ReadBytes(args.GetProperty("rgba"));
            // Tauri Image::new_owned stores bytes and dimensions without decoding.
            // Native icon conversion validates the buffer when an icon is requested.
            return Value(JsonValue.From(app.Resources.Add(new ImageData(rgba, width, height))));
        });
        registry.Register("plugin:image|rgba", async (context, args, cancellationToken) =>
        {
            var rid = args.GetProperty("rid").GetUInt32();
            if (rid >= 0x80000000) _ = app.Resources.Get<ImageData>(rid);
            if (app.Resources.TryGet(rid) is not ImageData image)
                return await app.InvokeRuntimeDirectAsync("plugin:image|rgba", args, context, cancellationToken);
            return JsonValue.Write(writer =>
            {
                writer.WriteStartArray(); foreach (var value in image.Rgba.Span) writer.WriteNumberValue(value); writer.WriteEndArray();
            });
        });
        registry.Register("plugin:image|size", async (context, args, cancellationToken) =>
        {
            var rid = args.GetProperty("rid").GetUInt32();
            if (rid >= 0x80000000) _ = app.Resources.Get<ImageData>(rid);
            if (app.Resources.TryGet(rid) is not ImageData image)
                return await app.InvokeRuntimeDirectAsync("plugin:image|size", args, context, cancellationToken);
            return JsonValue.Write(writer =>
            {
                writer.WriteStartObject(); writer.WriteNumber("width", image.Width); writer.WriteNumber("height", image.Height); writer.WriteEndObject();
            });
        });
    }

    private static byte[] ReadBytes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new InvokeException("Binary IPC values must be an array of bytes.");
        var bytes = new byte[value.GetArrayLength()];
        var index = 0;
        foreach (var item in value.EnumerateArray()) bytes[index++] = item.GetByte();
        return bytes;
    }

    private static void RegisterEventCommands(AppHandle app, CommandRegistry registry)
    {
        registry.Register("plugin:event|listen", (context, args, _) =>
        {
            var eventName = ReadEventName(args, "listen");
            var callback = args.GetProperty("handler").GetUInt32();
            var target = ReadTarget(args);
            return Value(JsonValue.From(app.AddJsEventListener(eventName, context.WebviewLabel, callback, target, context.DocumentId)));
        });
        registry.Register("plugin:event|unlisten", (_, args, _) =>
        {
            var eventName = ReadEventName(args, "unlisten");
            var id = CommandArguments.Required(args, "unlisten", "eventId").GetUInt32();
            app.RemoveJsEventListener(eventName, id);
            return Value(JsonValue.Null);
        });
        registry.Register("plugin:event|emit", (_, args, _) =>
        {
            var eventName = ReadEventName(args, "emit");
            app.Emit(eventName, args.TryGetProperty("payload", out var payload) ? payload : JsonValue.Null);
            return Value(JsonValue.Null);
        });
        registry.Register("plugin:event|emit_to", (_, args, _) =>
        {
            var eventName = ReadEventName(args, "emit_to");
            var target = ReadTarget(args);
            app.Emit(eventName, args.TryGetProperty("payload", out var payload) ? payload : JsonValue.Null, target);
            return Value(JsonValue.Null);
        });
    }

    private static string ReadEventName(JsonElement args, string command)
    {
        var name = CommandArguments.String(args, command, "event");
        if (!EventName.IsValid(name)) throw CommandArguments.Error(command, "event", EventName.ArgumentError);
        return name;
    }

    private static Runtime.RuntimeEventTarget ReadTarget(JsonElement args)
    {
        if (!args.TryGetProperty("target", out var target) || target.ValueKind == JsonValueKind.Null) return Runtime.RuntimeEventTarget.Any;
        if (target.ValueKind == JsonValueKind.String) return new("AnyLabel", target.GetString());
        var kind = target.GetProperty("kind").GetString() ?? throw new InvokeException("Event target kind is required.");
        var label = target.TryGetProperty("label", out var configuredLabel) ? configuredLabel.GetString() : null;
        if (kind is not ("Any" or "AnyLabel" or "App" or "Window" or "Webview" or "WebviewWindow"))
            throw new InvokeException($"Event target kind '{kind}' is invalid.");
        if (kind is "AnyLabel" or "Window" or "Webview" or "WebviewWindow" && label is null)
            throw new InvokeException($"Event target kind '{kind}' requires a label.");
        return new(kind, label);
    }

    private static string ReadPath(JsonElement args) =>
        args.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty :
        args.TryGetProperty("paths", out var paths) && paths.GetArrayLength() > 0 ? paths[0].GetString() ?? string.Empty : string.Empty;

    private static string ResolvePaths(JsonElement args)
    {
        var paths = args.TryGetProperty("paths", out var values) ? values : args;
        var path = Environment.CurrentDirectory;
        foreach (var value in paths.EnumerateArray())
        {
            var item = value.GetString() ?? string.Empty;
            if (OperatingSystem.IsWindows() && Path.GetPathRoot(item) is "/" or "\\")
            {
                // Rust PathBuf::push keeps the existing drive/UNC prefix when
                // the next component has a root but no prefix of its own.
                var prefix = (Path.GetPathRoot(path) ?? string.Empty).TrimEnd('/', '\\');
                path = prefix + item;
            }
            else path = Path.IsPathRooted(item) ? item : Path.Combine(path, item);
        }
        return NormalizePath(path, preserveTrailingSeparator: false, preserveDriveRelative: true);
    }

    private static string JoinPaths(JsonElement args)
    {
        var paths = args.TryGetProperty("paths", out var values) ? values : args;
        var separator = Path.DirectorySeparatorChar;
        var combined = string.Concat(paths.EnumerateArray().Select(item =>
        {
            var value = item.GetString() ?? string.Empty;
            return value.Length > 0 && !value.EndsWith('/') && !value.EndsWith('\\') ? value + separator : value;
        }));
        var normalized = NormalizePath(combined, preserveTrailingSeparator: false);
        return normalized.Length == 0 ? "." : normalized;
    }

    private static string NormalizePath(string path, bool preserveTrailingSeparator = true, bool preserveDriveRelative = false)
    {
        var separator = Path.DirectorySeparatorChar;
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var remainder = path[root.Length..];
        var separators = OperatingSystem.IsWindows() ? new[] { '/', '\\' } : ['/'];
        var components = new List<string>();
        foreach (var component in remainder.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".") continue;
            if (component == "..")
            {
                if (components.Count > 0) components.RemoveAt(components.Count - 1);
                continue;
            }
            components.Add(component);
        }
        var normalized = OperatingSystem.IsWindows() ? root.Replace('/', '\\') : root;
        foreach (var component in components)
        {
            var driveRelative = preserveDriveRelative && OperatingSystem.IsWindows() &&
                normalized.Length == 2 && normalized[1] == ':';
            if (normalized.Length > 0 && !normalized.EndsWith('/') && !normalized.EndsWith('\\') && !driveRelative) normalized += separator;
            normalized += component;
        }
        if (normalized.Length == 0 && path is "." or "..") normalized = path;
        // Pinned Tauri's normalize intentionally appends even if the root already
        // ends in a separator; join/resolve call the helper without this behavior.
        if (preserveTrailingSeparator && (path.EndsWith('/') || path.EndsWith('\\'))) normalized += separator;
        return normalized;
    }

    private static bool IsPathSeparator(char value) => value == '/' || OperatingSystem.IsWindows() && value == '\\';

    private static string TrimPathEnd(string path)
    {
        int rootLength = (Path.GetPathRoot(path) ?? string.Empty).Length;
        if (OperatingSystem.IsWindows() && path.StartsWith("\\\\", StringComparison.Ordinal) &&
            rootLength < path.Length && IsPathSeparator(path[rootLength])) rootLength++;
        int end = path.Length;
        while (end > rootLength)
        {
            if (IsPathSeparator(path[end - 1])) { end--; continue; }
            if (path[end - 1] == '.' && end > rootLength + 1 && IsPathSeparator(path[end - 2])) { end--; continue; }
            break;
        }
        return path[..end];
    }

    private static string? LexicalFileName(string path)
    {
        var trimmed = TrimPathEnd(path);
        int rootLength = (Path.GetPathRoot(trimmed) ?? string.Empty).Length;
        int start = trimmed.Length;
        while (start > rootLength && !IsPathSeparator(trimmed[start - 1])) start--;
        var name = trimmed[start..];
        return name is "" or "." or ".." ? null : name;
    }

    private static string? LexicalParent(string path)
    {
        var trimmed = TrimPathEnd(path);
        int rootLength = (Path.GetPathRoot(trimmed) ?? string.Empty).Length;
        if (trimmed.Length == rootLength) return null;
        int end = trimmed.Length;
        while (end > rootLength && !IsPathSeparator(trimmed[end - 1])) end--;
        return TrimPathEnd(trimmed[..end]);
    }

    internal static string ResolveDirectory(int directory, string identifier)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cache = OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches") : OperatingSystem.IsLinux() ? XdgDirectory("XDG_CACHE_HOME", Path.Combine(home, ".cache")) : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var config = OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support") : OperatingSystem.IsLinux() ? XdgDirectory("XDG_CONFIG_HOME", Path.Combine(home, ".config")) : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var data = OperatingSystem.IsMacOS() ? config : OperatingSystem.IsLinux() ? XdgDirectory("XDG_DATA_HOME", Path.Combine(home, ".local", "share")) : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localData = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? data : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return directory switch
        {
        1 => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        2 => cache,
        3 => config,
        4 => data,
        5 => localData,
        6 => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        7 => XdgUserDirectory("DOWNLOAD", Path.Combine(home, "Downloads")),
        8 => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        9 => XdgUserDirectory("PUBLICSHARE", OperatingSystem.IsWindows() ? Path.Combine(Path.GetDirectoryName(home) ?? home, "Public") : Path.Combine(home, "Public")),
        10 => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        11 => AppContext.BaseDirectory,
        12 => Path.GetTempPath(),
        13 => Path.Combine(config, identifier),
        14 => Path.Combine(data, identifier),
        15 => Path.Combine(localData, identifier),
        16 => Path.Combine(cache, identifier),
        17 => OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Logs", identifier) : Path.Combine(localData, identifier, "logs"),
        18 => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        19 => OperatingSystem.IsLinux() ? XdgDirectory("XDG_BIN_HOME", Path.Combine(Path.GetDirectoryName(data) ?? Path.Combine(home, ".local"), "bin")) : AppContext.BaseDirectory,
        20 => OperatingSystem.IsLinux() ? Path.Combine(data, "fonts") : Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        21 => home,
        22 => OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? throw new InvokeException("The runtime directory is unavailable.") : Path.GetTempPath(),
        23 => OperatingSystem.IsLinux() ? XdgUserDirectory("TEMPLATES", Path.Combine(home, "Templates")) : Environment.GetFolderPath(Environment.SpecialFolder.Templates),
        _ => throw new InvokeException($"BaseDirectory value {directory} is not implemented.")
        };
    }

    private static string XdgDirectory(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

    private static string XdgUserDirectory(string name, string fallback)
    {
        if (!OperatingSystem.IsLinux()) return fallback;
        var configHome = XdgDirectory("XDG_CONFIG_HOME", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));
        var file = Path.Combine(configHome, "user-dirs.dirs");
        if (!File.Exists(file)) return fallback;
        var prefix = $"XDG_{name}_DIR=";
        foreach (var line in File.ReadLines(file))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var value = line[prefix.Length..].Trim().Trim('"').Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.Ordinal);
            return value;
        }
        return fallback;
    }

    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
    private static ValueTask<JsonElement> Value(string value) => Value(JsonValue.From(value));

    private static PermissionDefinition[] CreatePermissions()
    {
        static string[] Commands(string plugin, params string[] commands) =>
            [.. commands.Select(command => $"plugin:{plugin}|{command}")];

        // Generated from compatibility/generated/permissions-manifest.json at
        // tauri 2.11.5 (7cd71369c00978a3783b6ae3e9972358abbe4ae6).
        IReadOnlyDictionary<string, string[]> commandCatalog = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["app"] = ["app-hide", "app-show", "bundle-type", "default-window-icon", "fetch-data-store-identifiers", "identifier", "name", "register-listener", "remove-data-store", "remove-listener", "set-app-theme", "set-dock-visibility", "supports-multiple-windows", "tauri-version", "version"],
            ["event"] = ["emit", "emit-to", "listen", "unlisten"],
            ["image"] = ["from-bytes", "from-path", "new", "rgba", "size"],
            ["menu"] = ["append", "create-default", "get", "insert", "is-checked", "is-enabled", "items", "new", "popup", "prepend", "remove", "remove-at", "set-accelerator", "set-as-app-menu", "set-as-help-menu-for-nsapp", "set-as-window-menu", "set-as-windows-menu-for-nsapp", "set-checked", "set-enabled", "set-icon", "set-text", "text"],
            ["path"] = ["basename", "dirname", "extname", "is-absolute", "join", "normalize", "resolve", "resolve-directory"],
            ["resources"] = ["close"],
            ["tray"] = ["get-by-id", "new", "remove-by-id", "set-icon", "set-icon-as-template", "set-icon-with-as-template", "set-menu", "set-show-menu-on-left-click", "set-temp-dir-path", "set-title", "set-tooltip", "set-visible"],
            ["webview"] = ["clear-all-browsing-data", "create-webview", "create-webview-window", "get-all-webviews", "internal-toggle-devtools", "print", "reparent", "set-webview-auto-resize", "set-webview-background-color", "set-webview-focus", "set-webview-position", "set-webview-size", "set-webview-zoom", "webview-close", "webview-hide", "webview-position", "webview-show", "webview-size"],
            ["window"] = ["activity-name", "available-monitors", "center", "close", "create", "current-monitor", "cursor-position", "destroy", "get-all-windows", "hide", "inner-position", "inner-size", "internal-toggle-maximize", "is-always-on-top", "is-closable", "is-decorated", "is-enabled", "is-focused", "is-fullscreen", "is-maximizable", "is-maximized", "is-minimizable", "is-minimized", "is-resizable", "is-visible", "maximize", "minimize", "monitor-from-point", "outer-position", "outer-size", "primary-monitor", "request-user-attention", "scale-factor", "scene-identifier", "set-always-on-bottom", "set-always-on-top", "set-background-color", "set-badge-count", "set-badge-label", "set-closable", "set-content-protected", "set-cursor-grab", "set-cursor-icon", "set-cursor-position", "set-cursor-visible", "set-decorations", "set-effects", "set-enabled", "set-focus", "set-focusable", "set-fullscreen", "set-icon", "set-ignore-cursor-events", "set-max-size", "set-maximizable", "set-min-size", "set-minimizable", "set-overlay-icon", "set-position", "set-progress-bar", "set-resizable", "set-shadow", "set-simple-fullscreen", "set-size", "set-size-constraints", "set-skip-taskbar", "set-theme", "set-title", "set-title-bar-style", "set-visible-on-all-workspaces", "show", "start-dragging", "start-resize-dragging", "theme", "title", "toggle-maximize", "unmaximize", "unminimize"]
        };

        var permissions = new List<PermissionDefinition>();
        foreach (var (plugin, commands) in commandCatalog)
        {
            foreach (var command in commands)
            {
                var wireCommand = $"plugin:{plugin}|{command.Replace('-', '_')}";
                permissions.Add(new($"core:{plugin}:allow-{command}", [wireCommand], []));
                permissions.Add(new($"core:{plugin}:deny-{command}", [], [wireCommand]));
            }
        }

        var defaults = new[]
        {
            new PermissionDefinition("core:app:default", Commands("app", "version", "name", "tauri_version", "identifier", "bundle_type", "register_listener", "remove_listener", "supports_multiple_windows"), []),
            new PermissionDefinition("core:path:default", Commands("path", "resolve_directory", "resolve", "normalize", "join", "dirname", "extname", "basename", "is_absolute"), []),
            new PermissionDefinition("core:resources:default", Commands("resources", "close"), []),
            new PermissionDefinition("core:event:default", Commands("event", "listen", "unlisten", "emit", "emit_to"), []),
            new PermissionDefinition("core:image:default", Commands("image", "new", "from_bytes", "from_path", "rgba", "size"), []),
            new PermissionDefinition("core:menu:default", Commands("menu", "new", "append", "prepend", "insert", "remove", "remove_at", "items", "get", "popup", "create_default", "set_as_app_menu", "set_as_window_menu", "text", "set_text", "is_enabled", "set_enabled", "set_accelerator", "set_as_windows_menu_for_nsapp", "set_as_help_menu_for_nsapp", "is_checked", "set_checked", "set_icon"), []),
            new PermissionDefinition("core:tray:default", Commands("tray", "new", "get_by_id", "remove_by_id", "set_icon", "set_menu", "set_tooltip", "set_title", "set_visible", "set_temp_dir_path", "set_icon_as_template", "set_icon_with_as_template", "set_show_menu_on_left_click"), []),
            new PermissionDefinition("core:webview:default", Commands("webview", "get_all_webviews", "webview_position", "webview_size", "internal_toggle_devtools"), []),
            new PermissionDefinition("core:window:default", Commands("window", "get_all_windows", "scale_factor", "inner_position", "outer_position", "inner_size", "outer_size", "is_fullscreen", "is_minimized", "is_maximized", "is_focused", "is_decorated", "is_resizable", "is_maximizable", "is_minimizable", "is_closable", "is_visible", "is_enabled", "title", "current_monitor", "primary_monitor", "monitor_from_point", "available_monitors", "cursor_position", "theme", "is_always_on_top", "activity_name", "scene_identifier", "internal_toggle_maximize"), [])
        };
        permissions.AddRange(defaults);
        permissions.Add(new PermissionDefinition("core:default", [.. defaults.SelectMany(static permission => permission.Allow)], []));
        return [.. permissions];
    }
}

public sealed class ImageData
{
    public ImageData(ReadOnlySpan<byte> rgba, uint width, uint height)
    {
        Rgba = rgba.ToArray(); Width = width; Height = height;
    }
    public ReadOnlyMemory<byte> Rgba { get; }
    public uint Width { get; }
    public uint Height { get; }
}
