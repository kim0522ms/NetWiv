// Matching/parser rules adapted from glob 0.3.4.
// Copyright 2014 The Rust Project Developers.
// SPDX-License-Identifier: MIT OR Apache-2.0
using System.Text;

namespace NetWiv;

// Implements glob 0.3.4 Pattern's default matching rules used by pinned Tauri
// capability window/webview selectors. Reference: rust-lang/glob, MIT OR Apache-2.0.
internal sealed class LabelPattern
{
    private readonly Token[] _tokens;
    private readonly GlobMatchOptions _options;

    public LabelPattern(string pattern) : this(pattern, GlobMatchOptions.Default, "capability label") { }

    internal LabelPattern(string pattern, GlobMatchOptions options, string description)
    {
        _options = options;
        var chars = pattern.EnumerateRunes().ToArray();
        var tokens = new List<Token>();
        for (int index = 0; index < chars.Length;)
        {
            var current = chars[index++];
            if (current.Value == '?') tokens.Add(new(Kind.Any));
            else if (current.Value == '*')
            {
                int start = index - 1;
                while (index < chars.Length && chars[index].Value == '*') index++;
                int count = index - start;
                if (count > 2) throw Invalid(pattern, description);
                if (count == 2)
                {
                    if ((start > 0 && !IsSeparator(chars[start - 1])) ||
                        (index < chars.Length && !IsSeparator(chars[index]))) throw Invalid(pattern, description);
                    if (index < chars.Length) index++;
                }
                tokens.Add(new(count == 2 ? Kind.Recursive : Kind.Star));
            }
            else if (current.Value == '[')
            {
                bool negate = index < chars.Length && chars[index].Value == '!';
                if (negate) index++;
                int close = index + 1;
                while (close < chars.Length && chars[close].Value != ']') close++;
                if (close >= chars.Length) throw Invalid(pattern, description);
                var ranges = new List<(int Start, int End)>();
                while (index < close)
                {
                    if (index + 2 < close && chars[index + 1].Value == '-')
                    {
                        ranges.Add((chars[index].Value, chars[index + 2].Value)); index += 3;
                    }
                    else { ranges.Add((chars[index].Value, chars[index].Value)); index++; }
                }
                index = close + 1;
                tokens.Add(new(negate ? Kind.Except : Kind.Within, Ranges: ranges.ToArray()));
            }
            else tokens.Add(new(Kind.Literal, current));
        }
        _tokens = tokens.ToArray();
    }

    public bool Matches(string value)
    {
        var chars = value.EnumerateRunes().ToArray();
        var memo = new Dictionary<(int Token, int Character), bool>();
        return Match(0, 0);

        bool Match(int tokenIndex, int characterIndex)
        {
            if (tokenIndex == _tokens.Length) return characterIndex == chars.Length;
            if (memo.TryGetValue((tokenIndex, characterIndex), out var cached)) return cached;
            var token = _tokens[tokenIndex];
            bool matched = false;
            if (token.Type == Kind.Star)
            {
                for (int end = characterIndex; end <= chars.Length; end++)
                {
                    if (Match(tokenIndex + 1, end)) { matched = true; break; }
                    if (end == chars.Length ||
                        _options.RequireLiteralLeadingDot && IsComponentStart(chars, end) && chars[end].Value == '.' ||
                        _options.RequireLiteralSeparator && IsSeparator(chars[end])) break;
                }
            }
            else if (token.Type == Kind.Recursive)
            {
                if (Match(tokenIndex + 1, characterIndex)) matched = true;
                bool blockedByLeadingDot = false;
                for (int end = characterIndex; !matched && end < chars.Length; end++)
                {
                    if (_options.RequireLiteralLeadingDot && IsComponentStart(chars, end) && chars[end].Value == '.')
                    {
                        blockedByLeadingDot = true;
                        break;
                    }
                    if (IsSeparator(chars[end]) && Match(tokenIndex + 1, end + 1)) matched = true;
                }
                if (!matched && !blockedByLeadingDot) matched = Match(tokenIndex + 1, chars.Length);
            }
            else if (characterIndex < chars.Length)
            {
                var character = chars[characterIndex];
                bool accepted = token.Type switch
                {
                    Kind.Any => !(_options.RequireLiteralSeparator && IsSeparator(character)) &&
                        !(_options.RequireLiteralLeadingDot && IsComponentStart(chars, characterIndex) && character.Value == '.'),
                    Kind.Literal => character == token.Character || IsSeparator(character) && IsSeparator(token.Character),
                    Kind.Within => AcceptsRange(token, character, chars, characterIndex),
                    Kind.Except => AcceptsRange(token, character, chars, characterIndex),
                    _ => false
                };
                matched = accepted && Match(tokenIndex + 1, characterIndex + 1);
            }
            memo[(tokenIndex, characterIndex)] = matched;
            return matched;
        }

        bool AcceptsRange(Token token, Rune character, Rune[] input, int index)
        {
            if (_options.RequireLiteralSeparator && IsSeparator(character) ||
                _options.RequireLiteralLeadingDot && IsComponentStart(input, index) && character.Value == '.') return false;
            var within = token.Ranges!.Any(range => character.Value >= range.Start && character.Value <= range.End);
            return token.Type == Kind.Within ? within : !within;
        }
    }

    private static bool IsComponentStart(Rune[] value, int index) => index == 0 || IsSeparator(value[index - 1]);
    private static bool IsSeparator(Rune value) => value.Value == '/' || Path.DirectorySeparatorChar == '\\' && value.Value == '\\';
    private static ArgumentException Invalid(string pattern, string description) => new($"Invalid {description} glob '{pattern}'.");
    private enum Kind { Literal, Any, Star, Recursive, Within, Except }
    private sealed record Token(Kind Type, Rune Character = default, (int Start, int End)[]? Ranges = null);
}

internal readonly record struct GlobMatchOptions(bool RequireLiteralSeparator, bool RequireLiteralLeadingDot)
{
    public static GlobMatchOptions Default { get; } = new(false, false);
}
