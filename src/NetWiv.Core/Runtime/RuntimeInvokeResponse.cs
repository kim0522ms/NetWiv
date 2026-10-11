using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NetWiv.Runtime;

public readonly struct RuntimeInvokeResponse
{
    private RuntimeInvokeResponse(JsonElement json, ReadOnlyMemory<byte> binary, bool isBinary)
    {
        Json = json;
        Binary = binary;
        IsBinary = isBinary;
    }

    public bool IsBinary { get; }
    public JsonElement Json { get; }
    public ReadOnlyMemory<byte> Binary { get; }

    /// <summary>Deserializes the response using generated metadata. Binary responses are byte sequences.</summary>
    public T? Deserialize<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (IsBinary && typeof(T) == typeof(byte[])) return (T)(object)Binary.ToArray();
        var binary = Binary;
        var value = IsBinary ? JsonValue.Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in binary.Span) writer.WriteNumberValue(item);
            writer.WriteEndArray();
        }) : Json;
        // A Tauri byte vector is a JSON array, whereas STJ's default byte[] converter expects base64.
        if (typeof(T) == typeof(byte[]))
        {
            if (value.ValueKind != JsonValueKind.Array) throw new JsonException("Response byte sequence must be a JSON array.");
            var bytes = new byte[value.GetArrayLength()];
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetByte(out var number))
                    throw new JsonException("Response byte sequence must contain integers from 0 to 255.");
                bytes[index++] = number;
            }
            return (T)(object)bytes;
        }
        return JsonSerializer.Deserialize(value, typeInfo);
    }

    public static RuntimeInvokeResponse FromJson(JsonElement value) => new(value.Clone(), default, false);
    public static RuntimeInvokeResponse FromBinary(ReadOnlyMemory<byte> value) => new(default, value, true);
    public static implicit operator RuntimeInvokeResponse(JsonElement value) => FromJson(value);
}
