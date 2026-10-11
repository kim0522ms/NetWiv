// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text;
using System.Text.Json;

namespace NetWiv;

internal static class CallbackScript
{
    internal const int MinimumParseLength = 10_240;
    private const int MaximumStringLength = (1 << 30) - 2;

    internal static string Serialize(JsonElement value)
    {
        var json = value.GetRawText();
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return json;
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes <= MinimumParseLength) return json;
        long escapedSize = bytes + 14L;
        foreach (var character in json) if (character is '\\' or '\'') escapedSize++;
        return escapedSize < MaximumStringLength ? EscapeJson(json) : json;
    }

    internal static string EscapeJson(string json) => "JSON.parse('" + json.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal) + "')";
}
