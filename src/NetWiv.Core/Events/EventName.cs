using System.Text;

namespace NetWiv;

internal static partial class EventName
{
    internal const string ArgumentError = "Event name must include only alphanumeric characters, `-`, `/`, `:` and `_`.";

    internal static bool IsValid(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is '-' or '/' or ':' or '_') continue;
            var ranges = AlphanumericRanges;
            int low = 0, high = ranges.Length / 2 - 1;
            bool found = false;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (rune.Value < ranges[middle * 2]) high = middle - 1;
                else if (rune.Value > ranges[middle * 2 + 1]) low = middle + 1;
                else { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    internal static void Validate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsValid(value)) throw new ArgumentException(
            $"only alphanumeric, '-', '/', ':', '_' permitted for event names: {RustDebugString.Format(value)}");
    }
}
