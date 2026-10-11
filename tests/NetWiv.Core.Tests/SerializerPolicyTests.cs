using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

internal static class SerializerPolicyTests
{
    internal static Task DirectionalDefaults()
    {
        var reading = JsonValue.Parse("""{"value":2,"included":3}""");
        var expectedRead = JsonSerializer.Deserialize(reading, ReadDefaultsJson.Default.DefaultDirectionalDto)!;
        Assert.Equal(2, expectedRead.Value);
        var read = CommandArgumentJson.Deserialize(reading, ReadDefaultsJson.Default.DefaultDirectionalDto, "policy", "value", false);
        Assert.Equal(2, read.Value);
        Assert.Equal(3, read.Included);
        Assert.Equal("""{"value":2,"included":3}""", JsonSerializer.Serialize(read, ReadDefaultsJson.Default.DefaultDirectionalDto));
        var writing = JsonValue.Parse("""{"value":2,"included":3}""");
        var write = CommandArgumentJson.Deserialize(writing, WriteDefaultsJson.Default.DefaultDirectionalDto, "policy", "value", false);
        Assert.Equal(2, write.Value);
        Assert.Equal("""{"included":3}""", JsonSerializer.Serialize(write, WriteDefaultsJson.Default.DefaultDirectionalDto));
        Assert.Equal("""{"value":2,"included":3}""", JsonSerializer.Serialize(write, WriteMetadataDefaultsJson.Default.DefaultDirectionalDto));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"value":{}}""", ReadDefaultsJson.Default.DefaultDirectionalDto));
        return Task.CompletedTask;
    }

    internal static Task NullableEnumConverter()
    {
        foreach (var (json, expected) in new[] {
            ("""{"state":null}""", """{"state":null}"""),
            ("""{"state":"in-progress"}""", """{"state":"in-progress"}"""),
            ("""{"state":1}""", """{"state":"in-progress"}"""),
            ("""{"state":23}""", """{"state":23}""") })
        {
            var value = CommandArgumentJson.Deserialize(JsonValue.Parse(json), NullableEnumJson.Default.NullableEnumDto, "policy", "value", false);
            Assert.Equal(expected, JsonSerializer.Serialize(value, NullableEnumJson.Default.NullableEnumDto));
        }
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"state":"unknown"}"""),
            NullableEnumJson.Default.NullableEnumDto, "policy", "value", false));
        return Task.CompletedTask;
    }

    internal static Task IncludedFields()
    {
        var explicitFields = CommandArgumentJson.Deserialize(JsonValue.Parse("""{"wire-field":17,"ignored":{},"state":"in-progress"}"""),
            ExplicitFieldsJson.Default.ExplicitFieldsDto, "fields", "value", false);
        Assert.Equal(17, explicitFields.Included);
        Assert.Equal(7, explicitFields.Ignored);
        Assert.Equal(NullableStatus.Running, explicitFields.State);
        Assert.Equal("""{"wire-field":17,"state":"in-progress"}""", JsonSerializer.Serialize(explicitFields, ExplicitFieldsJson.Default.ExplicitFieldsDto));
        var fields = CommandArgumentJson.Deserialize(JsonValue.Parse("""{"name":"Ada","count":3,"outputOnly":{},"hidden":{}}"""),
            IncludedFieldsJson.Default.IncludedFieldsDto, "fields", "value", false);
        Assert.Equal("Ada", fields.Name);
        Assert.Equal(3, fields.Count);
        Assert.Equal(7, fields.OutputOnly);
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"name":"Ada"}"""),
            IncludedFieldsJson.Default.IncludedFieldsDto, "fields", "value", false));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"count":3}"""),
            IncludedFieldsJson.Default.IncludedFieldsDto, "fields", "value", false));
        return Task.CompletedTask;
    }
}

internal sealed class ExplicitFieldsDto
{
    [JsonInclude, JsonPropertyName("wire-field")] public int Included = 0;
    public int Ignored = 7;
    [JsonInclude, JsonConverter(typeof(JsonStringEnumConverter<NullableStatus>))] public NullableStatus? State = null;
}

internal sealed class IncludedFieldsDto(string name)
{
    public readonly string Name = name;
    public readonly int OutputOnly = 7;
    public required int Count;
    [JsonIgnore] public int Hidden = 0;
}

internal sealed class DefaultDirectionalDto
{
    public int Value { get; set; } = 7;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int Included { get; set; }
}

internal enum NullableStatus { Ready, [JsonStringEnumMemberName("in-progress")] Running }
internal sealed class NullableEnumDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<NullableStatus>))] public NullableStatus? State { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenReading)]
[JsonSerializable(typeof(DefaultDirectionalDto))]
internal partial class ReadDefaultsJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWriting)]
[JsonSerializable(typeof(DefaultDirectionalDto))]
internal partial class WriteDefaultsJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWriting,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DefaultDirectionalDto))]
internal partial class WriteMetadataDefaultsJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NullableEnumDto))]
internal partial class NullableEnumJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExplicitFieldsDto))]
internal partial class ExplicitFieldsJson : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, IncludeFields = true)]
[JsonSerializable(typeof(IncludedFieldsDto))]
internal partial class IncludedFieldsJson : JsonSerializerContext;
