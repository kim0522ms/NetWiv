using System.Text.Json;
using NetWiv;
using NetWiv.LegacyJsonFixture;

internal static class LegacyJsonTests
{
    internal static async Task NullableMembers()
    {
        // Ensure the fixture retains the old metadata rather than silently using STJ 10.
        Assert.True(!LegacyJson.Default.LegacyRequest.Properties.Single(p => p.Name == "note").IsSetNullable,
            "The fixture must use STJ 8 constructor metadata");
        using var app = new Builder().UseRuntime(new FakeRuntime())
            .RegisterCommand("legacy_echo", (_, args, _) =>
            {
                var request = CommandArgumentJson.Deserialize(args.GetProperty("request"), LegacyJson.Default.LegacyRequest, "legacy_echo", "request", false);
                return ValueTask.FromResult(JsonValue.From(request.Note ?? "(no note)"));
            }).Build();
        app.Run();
        foreach (var json in new[] { "{}", "{\"id\":null}", "{\"id\":\"a\"}", "{\"id\":\"a\",\"note\":null}", "{\"id\":\"a\",\"note\":\"x\"}", "{\"id\":\"a\",\"optionalCount\":null}" })
        {
            var expected = JsonSerializer.Deserialize(json, LegacyJson.Default.LegacyRequest)!;
            var actual = CommandArgumentJson.Deserialize(JsonValue.Parse(json), LegacyJson.Default.LegacyRequest, "legacy_echo", "request", false);
            Assert.Equal(JsonSerializer.Serialize(expected, LegacyJson.Default.LegacyRequest), JsonSerializer.Serialize(actual, LegacyJson.Default.LegacyRequest));
            var result = await app.Handle.InvokeAsync("legacy_echo", JsonValue.Parse("{\"request\":" + json + "}"));
            Assert.Equal(expected.Note ?? "(no note)", result.GetString());
        }
        var serialized = JsonSerializer.Serialize(new LegacyRequest("a"), LegacyJson.Default.LegacyRequest);
        Assert.Contains("\"note\":null", serialized);
        Assert.Equal("(no note)", (await app.Handle.InvokeAsync("legacy_echo", JsonValue.Parse("{\"request\":" + serialized + "}"))).GetString());
        Assert.True(CommandArgumentJson.Deserialize(JsonValue.Parse("{}"), LegacyJson.Default.LegacyOptional, "legacy", "request", false).Note is null,
            "Legacy metadata cannot establish a non-nullable constructor requirement");
        CommandArgumentJson.Deserialize(JsonValue.Parse("{\"note\":null}"), LegacyJson.Default.LegacyRequired, "legacy", "request", false);
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("{}"), LegacyJson.Default.LegacyRequired, "legacy", "request", false));
        foreach (var json in new[] { "null", "{\"id\":\"a\",\"note\":42}", "{\"id\":\"a\",\"count\":null}" })
            Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse(json), LegacyJson.Default.LegacyRequest, "legacy", "request", false));
        foreach (var json in new[] {
            """{"requiredNote":null}""",
            """{"requiredNote":null,"request":null,"label":null,"values":null}""",
            """{"requiredNote":"present","request":{"id":null},"values":["value"]}""" })
        {
            var expected = JsonSerializer.Deserialize(json, LegacyJson.Default.LegacyEnvelope)!;
            var actual = CommandArgumentJson.Deserialize(JsonValue.Parse(json), LegacyJson.Default.LegacyEnvelope, "legacy", "envelope", false);
            Assert.Equal(JsonSerializer.Serialize(expected, LegacyJson.Default.LegacyEnvelope), JsonSerializer.Serialize(actual, LegacyJson.Default.LegacyEnvelope));
        }
        foreach (var json in new[] { "{}", """{"requiredNote":null,"request":{"count":null}}""" })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, LegacyJson.Default.LegacyEnvelope));
            Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse(json), LegacyJson.Default.LegacyEnvelope, "legacy", "envelope", false));
        }
    }
}
