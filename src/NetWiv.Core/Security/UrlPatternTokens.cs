// Adapted from urlpattern 0.3.0 (MIT), Copyright 2018-2021 the Deno authors.
using System.Globalization;
using System.Text;

namespace NetWiv;

internal enum UrlTokenKind { Open, Close, Regex, Name, Char, Escaped, Modifier, Star, End, Invalid }
internal sealed record UrlToken(UrlTokenKind Kind, int Index, string Value);

internal static class UrlPatternTokens
{
    internal static List<UrlToken> Parse(string input, bool strict)
    {
        var tokens = new List<UrlToken>();
        for (var i = 0; i < input.Length;)
        {
            var start = i++;
            var c = input[start];
            var kind = c switch
            {
                '*' => UrlTokenKind.Star, '+' or '?' => UrlTokenKind.Modifier,
                '{' => UrlTokenKind.Open, '}' => UrlTokenKind.Close, _ => UrlTokenKind.Char
            };
            if (c == '\\')
            {
                if (i == input.Length) { Invalid(start, "Incomplete escape"); continue; }
                var length = Rune.GetRuneAt(input, i).Utf16SequenceLength;
                tokens.Add(new(UrlTokenKind.Escaped, start, input.Substring(i, length)));
                i += length;
                continue;
            }
            if (c == ':')
            {
                var nameStart = i;
                while (i < input.Length && IsName(Rune.GetRuneAt(input, i), i == nameStart))
                    i += Rune.GetRuneAt(input, i).Utf16SequenceLength;
                if (i == nameStart) { Invalid(start, "Missing parameter name"); continue; }
                tokens.Add(new(UrlTokenKind.Name, start, input[nameStart..i]));
                continue;
            }
            if (c == '(')
            {
                var depth = 1;
                var regexStart = i;
                var valid = true;
                while (i < input.Length)
                {
                    c = input[i++];
                    if (c > 127 || (i == regexStart + 1 && c == '?')) { valid = false; break; }
                    if (c == '\\')
                    {
                        if (i == input.Length || input[i++] > 127) { valid = false; break; }
                        continue;
                    }
                    if (c == ')') depth--;
                    if (c == ')' && depth == 0) break;
                    if (c == '(')
                    {
                        depth++;
                        if (i == input.Length || input[i] != '?') { valid = false; break; }
                    }
                }
                if (!valid || depth != 0 || i == regexStart + 1)
                {
                    i = regexStart;
                    Invalid(start, "Invalid regular expression group");
                    continue;
                }
                tokens.Add(new(UrlTokenKind.Regex, start, input[regexStart..(i - 1)]));
                continue;
            }
            if (char.IsHighSurrogate(c) && i < input.Length && char.IsLowSurrogate(input[i])) i++;
            tokens.Add(new(kind, start, input[start..i]));
        }
        tokens.Add(new(UrlTokenKind.End, input.Length, ""));
        return tokens;

        void Invalid(int index, string message)
        {
            if (strict) throw new FormatException($"{message} at URL pattern offset {index}.");
            tokens.Add(new(UrlTokenKind.Invalid, index, input[index..(index + 1)]));
        }
    }

    private static bool IsName(Rune rune, bool first)
    {
        if (rune.Value is '$' or '_') return true;
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber) return true;
        if (rune.Value is 0x1885 or 0x1886 or 0x2118 or 0x212e or 0x309b or 0x309c) return true;
        return !first && (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation
            || rune.Value is 0xb7 or 0x387 or 0x1369 or 0x136a or 0x136b or 0x136c or 0x136d
                or 0x136e or 0x136f or 0x1370 or 0x1371 or 0x19da or 0x200c or 0x200d);
    }
}
