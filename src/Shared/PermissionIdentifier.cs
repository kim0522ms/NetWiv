// Ported from tauri-utils/src/acl/identifier.rs at the pinned upstream commit.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text;

namespace NetWiv;

internal sealed record PermissionIdentifier
{
    internal const int MaxLength = 64 - 13 + 1 + 64;
    private readonly int _separator;
    internal string Value { get; }
    internal string Base => _separator < 0 ? Value : Value[(_separator + 1)..];
    internal string? Prefix => _separator < 0 ? null : Value[.._separator];
    private PermissionIdentifier(string value, int separator) => (Value, _separator) = (value, separator);

    internal static PermissionIdentifier Parse(string value)
    {
        const string invalid = "identifiers can only include lowercase ASCII, hyphens which are not leading or trailing, and a single colon if using a prefix";
        if (value.StartsWith("tauri-plugin-", StringComparison.Ordinal)) throw Error("identifiers cannot start with tauri-plugin-");
        if (value.Length == 0) throw Error("identifiers cannot be empty");
        var length = Encoding.UTF8.GetByteCount(value);
        if (length > MaxLength) throw Error($"identifiers cannot be longer than {MaxLength}, found {length}");
        if (!char.IsAsciiLetterOrDigit(value[0])) throw Error(invalid);
        var core = value.StartsWith("core:", StringComparison.Ordinal);
        var separator = -1;
        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index]; var previous = value[index - 1];
            if ((previous == '-' && current == ':') || (previous == ':' && current == '-')) throw Error(invalid);
            if (current == ':')
            {
                if (separator >= 0 && !core) throw Error("identifiers can only include a single separator '58'");
                separator = index;
            }
            else if (!char.IsAsciiLetterOrDigit(current) && current != '-') throw Error(invalid);
        }
        if (value[^1] == ':') throw Error("identifiers cannot have a prefix without a base");
        if (value[^1] == '-') throw Error("identifiers cannot have a trailing hyphen");
        return new(value, separator);

        FormatException Error(string reason) => new($"invalid plugin or permission identifier '{value}': {reason}");
    }
}
