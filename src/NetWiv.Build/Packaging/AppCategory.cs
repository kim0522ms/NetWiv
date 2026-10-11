using System.Text;
using NetWiv.Build.Configuration;

namespace NetWiv.Build.Packaging;

/// <summary>Tauri bundle category names, aliases and desktop menu classification.</summary>
public sealed partial record AppCategory
{
    private AppCategory(string name, string canonical, string desktop, string macos) =>
        (Name, CanonicalName, FreedesktopCategories, MacOSApplicationCategoryType) = (name, canonical, desktop, macos);

    public string Name { get; }
    public string CanonicalName { get; }
    public string FreedesktopCategories { get; }
    public string MacOSApplicationCategoryType { get; }

    public static AppCategory Parse(string input)
    {
        if (TryParse(input, out var category, out var suggestion)) return category!;
        throw new NetWivConfigException($"Invalid bundle.category '{input}': expected a valid app category string" +
            (suggestion is null ? "." : $" (did you mean \"{suggestion}\"?)."));
    }

    public static bool TryParse(string input, out AppCategory? category, out string? suggestion)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Rust's to_ascii_lowercase does not fold non-ASCII characters.
        var normalized = string.Concat(input.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));
        const string prefix = "public.app-category.";
        if (normalized.StartsWith(prefix, StringComparison.Ordinal)) normalized = normalized[prefix.Length..];
        normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        category = null;
        suggestion = null;
        var best = 0.0;
        var runes = normalized.EnumerateRunes().ToArray();
        foreach (var (alias, candidate) in Aliases)
        {
            if (normalized == alias) { category = candidate; suggestion = null; return true; }
            var confidence = JaroWinkler(runes, alias.EnumerateRunes().ToArray());
            if (confidence >= 0.8 && confidence > best) { best = confidence; suggestion = candidate.CanonicalName; }
        }
        return false;
    }

    private static double JaroWinkler(Rune[] left, Rune[] right)
    {
        if (left.Length == 0 || right.Length == 0) return left.Length == right.Length ? 1 : 0;
        var distance = Math.Max(0, Math.Max(left.Length, right.Length) / 2 - 1);
        var leftMatched = new bool[left.Length];
        var rightMatched = new bool[right.Length];
        var count = 0;
        for (var i = 0; i < left.Length; i++)
            for (var j = Math.Max(0, i - distance); j < Math.Min(right.Length, i + distance + 1); j++)
                if (!rightMatched[j] && left[i] == right[j])
                {
                    leftMatched[i] = rightMatched[j] = true;
                    count++;
                    break;
                }
        if (count == 0) return 0;
        var transpositions = 0;
        for (int i = 0, j = 0; i < left.Length; i++)
            if (leftMatched[i])
            {
                while (!rightMatched[j]) j++;
                if (left[i] != right[j++]) transpositions++;
            }
        // strsim 0.11.1 truncates odd transposition counts before computing Jaro.
        var similarity = (count / (double)left.Length + count / (double)right.Length + (count - transpositions / 2) / (double)count) / 3;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(left.Length, right.Length)) && left[prefix] == right[prefix]) prefix++;
        return similarity > 0.7 ? similarity + 0.1 * prefix * (1 - similarity) : similarity;
    }
}
