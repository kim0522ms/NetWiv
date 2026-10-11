// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv;

internal static class CallbackScriptTests
{
    internal static Task SerializeJs()
    {
        Equal("null", CallbackScript.Serialize(JsonValue.Null));
        Equal("5", CallbackScript.Serialize(JsonValue.From(5)));
        var text = new string('T', CallbackScript.MinimumParseLength);
        Equal('"' + text + '"', CallbackScript.Serialize(JsonValue.From(text)));
        Equal("JSON.parse('{\"value\":\"" + text + "\"}')", CallbackScript.Serialize(JsonValue.Parse("{\"value\":\"" + text + "\"}")));
        QuotedAndDangerous(text);
        return Task.CompletedTask;
    }

    internal static Task SerializeJsRaw()
    {
        foreach (var raw in new[] { "null", "5", "{ \"x\": [1, 2, 3] }" }) Equal(raw, CallbackScript.Serialize(JsonValue.Parse(raw)));
        var text = new string('T', CallbackScript.MinimumParseLength);
        Equal('"' + text + '"', CallbackScript.Serialize(JsonValue.Parse('"' + text + '"')));
        Equal("JSON.parse('{\"value\":\"" + text + "\"}')", CallbackScript.Serialize(JsonValue.Parse("{\"value\":\"" + text + "\"}")));
        QuotedAndDangerous(text);
        return Task.CompletedTask;
    }

    internal static Task ByteBoundaries()
    {
        foreach (var (prefix, suffix) in new[] { ("{\"v\":\"", "\"}"), ("[\"", "\"]") })
        {
            var exact = prefix + new string('x', CallbackScript.MinimumParseLength - prefix.Length - suffix.Length) + suffix;
            Equal(exact, CallbackScript.Serialize(JsonValue.Parse(exact)));
            var larger = prefix + new string('x', CallbackScript.MinimumParseLength - prefix.Length - suffix.Length + 1) + suffix;
            Equal(CallbackScript.EscapeJson(larger), CallbackScript.Serialize(JsonValue.Parse(larger)));
        }
        var unicode = "{\"v\":\"" + new string('한', 4000) + "\"}";
        Equal(CallbackScript.EscapeJson(unicode), CallbackScript.Serialize(JsonValue.Parse(unicode)));
        return Task.CompletedTask;
    }

    private static void QuotedAndDangerous(string text)
    {
        var raw = "{\"value\":\"\\\"" + text + "\\\"\"}";
        Equal("JSON.parse('{\"value\":\"\\\\\"" + text + "\\\\\"\"}')", CallbackScript.Serialize(JsonValue.Parse(raw)));
        const string dangerous = """{"test":"don\\🚀🐱‍👤\\'t forget to escape me!🚀🐱‍👤","te🚀🐱‍👤st2":"don't forget to escape me!","test3":"\\🚀🐱‍👤\\\\'''\\\\🚀🐱‍👤\\\\🚀🐱‍👤\\'''''"}""";
        const string escaped = """JSON.parse('{"test":"don\\\\🚀🐱‍👤\\\\\'t forget to escape me!🚀🐱‍👤","te🚀🐱‍👤st2":"don\'t forget to escape me!","test3":"\\\\🚀🐱‍👤\\\\\\\\\'\'\'\\\\\\\\🚀🐱‍👤\\\\\\\\🚀🐱‍👤\\\\\'\'\'\'\'"}')""";
        Equal(escaped, CallbackScript.EscapeJson(dangerous));
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual) throw new Exception("Callback JSON serialization differs from the pinned fixture.");
    }
}
