using System.Buffers;
using System.Text.Json;

namespace NetWiv;

public static class JsonValue
{
    public static JsonElement Null { get; } = Parse("null");
    public static JsonElement EmptyObject { get; } = Parse("{}");
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    public static JsonElement From(string? value) => Write(writer => writer.WriteStringValue(value));
    public static JsonElement From(bool value) => Write(writer => writer.WriteBooleanValue(value));
    public static JsonElement From(int value) => Write(writer => writer.WriteNumberValue(value));
    public static JsonElement From(long value) => Write(writer => writer.WriteNumberValue(value));
    public static JsonElement From(uint value) => Write(writer => writer.WriteNumberValue(value));

    internal static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
