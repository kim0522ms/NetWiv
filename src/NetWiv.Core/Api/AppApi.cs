using System.Text.Json;

namespace NetWiv;

public enum BundleType { Nsis, Msi, Deb, Rpm, AppImage, App }

public sealed class DataStoreIdentifier
{
    private readonly byte[] _bytes;

    public DataStoreIdentifier(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16) throw new ArgumentException("A data store identifier must contain exactly 16 bytes.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;
    public byte[] ToArray() => [.. _bytes];
}

public static class AppApi
{
    public static Task<string> GetVersionAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "version", cancellationToken);

    public static Task<string> GetNameAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "name", cancellationToken);

    public static Task<string> GetTauriVersionAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "tauri_version", cancellationToken);

    public static Task<string> GetIdentifierAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "identifier", cancellationToken);

    public static async Task<BundleType?> GetBundleTypeAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        (await InvokeAsync(app, "bundle_type", JsonValue.EmptyObject, cancellationToken)).GetString() switch
        {
            null => null,
            "nsis" => BundleType.Nsis,
            "msi" => BundleType.Msi,
            "deb" => BundleType.Deb,
            "rpm" => BundleType.Rpm,
            "appimage" => BundleType.AppImage,
            "app" => BundleType.App,
            var value => throw new InvokeException($"Unknown application bundle type '{value}'."),
        };

    public static async Task<IReadOnlyList<DataStoreIdentifier>> FetchDataStoreIdentifiersAsync(
        AppHandle app,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(app, "fetch_data_store_identifiers", JsonValue.EmptyObject, cancellationToken);
        if (result.ValueKind != JsonValueKind.Array)
            throw new InvokeException("The data store identifier response was not an array.");
        var identifiers = new List<DataStoreIdentifier>(result.GetArrayLength());
        foreach (var value in result.EnumerateArray()) identifiers.Add(ReadDataStoreIdentifier(value));
        return identifiers;
    }

    public static Task RemoveDataStoreAsync(
        AppHandle app,
        DataStoreIdentifier identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return InvokeAsync(app, "remove_data_store", ApiJson.Arguments(null, writer =>
        {
            writer.WriteStartArray("uuid");
            foreach (var value in identifier.Bytes.Span) writer.WriteNumberValue(value);
            writer.WriteEndArray();
        }), cancellationToken).AsTask();
    }

    public static async Task<bool> SupportsMultipleWindowsAsync(
        AppHandle app,
        CancellationToken cancellationToken = default) =>
        (await InvokeAsync(app, "supports_multiple_windows", JsonValue.EmptyObject, cancellationToken)).GetBoolean();

    public static Task ShowAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        InvokeAsync(app, "app_show", JsonValue.EmptyObject, cancellationToken).AsTask();

    public static Task HideAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        InvokeAsync(app, "app_hide", JsonValue.EmptyObject, cancellationToken).AsTask();

    public static Task SetThemeAsync(
        AppHandle app,
        Theme? theme,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(app, "set_app_theme", ApiJson.Arguments(null, writer =>
            writer.WriteString("theme", theme is null ? null : ApiJson.ThemeName(theme.Value))), cancellationToken).AsTask();

    public static Task SetDockVisibilityAsync(
        AppHandle app,
        bool visible,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(app, "set_dock_visibility", ApiJson.Arguments(null, writer =>
            writer.WriteBoolean("visible", visible)), cancellationToken).AsTask();

    public static async Task<Image?> DefaultWindowIconAsync(
        AppHandle app,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(app, "default_window_icon", JsonValue.EmptyObject, cancellationToken);
        return result.ValueKind == JsonValueKind.Null ? null : new Image(app, result.GetUInt32());
    }

    private static async Task<string> ReadStringAsync(
        AppHandle app,
        string command,
        CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(app, command, JsonValue.EmptyObject, cancellationToken);
        return result.GetString() ?? throw new InvokeException($"App command '{command}' did not return a string.");
    }

    private static ValueTask<JsonElement> InvokeAsync(
        AppHandle app,
        string command,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.InvokeAsync($"plugin:app|{command}", arguments, cancellationToken);
    }

    private static DataStoreIdentifier ReadDataStoreIdentifier(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 16)
            throw new InvokeException("A data store identifier must contain exactly 16 bytes.");
        Span<byte> bytes = stackalloc byte[16];
        var index = 0;
        foreach (var item in value.EnumerateArray()) bytes[index++] = item.GetByte();
        return new DataStoreIdentifier(bytes);
    }
}
