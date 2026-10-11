using System.Globalization;
using System.Text;

namespace NetWiv;

// serde's Unexpected::Str and Tauri's InvalidEventName use Rust string Debug,
// whose escaping rules and Unicode printability differ from JSON encoding.
internal static partial class RustDebugString
{
    internal static string Format(string value)
    {
        var output = new StringBuilder(value.Length + 2).Append('"');
        foreach (var rune in value.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '\0': output.Append("\\0"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                default:
                    if (IsPrintable(rune.Value)) output.Append(rune.ToString());
                    else output.Append("\\u{").Append(rune.Value.ToString("x", CultureInfo.InvariantCulture)).Append('}');
                    break;
            }
        }
        return output.Append('"').ToString();
    }

    private static bool IsPrintable(int value)
    {
        var ranges = PrintableRanges;
        int low = 0, high = ranges.Length / 2 - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (value < ranges[middle * 2]) high = middle - 1;
            else if (value > ranges[middle * 2 + 1]) low = middle + 1;
            else return true;
        }
        return false;
    }
}
