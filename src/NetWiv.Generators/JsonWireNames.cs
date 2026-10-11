// Adapted from dotnet/runtime v10.0.0 JsonSeparatorNamingPolicy.cs and JsonCamelCaseNamingPolicy.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Globalization;
using System.Text;

namespace NetWiv.Generators;

internal static class JsonWireNames
{
    internal static string ConvertName(string name, int policy)
    {
        if (policy == 0) return name;
        if (policy == 1)
        {
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length && char.IsUpper(chars[i]); i++)
            {
                if (i > 0 && i + 1 < chars.Length && !char.IsUpper(chars[i + 1]))
                {
                    if (chars[i + 1] == ' ') chars[i] = char.ToLowerInvariant(chars[i]);
                    break;
                }
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
            return new string(chars);
        }
        var lowercase = policy is 2 or 4;
        var separator = policy is 2 or 3 ? '_' : '-';
        var output = new StringBuilder();
        var state = 0; // start, upper, lower/digit, space
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            switch (char.GetUnicodeCategory(current))
            {
                case UnicodeCategory.UppercaseLetter:
                    if (state is 2 or 3 || state == 1 && i + 1 < name.Length && char.IsLower(name[i + 1])) output.Append(separator);
                    output.Append(lowercase ? char.ToLowerInvariant(current) : current);
                    state = 1;
                    break;
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == 3) output.Append(separator);
                    output.Append(lowercase ? current : char.ToUpperInvariant(current));
                    state = 2;
                    break;
                case UnicodeCategory.SpaceSeparator:
                    if (state != 0) state = 3;
                    break;
                default:
                    output.Append(current);
                    state = 0;
                    break;
            }
        }
        return output.ToString();
    }
}
