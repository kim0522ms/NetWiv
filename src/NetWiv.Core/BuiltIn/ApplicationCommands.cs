// Desktop app behavior follows Tauri's app/plugin.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: MIT OR Apache-2.0
using System.Text.Json;

namespace NetWiv;

internal static class ApplicationCommands
{
    public static void Register(AppHandle app, CommandRegistry registry)
    {
        registry.Register("plugin:app|bundle_type", (_, _, _) => Value(ReadBundleType()));
        registry.Register("plugin:app|supports_multiple_windows", (_, _, _) => Value(JsonValue.From(true)));
        registry.Register("plugin:app|default_window_icon", (_, _, _) => Value(app.DefaultWindowIcon is { } image
            ? JsonValue.From(app.Resources.Add(image)) : JsonValue.Null));
        foreach (var command in new[] { "app_show", "app_hide", "fetch_data_store_identifiers" })
        {
            var nativeCommand = "plugin:app|" + command;
            registry.Register(nativeCommand, async (context, args, token) => await app.InvokeRuntimeDirectAsync(nativeCommand, args, context, token));
        }
        registry.Register("plugin:app|set_dock_visibility", async (context, args, token) =>
        {
            _ = args.GetProperty("visible").GetBoolean();
            return await app.InvokeRuntimeDirectAsync("plugin:app|set_dock_visibility", args, context, token);
        });
        registry.Register("plugin:app|remove_data_store", async (context, args, token) =>
        {
            var uuid = args.GetProperty("uuid");
            if (uuid.ValueKind != JsonValueKind.Array || uuid.GetArrayLength() != 16)
                throw new InvokeException("Data store identifiers must contain exactly sixteen bytes.");
            foreach (var item in uuid.EnumerateArray()) _ = item.GetByte();
            return await app.InvokeRuntimeDirectAsync("plugin:app|remove_data_store", args, context, token);
        });
    }

    private static JsonElement ReadBundleType()
    {
        using var stream = typeof(ApplicationCommands).Assembly.GetManifestResourceStream("netwiv.bundle.type")
            ?? throw new InvalidOperationException("The NetWiv bundle metadata resource is missing.");
        using var reader = new StreamReader(stream);
        var token = reader.ReadToEnd().Trim();
        var name = token.Length < 3 ? null : token[^3..] switch
        {
            "DEB" => "deb", "RPM" => "rpm", "APP" => "appimage", "MAC" => "app", "MSI" => "msi", "NSS" => "nsis", _ => null
        };
        return name is null ? JsonValue.Null : JsonValue.From(name);
    }

    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
}
