using System.Text.Json;

namespace NetWiv;

internal static class ApiJson
{
    internal static JsonElement Arguments(
        string? label,
        Action<Utf8JsonWriter>? write = null) =>
        JsonValue.Write(writer =>
        {
            writer.WriteStartObject();
            if (label is not null) writer.WriteString("label", label);
            write?.Invoke(writer);
            writer.WriteEndObject();
        });

    internal static void WriteDpiSize(Utf8JsonWriter writer, string name, IDpiSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        switch (size)
        {
            case LogicalSize logical:
                writer.WritePropertyName("Logical");
                writer.WriteStartObject();
                writer.WriteNumber("width", logical.Width);
                writer.WriteNumber("height", logical.Height);
                writer.WriteEndObject();
                break;
            case PhysicalSize physical:
                writer.WritePropertyName("Physical");
                writer.WriteStartObject();
                writer.WriteNumber("width", physical.Width);
                writer.WriteNumber("height", physical.Height);
                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentException("Size must be LogicalSize or PhysicalSize.", name);
        }
        writer.WriteEndObject();
    }

    internal static void WriteNullableDpiSize(Utf8JsonWriter writer, string name, IDpiSize? size)
    {
        if (size is null) writer.WriteNull(name);
        else WriteDpiSize(writer, name, size);
    }

    internal static void WriteDpiPosition(Utf8JsonWriter writer, string name, IDpiPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        switch (position)
        {
            case LogicalPosition logical:
                writer.WritePropertyName("Logical");
                writer.WriteStartObject();
                writer.WriteNumber("x", logical.X);
                writer.WriteNumber("y", logical.Y);
                writer.WriteEndObject();
                break;
            case PhysicalPosition physical:
                writer.WritePropertyName("Physical");
                writer.WriteStartObject();
                writer.WriteNumber("x", physical.X);
                writer.WriteNumber("y", physical.Y);
                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentException("Position must be LogicalPosition or PhysicalPosition.", name);
        }
        writer.WriteEndObject();
    }

    internal static void WriteConstraints(
        Utf8JsonWriter writer,
        string name,
        WindowSizeConstraints? constraints)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        WriteLogicalConstraint(writer, "minWidth", constraints?.MinWidth);
        WriteLogicalConstraint(writer, "minHeight", constraints?.MinHeight);
        WriteLogicalConstraint(writer, "maxWidth", constraints?.MaxWidth);
        WriteLogicalConstraint(writer, "maxHeight", constraints?.MaxHeight);
        writer.WriteEndObject();
    }

    internal static void WriteProgress(Utf8JsonWriter writer, string name, ProgressBarState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        if (state.Status is { } status) writer.WriteString("status", ProgressStatusName(status));
        if (state.Progress is { } progress) writer.WriteNumber("progress", progress);
        writer.WriteEndObject();
    }

    internal static string EnumName<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static void WriteEffects(Utf8JsonWriter writer, string name, Effects? effects)
    {
        if (effects is null) { writer.WriteNull(name); return; }
        writer.WriteStartObject(name);
        writer.WriteStartArray("effects");
        foreach (var effect in effects.Values) writer.WriteStringValue(EnumName(effect));
        writer.WriteEndArray();
        if (effects.State is { } state) writer.WriteString("state", EnumName(state));
        if (effects.Radius is { } radius) writer.WriteNumber("radius", radius);
        if (effects.Color is { } color) WriteColor(writer, "color", color);
        writer.WriteEndObject();
    }

    internal static void WriteNullableColor(Utf8JsonWriter writer, string name, RgbaColor? color)
    {
        if (color is { } value) WriteColor(writer, name, value);
        else writer.WriteNull(name);
    }

    internal static void WriteColor(Utf8JsonWriter writer, string name, RgbaColor color)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("red", color.Red);
        writer.WriteNumber("green", color.Green);
        writer.WriteNumber("blue", color.Blue);
        writer.WriteNumber("alpha", color.Alpha);
        writer.WriteEndObject();
    }

    internal static PhysicalSize ReadPhysicalSize(JsonElement value) =>
        new(value.GetProperty("width").GetUInt32(), value.GetProperty("height").GetUInt32());

    internal static PhysicalPosition ReadPhysicalPosition(JsonElement value) =>
        new(value.GetProperty("x").GetInt32(), value.GetProperty("y").GetInt32());

    internal static Monitor? ReadMonitor(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null :
        new(value.GetProperty("name").GetString(), ReadPhysicalSize(value.GetProperty("size")),
            ReadPhysicalPosition(value.GetProperty("position")),
            ReadPhysicalPosition(value.GetProperty("workArea").GetProperty("position")),
            ReadPhysicalSize(value.GetProperty("workArea").GetProperty("size")),
            value.GetProperty("scaleFactor").GetDouble());

    internal static Theme ReadTheme(JsonElement value) => value.GetString() switch
    {
        "light" => Theme.Light,
        "dark" => Theme.Dark,
        _ => throw new InvokeException("The window theme response was invalid."),
    };

    internal static string ThemeName(Theme value) => value switch
    {
        Theme.Light => "light",
        Theme.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static string ProgressStatusName(ProgressBarStatus value) => value switch
    {
        ProgressBarStatus.None => "none",
        ProgressBarStatus.Normal => "normal",
        ProgressBarStatus.Indeterminate => "indeterminate",
        ProgressBarStatus.Paused => "paused",
        ProgressBarStatus.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static string CursorIconName(CursorIcon value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void WriteLogicalConstraint(Utf8JsonWriter writer, string name, double? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        DpiValidation.Finite(value.Value, name);
        writer.WriteStartObject();
        writer.WriteNumber("Logical", value.Value);
        writer.WriteEndObject();
    }
}
