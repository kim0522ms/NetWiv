using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv;

internal static class PropertyInputTests
{
    public static Task IgnoredMembers()
    {
        Check("""{"ignored":{"unexpected":true},"computed":[],"privateSetter":false,"value":7}""",
            PropertyInputContext.Default.PropertyInputDto);
        Check("{}", PropertyInputContext.Default.IgnoredConstructorDto);
        Check("""{"name":{"unexpected":true}}""", PropertyInputContext.Default.IgnoredConstructorDto);
        return Task.CompletedTask;
    }

    public static Task WritableAndPopulatedMembers()
    {
        var writeOnly = Check("""{"input":17}""", PropertyInputContext.Default.WriteOnlyDto);
        Assert.Equal(17, writeOnly.Stored);
        var populated = Check("""{"values":[2,3]}""", PropertyInputContext.Default.PopulatedDto);
        Assert.Equal("1,2,3", string.Join(',', populated.Values));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(
            JsonValue.Parse("""{"values":["bad"]}"""), PropertyInputContext.Default.PopulatedDto, "shape", "input", false));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(
            JsonValue.Parse("""{"input":"bad"}"""), PropertyInputContext.Default.WriteOnlyDto, "shape", "input", false));
        return Task.CompletedTask;
    }

    public static Task DirectionalIgnore()
    {
        var value = Check("""{"input":17,"output":{"ignored":true}}""", PropertyInputContext.Default.DirectionalDto);
        Assert.Equal(17, value.Input);
        Assert.Equal(23, value.Output);
        Assert.Equal("""{"output":23}""", JsonSerializer.Serialize(value, PropertyInputContext.Default.DirectionalDto));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(
            JsonValue.Parse("""{"input":"bad"}"""), PropertyInputContext.Default.DirectionalDto, "shape", "input", false));
        var constructed = Check("""{"name":"constructor input"}""", PropertyInputContext.Default.DirectionalConstructorDto);
        Assert.Equal("constructor input", constructed.Name);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"name":{}}""", PropertyInputContext.Default.DirectionalConstructorDto));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(
            JsonValue.Parse("""{"name":{}}"""), PropertyInputContext.Default.DirectionalConstructorDto, "shape", "input", false));
        return Task.CompletedTask;
    }

    private static T Check<T>(string json, JsonTypeInfo<T> info)
    {
        var value = JsonValue.Parse(json);
        var expected = JsonSerializer.Deserialize(value, info)!;
        var actual = CommandArgumentJson.Deserialize(value, info, "shape", "input", false);
        Assert.Equal(JsonSerializer.Serialize(expected, info), JsonSerializer.Serialize(actual, info));
        return actual;
    }
}

internal sealed class PropertyInputDto
{
    [JsonIgnore] public int Ignored { get; set; }
    public int Computed => 42;
    public int PrivateSetter { get; private set; } = 23;
    public int Value { get; set; }
}

internal sealed record IgnoredConstructorDto([property: JsonIgnore] string Name);

internal sealed class WriteOnlyDto
{
    public int Stored { get; private set; }
    public int Input { set => Stored = value; }
}

internal sealed class PopulatedDto
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<int> Values { get; } = [1];
}

internal sealed class DirectionalDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public int Input { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public int Output { get; set; } = 23;
}

internal sealed record DirectionalConstructorDto([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] string Name);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PropertyInputDto))]
[JsonSerializable(typeof(IgnoredConstructorDto))]
[JsonSerializable(typeof(WriteOnlyDto))]
[JsonSerializable(typeof(PopulatedDto))]
[JsonSerializable(typeof(DirectionalDto))]
[JsonSerializable(typeof(DirectionalConstructorDto))]
internal partial class PropertyInputContext : JsonSerializerContext;
