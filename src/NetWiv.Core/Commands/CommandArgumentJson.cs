using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NetWiv;

/// <summary>Validates command input using only source-generated JSON metadata.</summary>
public static class CommandArgumentJson
{
    public static T Deserialize<T>(JsonElement value, JsonTypeInfo<T> typeInfo, string command, string argument, bool nullable)
    {
        try
        {
            Validate(value, typeInfo, nullable);
            return JsonSerializer.Deserialize(value, typeInfo)!;
        }
        catch (ArgumentShapeException error)
        {
            throw new InvokeException($"invalid args `{argument}` for command `{command}`: {error.Message}");
        }
        catch (Exception error) when (error is JsonException || error is NotSupportedException && typeInfo.PolymorphismOptions is not null)
        {
            throw new InvokeException($"invalid args `{argument}` for command `{command}`: {error.Message}", error);
        }
    }

    private static void Validate(JsonElement value, JsonTypeInfo info, bool nullable, JsonNumberHandling? numberHandling = null)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (nullable) return;
            Invalid(value, Expected(info));
        }
        if (info.Kind == JsonTypeInfoKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object) Invalid(value, Expected(info));
            // Select the same declared variant as STJ before validating its members. The
            // original base metadata still owns deserialization and discriminator errors.
            if (info.PolymorphismOptions is { } polymorphism &&
                value.TryGetProperty(polymorphism.TypeDiscriminatorPropertyName, out var discriminator))
            {
                foreach (var derived in polymorphism.DerivedTypes)
                    if (derived.TypeDiscriminator is string text && discriminator.ValueKind == JsonValueKind.String && discriminator.GetString() == text ||
                        derived.TypeDiscriminator is int tagNumber && discriminator.ValueKind == JsonValueKind.Number && discriminator.TryGetInt32(out var actual) && actual == tagNumber)
                    {
                        info = info.Options.GetTypeInfo(derived.DerivedType);
                        break;
                    }
            }
            // serde_json::Value uses a sorted object map in the pinned Tauri build.
            foreach (var field in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                var property = info.Properties.FirstOrDefault(property => !property.IsExtensionData && string.Equals(property.Name, field.Name,
                    info.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                if (property is null || !CanDeserialize(property, info) || property.CustomConverter is not null) continue;
                var propertyInfo = info.Options.GetTypeInfo(property.PropertyType);
                Validate(field.Value, propertyInfo, property.IsSetNullable,
                    property.NumberHandling ?? info.NumberHandling ?? propertyInfo.NumberHandling);
            }
            foreach (var property in info.Properties)
            {
                if (property.IsExtensionData || !CanDeserialize(property, info)) continue;
                bool required = property.IsRequired || property.AssociatedParameter is { HasDefaultValue: false, IsNullable: false };
                if (required && !value.EnumerateObject().Any(field => string.Equals(field.Name, property.Name,
                        info.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new ArgumentShapeException($"missing field `{property.Name}`");
            }
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            if (value.ValueKind != JsonValueKind.Array) Invalid(value, "a sequence");
            if (info.ElementType is { } element)
                foreach (var item in value.EnumerateArray()) Validate(item, info.Options.GetTypeInfo(element),
                    !element.IsValueType || Nullable.GetUnderlyingType(element) is not null, numberHandling ?? info.NumberHandling);
            return;
        }
        if (info.Kind == JsonTypeInfoKind.Dictionary)
        {
            if (value.ValueKind != JsonValueKind.Object) Invalid(value, "a map");
            if (info.ElementType is { } element)
                foreach (var item in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                    Validate(item.Value, info.Options.GetTypeInfo(element), !element.IsValueType || Nullable.GetUnderlyingType(element) is not null,
                        numberHandling ?? info.NumberHandling);
            return;
        }
        Type type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (type == typeof(string)) { if (value.ValueKind != JsonValueKind.String) Invalid(value, "a string"); return; }
        if (type == typeof(bool)) { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Invalid(value, "a boolean"); return; }
        string? number = NumberName(type);
        if (number is null) return; // Custom converters retain their own validation behavior.
        var handling = numberHandling ?? info.NumberHandling ?? info.Options.NumberHandling;
        if (value.ValueKind == JsonValueKind.String &&
            ((handling & JsonNumberHandling.AllowReadingFromString) != 0 ||
             (handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0 && (type == typeof(float) || type == typeof(double))))
            return; // The generated serializer checks quoted numeric syntax, ranges and named literals.
        if (value.ValueKind != JsonValueKind.Number) Invalid(value, number);
        if (type != typeof(float) && type != typeof(double) && type != typeof(decimal) && !SerdeJsonNumber.IsInteger(value))
            Invalid(value, number);
        bool valid = type == typeof(byte) ? value.TryGetByte(out _) :
            type == typeof(sbyte) ? value.TryGetSByte(out _) : type == typeof(short) ? value.TryGetInt16(out _) :
            type == typeof(ushort) ? value.TryGetUInt16(out _) : type == typeof(int) ? value.TryGetInt32(out _) :
            type == typeof(uint) ? value.TryGetUInt32(out _) : type == typeof(long) ? value.TryGetInt64(out _) :
            type == typeof(ulong) ? value.TryGetUInt64(out _) : type == typeof(float) ? value.TryGetSingle(out _) :
            type == typeof(double) ? value.TryGetDouble(out _) : value.TryGetDecimal(out _);
        if (!valid) throw new ArgumentShapeException($"invalid value: {Unexpected(value)}, expected {number}");
    }

    private static bool CanDeserialize(JsonPropertyInfo property, JsonTypeInfo declaringType)
    {
        if (property.Set is not null) return true;
        if (property.Get is null) return false; // Includes JsonIgnore, even on constructor-bound properties.
        return property.AssociatedParameter is not null ||
            (property.ObjectCreationHandling ?? declaringType.PreferredPropertyObjectCreationHandling ??
                declaringType.Options.PreferredObjectCreationHandling) == JsonObjectCreationHandling.Populate;
    }

    private static string Expected(JsonTypeInfo info) => info.Kind switch
    {
        JsonTypeInfoKind.Object => $"struct {info.Type.Name}",
        JsonTypeInfoKind.Dictionary => "a map",
        JsonTypeInfoKind.Enumerable => "a sequence",
        _ => info.Type == typeof(string) ? "a string" : info.Type == typeof(bool) ? "a boolean" : NumberName(info.Type) ?? info.Type.Name
    };

    private static string? NumberName(Type type) =>
        type == typeof(byte) ? "u8" : type == typeof(sbyte) ? "i8" :
        type == typeof(short) ? "i16" : type == typeof(ushort) ? "u16" :
        type == typeof(int) ? "i32" : type == typeof(uint) ? "u32" :
        type == typeof(long) ? "i64" : type == typeof(ulong) ? "u64" :
        type == typeof(float) ? "f32" : type == typeof(double) ? "f64" :
        type == typeof(decimal) ? "a decimal" : null;

    internal static string Unexpected(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => $"string {RustDebugString.Format(value.GetString()!)}",
        JsonValueKind.True => "boolean `true`",
        JsonValueKind.False => "boolean `false`",
        JsonValueKind.Object => "map",
        JsonValueKind.Array => "sequence",
        JsonValueKind.Number => SerdeJsonNumber.Unexpected(value),
        _ => "missing value"
    };
    private static void Invalid(JsonElement value, string expected) => throw new ArgumentShapeException($"invalid type: {Unexpected(value)}, expected {expected}");
    private sealed class ArgumentShapeException(string message) : Exception(message);
}
