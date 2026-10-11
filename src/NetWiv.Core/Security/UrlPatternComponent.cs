// Adapted from urlpattern 0.3.0 (MIT), Copyright 2018-2021 the Deno authors.
using System.Text;
using System.Text.RegularExpressions;

namespace NetWiv;

internal sealed class UrlPatternComponent
{
    private sealed record Part(string Value, string Prefix, string Suffix, string Modifier, bool Fixed);
    private readonly Regex _regex;
    private readonly string? _literal;
    private readonly string? _capturePrefix;
    private readonly string _captureSuffix = "";
    private readonly char? _delimiter;
    private readonly bool _allowEmpty;

    internal UrlPatternComponent(string? pattern, Func<string, string> encode, char? delimiter = null, string prefix = "")
    {
        _delimiter = delimiter;
        var tokens = UrlPatternTokens.Parse(pattern ?? "*", strict: true);
        var parts = new List<Part>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pending = new StringBuilder();
        var index = 0;
        var numericName = 0;
        var segment = delimiter is { } d ? $"[^{Regex.Escape(d.ToString())}]+?" : ".+?";
        while (index < tokens.Count)
        {
            var character = Take(UrlTokenKind.Char);
            var name = Take(UrlTokenKind.Name);
            var expression = Take(UrlTokenKind.Regex) ?? (name is null ? Take(UrlTokenKind.Star) : null);
            if (name is not null || expression is not null)
            {
                var partPrefix = character?.Value ?? "";
                if (partPrefix != prefix) { pending.Append(partPrefix); partPrefix = ""; }
                Flush();
                Add(partPrefix, name, expression, "", Modifier());
                continue;
            }
            character ??= Take(UrlTokenKind.Escaped);
            if (character is not null) { pending.Append(character.Value); continue; }
            if (Take(UrlTokenKind.Open) is not null)
            {
                var partPrefix = Text();
                name = Take(UrlTokenKind.Name);
                expression = Take(UrlTokenKind.Regex) ?? (name is null ? Take(UrlTokenKind.Star) : null);
                var suffix = Text();
                Require(UrlTokenKind.Close);
                Add(partPrefix, name, expression, suffix, Modifier());
                continue;
            }
            Flush();
            Require(UrlTokenKind.End);
        }
        // Rust validates the complete regex even when it selects a literal/capture fast path.
        _regex = MakeRegex(Generate(parts));
        var first = 0;
        var last = parts.Count;
        var literalPrefix = first < last && parts[first] is { Fixed: true, Modifier: "" } ? parts[first++].Value : "";
        var literalSuffix = first < last && parts[last - 1] is { Fixed: true, Modifier: "" } ? parts[--last].Value : "";
        if (first == last) _literal = literalPrefix + literalSuffix;
        else if (last - first == 1 && parts[first] is { Fixed: false, Modifier: "" } p && (p.Value == ".*" || p.Value == segment))
        {
            _capturePrefix = literalPrefix + p.Prefix;
            _captureSuffix = p.Suffix + literalSuffix;
            _allowEmpty = p.Value == ".*";
            if (_allowEmpty) _delimiter = null;
        }

        UrlToken? Take(UrlTokenKind kind) => index < tokens.Count && tokens[index].Kind == kind ? tokens[index++] : null;
        void Require(UrlTokenKind kind)
        {
            if (Take(kind) is null) throw new FormatException($"Expected {kind} in URL pattern '{pattern}'.");
        }
        string Modifier() => (Take(UrlTokenKind.Modifier) ?? Take(UrlTokenKind.Star))?.Value ?? "";
        string Text()
        {
            var text = new StringBuilder();
            while ((Take(UrlTokenKind.Char) ?? Take(UrlTokenKind.Escaped)) is { } token) text.Append(token.Value);
            return text.ToString();
        }
        void Flush()
        {
            if (pending.Length == 0) return;
            parts.Add(new(encode(pending.ToString()), "", "", "", true));
            pending.Clear();
        }
        void Add(string partPrefix, UrlToken? name, UrlToken? expression, string suffix, string modifier)
        {
            if (name is null && expression is null && modifier == "") { pending.Append(partPrefix); return; }
            Flush();
            if (name is null && expression is null)
            {
                if (partPrefix != "") parts.Add(new(encode(partPrefix), "", "", modifier, true));
                return;
            }
            var key = name?.Value ?? (numericName++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!names.Add(key)) throw new FormatException($"Duplicate URL pattern parameter '{key}'.");
            var value = expression is null ? segment : expression.Kind == UrlTokenKind.Star ? ".*" : UrlPatternRegex.Translate(expression.Value);
            parts.Add(new(value, encode(partPrefix), encode(suffix), modifier, false));
        }
    }

    internal bool Matches(string value)
    {
        if (_literal is not null) return value == _literal;
        if (_capturePrefix is not null)
        {
            if (!value.StartsWith(_capturePrefix, StringComparison.Ordinal) || !value.EndsWith(_captureSuffix, StringComparison.Ordinal)) return false;
            var length = value.Length - _capturePrefix.Length - _captureSuffix.Length;
            if (length < 0 || (!_allowEmpty && length == 0)) return false;
            return _delimiter is not { } delimiter || !value.AsSpan(_capturePrefix.Length, length).Contains(delimiter);
        }
        return _regex.IsMatch(value);
    }

    private static Regex MakeRegex(string value)
    {
        try
        {
            // No IL generation, backtracking, backreferences or lookaround across an ACL boundary.
            return new Regex(value, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException error) { throw new FormatException("Invalid URL pattern regular expression.", error); }
        catch (NotSupportedException error) { throw new FormatException("Unsupported URL pattern regular expression.", error); }
    }

    private static string Generate(IReadOnlyList<Part> parts)
    {
        var result = new StringBuilder("\\A");
        foreach (var part in parts)
        {
            var modifier = part.Modifier;
            if (part.Fixed)
            {
                result.Append("(?:").Append(Regex.Escape(part.Value)).Append(')').Append(modifier);
                continue;
            }
            var prefix = Regex.Escape(part.Prefix);
            var suffix = Regex.Escape(part.Suffix);
            if (prefix == "" && suffix == "")
                result.Append("(?:").Append(part.Value).Append(')').Append(modifier);
            else if (modifier is "" or "?")
                result.Append("(?:").Append(prefix).Append("(?:").Append(part.Value).Append(')').Append(suffix).Append(')').Append(modifier);
            else
                result.Append("(?:").Append(prefix).Append("(?:").Append(part.Value).Append(")(?:").Append(suffix).Append(prefix)
                    .Append("(?:").Append(part.Value).Append("))*").Append(suffix).Append(')').Append(modifier == "*" ? "?" : "");
        }
        return result.Append("\\z").ToString();
    }
}
