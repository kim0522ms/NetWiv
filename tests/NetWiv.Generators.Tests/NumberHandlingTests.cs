using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class NumberHandlingTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void CommandValidationAgreesWithGeneratedSerializerForEveryFlagCombination(int flags)
    {
        var context = new NumberJsonContext(new JsonSerializerOptions { NumberHandling = (JsonNumberHandling)flags });
        foreach (var json in new[] { "42", "\"42\"", "\"1.5\"", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "null", "true", "\"bad\"", "2147483648" })
        {
            Check(json, context.Int32, false);
            Check(json, context.NullableInt32, true);
            Check(json, context.Double, false);
            Check("[" + json + "]", context.ListInt32, false);
            Check("[" + json + "]", context.ListNullableInt32, false);
            Check("{\"value\":" + json + "}", context.DictionaryStringDouble, false);
            Check("{\"value\":" + json + "}", context.DictionaryStringNullableInt32, false);
            Check("{\"Value\":" + json + ",\"Values\":[" + json + "]}", context.PlainNumbers, false);
        }
        // A containing type's policy applies to its numeric properties, not a nested object's properties.
        foreach (var json in new[]
        {
            "{\"Loose\":\"42\",\"Strict\":42,\"Values\":[\"1\"],\"Nested\":{\"Value\":42}}",
            "{\"Loose\":42,\"Strict\":\"42\"}",
            "{\"Nested\":{\"Value\":\"42\"}}",
            "{\"Loose\":\"bad\"}",
        }) Check(json, context.OverrideNumbers, false);
    }

    [Fact]
    public async Task GeneratedCommandUsesPropertyAndTypePoliciesForInputAndOutput()
    {
        using var app = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("number_policy").Build();
        var result = await app.Handle.InvokeAsync("number_policy", JsonValue.Parse("""
            {"value":{"loose":"42","strict":2,"values":["3"],"nested":{"value":4}}}
            """));
        Assert.Equal("42", result.GetProperty("loose").GetString());
        Assert.Equal(2, result.GetProperty("strict").GetInt32());
        Assert.Equal("3", result.GetProperty("values")[0].GetString());
        Assert.Equal(4, result.GetProperty("nested").GetProperty("value").GetInt32());
        await Assert.ThrowsAsync<InvokeException>(() => app.Handle.InvokeAsync("number_policy",
            JsonValue.Parse("""{"value":{"strict":"2"}}""")).AsTask());
    }

    private static void Check<T>(string json, JsonTypeInfo<T> info, bool nullable)
    {
        using var document = JsonDocument.Parse(json);
        T? expected;
        try { expected = JsonSerializer.Deserialize(document.RootElement, info); }
        catch (JsonException)
        {
            Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(document.RootElement, info, "numbers", "value", nullable));
            return;
        }
        var actual = CommandArgumentJson.Deserialize(document.RootElement, info, "numbers", "value", nullable);
        string expectedJson;
        try { expectedJson = JsonSerializer.Serialize(expected!, info); }
        catch (ArgumentException)
        {
            // AllowReadingFromString alone can read non-finite floats without permitting their output.
            Assert.Throws<ArgumentException>(() => JsonSerializer.Serialize(actual, info));
            return;
        }
        Assert.Equal(expectedJson, JsonSerializer.Serialize(actual, info));
    }
}

public sealed class PlainNumbers
{
    public int Value { get; set; }
    public List<int> Values { get; set; } = [];
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed class OverrideNumbers
{
    public int Loose { get; set; }
    [JsonNumberHandling(JsonNumberHandling.Strict)] public int Strict { get; set; }
    public List<int> Values { get; set; } = [];
    public PlainNumbers Nested { get; set; } = new();
}

public static class NumberCommands
{
    [Command("number_policy")] public static OverrideNumbers Echo(OverrideNumbers value) => value;
}

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<int?>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, int?>))]
[JsonSerializable(typeof(PlainNumbers))]
[JsonSerializable(typeof(OverrideNumbers))]
public partial class NumberJsonContext : JsonSerializerContext;
