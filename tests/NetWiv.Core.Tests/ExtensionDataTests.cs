using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

internal static class ExtensionDataTests
{
    private const string Payload = """{"name":"Ada","extra":42,"nested":{"value":null},"list":[1,null]}""";

    internal static Task RoundTrip()
    {
        var info = ExtensionDataJson.Default.ExtensibleDto;
        var direct = JsonSerializer.Deserialize(Payload, info)!;
        Assert.Equal(42, direct.Extra!["extra"].GetInt32());
        Assert.Equal(Payload, JsonSerializer.Serialize(direct, info));
        var value = CommandArgumentJson.Deserialize(JsonValue.Parse(Payload), info, "extension", "value", false);
        Assert.Equal(Payload, JsonSerializer.Serialize(value, info));
        Assert.Equal("""{"name":"Ada"}""", JsonSerializer.Serialize(
            CommandArgumentJson.Deserialize(JsonValue.Parse("""{"name":"Ada"}"""), info, "extension", "value", false), info));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"name":42,"extra":null}"""),
            info, "extension", "value", false));
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"extra":42}"""),
            info, "extension", "value", false));
        return Task.CompletedTask;
    }

    internal static Task DirectionsAndFields()
    {
        var json = JsonValue.Parse(Payload);
        var getter = CommandArgumentJson.Deserialize(json, ExtensionDataJson.Default.GetterExtensionDto, "extension", "value", false);
        Assert.Equal(0, getter.Extra.Count);
        Assert.Equal("{}", JsonSerializer.Serialize(getter, ExtensionDataJson.Default.GetterExtensionDto));
        var field = CommandArgumentJson.Deserialize(json, ExtensionDataJson.Default.FieldExtensionDto, "extension", "value", false);
        Assert.Equal(42, field.Extra["extra"].GetInt32());
        Assert.Equal(Payload, JsonSerializer.Serialize(field, ExtensionDataJson.Default.FieldExtensionDto));
        var input = CommandArgumentJson.Deserialize(json, ExtensionDataJson.Default.InputExtensionDto, "extension", "value", false);
        Assert.Equal(42, input.Extra!["extra"].GetInt32());
        Assert.Equal("{}", JsonSerializer.Serialize(input, ExtensionDataJson.Default.InputExtensionDto));
        var output = CommandArgumentJson.Deserialize(json, ExtensionDataJson.Default.OutputExtensionDto, "extension", "value", false);
        Assert.True(output.Extra is null, "WhenReading must not collect extension data");
        output.Extra = new() { ["extra"] = JsonValue.Parse("42") };
        Assert.Equal("""{"extra":42}""", JsonSerializer.Serialize(output, ExtensionDataJson.Default.OutputExtensionDto));
        return Task.CompletedTask;
    }
}

internal sealed class ExtensibleDto
{
    public required string Name { get; set; }
    [JsonExtensionData] public IDictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class GetterExtensionDto
{
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; } = new();
}

internal sealed class FieldExtensionDto
{
    [JsonInclude, JsonExtensionData] public readonly Dictionary<string, JsonElement> Extra = new();
}

internal sealed class InputExtensionDto
{
    [JsonExtensionData, JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class OutputExtensionDto
{
    [JsonExtensionData, JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public Dictionary<string, JsonElement>? Extra { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExtensibleDto))]
[JsonSerializable(typeof(GetterExtensionDto))]
[JsonSerializable(typeof(FieldExtensionDto))]
[JsonSerializable(typeof(InputExtensionDto))]
[JsonSerializable(typeof(OutputExtensionDto))]
internal partial class ExtensionDataJson : JsonSerializerContext;
