using System.Text.Json;

namespace NetWiv;

public sealed class Image : Resource
{
    internal Image(AppHandle app, uint resourceId) : base(app, resourceId) { }

    public static async Task<Image> NewAsync(
        AppHandle app,
        ReadOnlyMemory<byte> rgba,
        uint width,
        uint height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var result = await app.InvokeAsync("plugin:image|new", ApiJson.Arguments(null, writer =>
        {
            WriteBytes(writer, "rgba", rgba.Span);
            writer.WriteNumber("width", width);
            writer.WriteNumber("height", height);
        }), cancellationToken);
        return new Image(app, result.GetUInt32());
    }

    public static async Task<Image> FromBytesAsync(
        AppHandle app,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var result = await app.InvokeAsync("plugin:image|from_bytes", ApiJson.Arguments(null, writer =>
            WriteBytes(writer, "bytes", bytes.Span)), cancellationToken);
        return new Image(app, result.GetUInt32());
    }

    public static async Task<Image> FromPathAsync(
        AppHandle app,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = await app.InvokeAsync("plugin:image|from_path", ApiJson.Arguments(null, writer =>
            writer.WriteString("path", path)), cancellationToken);
        return new Image(app, result.GetUInt32());
    }

    public async Task<byte[]> RgbaAsync(CancellationToken cancellationToken = default)
    {
        var result = await App.InvokeAsync(
            "plugin:image|rgba",
            ApiJson.Arguments(null, writer => writer.WriteNumber("rid", ResourceId)),
            cancellationToken);
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new InvokeException("The image RGBA response was not a byte array.");
        }

        var bytes = new byte[result.GetArrayLength()];
        var index = 0;
        foreach (var value in result.EnumerateArray()) bytes[index++] = value.GetByte();
        return bytes;
    }

    public async Task<PhysicalSize> SizeAsync(CancellationToken cancellationToken = default) =>
        ApiJson.ReadPhysicalSize(await App.InvokeAsync(
            "plugin:image|size",
            ApiJson.Arguments(null, writer => writer.WriteNumber("rid", ResourceId)),
            cancellationToken));

    internal static void WriteReference(Utf8JsonWriter writer, string name, Image? image)
    {
        if (image is null) writer.WriteNull(name);
        else writer.WriteNumber(name, image.ResourceId);
    }

    internal static void WriteBytes(Utf8JsonWriter writer, string name, ReadOnlySpan<byte> bytes)
    {
        writer.WriteStartArray(name);
        foreach (var value in bytes) writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }
}
