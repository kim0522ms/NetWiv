// The byte-array and element-reference wire contracts follow the .NET Foundation
// ASP.NET Core 8.0.32 implementation, licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Aot.Interop;

internal sealed class AotReferenceResolver(AotJsRuntime runtime, BlazorAotInteropRegistry registry) : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
        registry.ReferenceMetadata.TryGetValue(type, out var create) ? create(runtime, options) : null;
}

internal sealed class AotObjectReferenceConverter<T>(AotJsRuntime runtime) : JsonConverter<BlazorAotObjectReference<T>> where T : class
{
    public override BlazorAotObjectReference<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 || !value.TryGetProperty("__dotNetObject", out var id))
            throw new JsonException("Invalid NativeAOT callback reference.");
        return runtime.GetReference(id.GetInt64()) as BlazorAotObjectReference<T>
            ?? throw new JsonException("The NativeAOT callback reference has another registered type.");
    }

    public override void Write(Utf8JsonWriter writer, BlazorAotObjectReference<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("__dotNetObject", runtime.BindReference(value));
        writer.WriteEndObject();
    }
}

internal sealed class UnsupportedDotNetReferenceConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(DotNetObjectReference<>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("NativeAOT callbacks require BlazorAotObjectReference<T> and explicit instance registration. DotNetObjectReference<T> uses runtime discovery.");
}

internal sealed class AotByteArrayConverter(AotJsRuntime runtime) : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.TryGetBytesFromBase64(out var value)) return value;
        using var document = JsonDocument.ParseValue(ref reader);
        var reference = document.RootElement;
        if (reference.ValueKind != JsonValueKind.Object || reference.EnumerateObject().Count() != 1 || !reference.TryGetProperty("__byte[]", out var id))
            throw new JsonException("Invalid JavaScript byte-array reference.");
        return runtime.ReadBytes(id.GetInt32());
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        var id = runtime.SendBytes(value);
        writer.WriteStartObject();
        writer.WriteNumber("__byte[]", id);
        writer.WriteEndObject();
    }
}

internal sealed class AotElementReferenceConverter(ElementReferenceContext context) : JsonConverter<ElementReference>
{
    public override ElementReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 || !value.TryGetProperty("__internalId", out var id))
            throw new JsonException("Invalid element reference.");
        return new ElementReference(id.GetString() ?? throw new JsonException("An element reference requires an ID."), context);
    }

    public override void Write(Utf8JsonWriter writer, ElementReference value, JsonSerializerOptions options)
    {
        if (value.Context is not null && !ReferenceEquals(value.Context, context))
            throw new InvalidOperationException("An element reference belongs to another WebView document.");
        writer.WriteStartObject();
        writer.WriteString("__internalId", value.Id);
        writer.WriteEndObject();
    }
}
