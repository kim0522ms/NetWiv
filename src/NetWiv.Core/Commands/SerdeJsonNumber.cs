using System.Globalization;
using System.Text.Json;

namespace NetWiv;

internal static class SerdeJsonNumber
{
    // serde_json preserves floating-point syntax and negative zero even when
    // the mathematical value fits in an integer.
    internal static bool IsInteger(JsonElement value) => value.GetRawText() != "-0" &&
        (value.TryGetInt64(out _) || value.TryGetUInt64(out _));

    internal static string Unexpected(JsonElement value) => IsInteger(value)
        ? $"integer `{value.GetRawText()}`"
        : $"floating point `{Format(value.GetDouble())}`";

    internal static string Format(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!double.IsFinite(value)) return text;
        var sign = text.StartsWith('-') ? "-" : "";
        if (sign.Length != 0) text = text[1..];
        var exponentIndex = text.IndexOf('E');
        int exponent = exponentIndex < 0 ? 0 : int.Parse(text.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
        if (exponentIndex >= 0) text = text[..exponentIndex];
        int point = text.IndexOf('.');
        if (point < 0) point = text.Length;
        var digits = text.Replace(".", "", StringComparison.Ordinal);
        var significant = digits.TrimStart('0');
        if (significant.Length == 0) return sign + "0.0";
        exponent += point - (digits.Length - significant.Length) - 1;
        digits = significant.TrimEnd('0');
        // serde_json 1.0.151 uses zmij 1.0.23's f64 fixed range -5..=15.
        if (exponent is < -5 or > 15)
            return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" +
                (exponent >= 0 ? "+" : "") + exponent.ToString(CultureInfo.InvariantCulture);
        point = exponent + 1;
        if (point <= 0) return sign + "0." + new string('0', -point) + digits;
        if (point >= digits.Length) return sign + digits + new string('0', point - digits.Length) + ".0";
        return sign + digits.Insert(point, ".");
    }
}
