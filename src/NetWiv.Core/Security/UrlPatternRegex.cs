using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NetWiv;

// URL matching components are ASCII after WHATWG percent/IDNA encoding. Compile
// Rust character-set operations over that alphabet rather than interpreting the
// same bracket syntax with .NET's different union/subtraction rules.
internal static class UrlPatternRegex
{
    internal static string Translate(string expression)
    {
        var index = 0;
        var result = new StringBuilder();
        while (index < expression.Length)
        {
            if (expression[index] == '[') result.Append(Emit(Class()));
            else if (expression[index] == '\\') result.Append(Escape());
            else result.Append(expression[index++]);
        }
        return result.ToString();

        HashSet<int> Class()
        {
            index++;
            var negate = index < expression.Length && expression[index] == '^';
            if (negate) index++;
            var left = Union();
            while (index + 1 < expression.Length && expression[index] == expression[index + 1] && expression[index] is '&' or '-' or '~')
            {
                var operation = expression[index];
                index += 2;
                var right = Union();
                if (operation == '&') left.IntersectWith(right);
                else if (operation == '-') left.ExceptWith(right);
                else left.SymmetricExceptWith(right);
            }
            if (index == expression.Length || expression[index++] != ']') throw new FormatException("Unclosed URL pattern character class.");
            if (!negate) return left;
            var complement = Enumerable.Range(0, 128).ToHashSet();
            complement.ExceptWith(left);
            return complement;
        }

        HashSet<int> Union()
        {
            var union = new HashSet<int>();
            var first = true;
            while (index < expression.Length)
            {
                if (!first && expression[index] == ']') break;
                if (index + 1 < expression.Length && expression[index] == expression[index + 1] && expression[index] is '&' or '-' or '~') break;
                first = false;
                var atom = Atom();
                if (index + 1 < expression.Length && expression[index] == '-' && expression[index + 1] is not ('-' or ']'))
                {
                    index++;
                    var end = Atom();
                    if (atom.Scalar is not { } startScalar || end.Scalar is not { } endScalar || startScalar > endScalar) throw new FormatException("Invalid URL pattern character range.");
                    union.UnionWith(Enumerable.Range(0, 128).Where(c => c >= startScalar && c <= endScalar));
                }
                else union.UnionWith(atom.Values);
            }
            return union;
        }

        (HashSet<int> Values, int? Scalar) Atom()
        {
            if (index >= expression.Length) throw new FormatException("Unclosed URL pattern character class.");
            if (expression.AsSpan(index).StartsWith("[:", StringComparison.Ordinal))
            {
                var end = expression.IndexOf(":]", index + 2, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("Unclosed URL pattern POSIX class.");
                var name = expression[(index + 2)..end];
                index = end + 2;
                var negate = name.StartsWith('^');
                if (negate) name = name[1..];
                Func<int, bool> predicate = name switch
                {
                    "alnum" => c => char.IsAsciiLetterOrDigit((char)c), "alpha" => c => char.IsAsciiLetter((char)c),
                    "ascii" => _ => true, "blank" => c => c is 9 or 32, "cntrl" => c => c < 32 || c == 127,
                    "digit" => c => char.IsAsciiDigit((char)c), "graph" => c => c is >= 33 and <= 126,
                    "lower" => c => c is >= 'a' and <= 'z', "print" => c => c is >= 32 and <= 126,
                    "punct" => c => c is >= 33 and <= 126 && !char.IsAsciiLetterOrDigit((char)c),
                    "space" => c => c is >= 9 and <= 13 or 32, "upper" => c => c is >= 'A' and <= 'Z',
                    "word" => c => char.IsAsciiLetterOrDigit((char)c) || c == '_', "xdigit" => c => char.IsAsciiHexDigit((char)c),
                    _ => throw new FormatException("Invalid URL pattern POSIX class.")
                };
                return (Enumerable.Range(0, 128).Where(c => predicate(c) != negate).ToHashSet(), null);
            }
            if (expression[index] == '[') return (Class(), null);
            if (expression[index] != '\\') { var literal = expression[index++]; return ([literal], literal); }
            if (index + 1 < expression.Length && expression[index + 1] is 'b' or 'B' or 'A' or 'z')
                throw new FormatException("A Rust regex assertion is invalid inside a character class.");
            var value = Escape();
            var regex = new Regex("\\A(?:" + value + ")\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            var values = Enumerable.Range(0, 128).Where(c => regex.IsMatch(((char)c).ToString())).ToHashSet();
            int? scalar = values.Count == 1 ? values.Single() : null;
            if (value.StartsWith("\\u", StringComparison.Ordinal) && value.Length == 6) scalar = int.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (values, scalar);
        }

        string Escape()
        {
            var start = index++;
            if (index == expression.Length) throw new FormatException("Incomplete URL pattern regex escape.");
            var type = expression[index++];
            if (type is 'Z' or 'G' or 'k' or 'c' || char.IsAsciiDigit(type)) throw new FormatException("Invalid Rust regular expression escape.");
            if (type is 'x' or 'u' or 'U')
            {
                var braces = index < expression.Length && expression[index] == '{';
                if (braces) index++;
                var hexStart = index;
                if (braces) { while (index < expression.Length && expression[index] != '}') index++; }
                else index += type == 'x' ? 2 : type == 'u' ? 4 : 8;
                if (index > expression.Length || braces && index == expression.Length
                    || !int.TryParse(expression.AsSpan(hexStart, index - hexStart), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scalar)
                    || !Rune.IsValid(scalar)) throw new FormatException("Invalid URL pattern hexadecimal escape.");
                if (braces) index++;
                return scalar <= 0xffff ? "\\u" + scalar.ToString("X4", CultureInfo.InvariantCulture) : "[^\\x00-\\x7f]";
            }
            if (type is 'p' or 'P' && index < expression.Length && expression[index] == '{')
            {
                var end = expression.IndexOf('}', index);
                if (end < 0) throw new FormatException("Unclosed URL pattern Unicode property.");
                var property = expression[(index + 1)..end];
                index = end + 1;
                if (property is "ASCII" or "Any" or "Assigned") return type == 'p' ? "[\\x00-\\x7f]" : "[^\\x00-\\x7f]";
                // .NET block names such as IsBasicLatin are not Rust Unicode properties.
                if (property is not ("L" or "Lu" or "Ll" or "Lt" or "Lm" or "Lo" or "M" or "Mn" or "Mc" or "Me"
                    or "N" or "Nd" or "Nl" or "No" or "P" or "Pc" or "Pd" or "Ps" or "Pe" or "Pi" or "Pf" or "Po"
                    or "S" or "Sm" or "Sc" or "Sk" or "So" or "Z" or "Zs" or "Zl" or "Zp" or "C" or "Cc" or "Cf" or "Co" or "Cn"))
                    throw new FormatException($"Unsupported Rust Unicode property '{property}' in URL pattern.");
            }
            return expression[start..index];
        }
    }

    private static string Emit(HashSet<int> values)
    {
        if (values.Count == 0) return "[^\\x00-\\x7f]";
        var result = new StringBuilder("[");
        for (var i = 0; i < 128; i++)
        {
            if (!values.Contains(i)) continue;
            var start = i;
            while (i + 1 < 128 && values.Contains(i + 1)) i++;
            result.Append("\\x").Append(start.ToString("X2", CultureInfo.InvariantCulture));
            if (i > start) result.Append("-\\x").Append(i.ToString("X2", CultureInfo.InvariantCulture));
        }
        return result.Append(']').ToString();
    }
}
