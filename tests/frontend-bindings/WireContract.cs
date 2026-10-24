using NetWiv;
using System.Text.Json.Serialization;

[assembly: EventPayload("batch-progress", typeof(EventProgress))]

public sealed record EventProgress(int Completed,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long TotalBytes);

[JsonConverter(typeof(JsonStringEnumConverter<WireStatus>))]
public enum WireStatus { Ready, [JsonStringEnumMemberName("in-progress")] Running }

public class WireBase { public virtual string DisplayName { get; set; } = ""; }
public sealed class WireContract : WireBase
{
    private int _input;
    public int Input { set => _input = value; }
    public int Output => _input;
    public int Computed => 42;
    [JsonIgnore] public int Ignored { get; set; }
    [JsonInclude, JsonPropertyName("included-field")] public int IncludedField = 0;
    public int IgnoredField = 7;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public int InputOnly { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public int OutputOnly { get; set; } = 23;
    public int InputOnlyResult => InputOnly;
    public override string DisplayName { get; set; } = "";
    public WireStatus Status { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<WireStatus>))] public WireStatus? NullableStatus { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int OmittedCount { get; set; }
    [JsonPropertyName("exact-name")] public int WireNumber { get; set; }
}

public static class WireCommands
{
    [Command]
    public static WireContract WireProfile(WireContract value) => value;

    [Command]
    public static ConstructorWire ConstructorProfile(ConstructorWire value) => value;

    [Command]
    public static NumberWire NumberProfile(NumberWire value) => value;

    [Command]
    public static ExtensibleWire ExtensionProfile(ExtensibleWire value) => value;
}

public sealed class ExtensibleWire
{
    public required string Name { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

public sealed record ConstructorWire(string Name, int Count, string? Note = null, int Retry = 3);

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
public sealed record NumberWire(int Count,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int Strict,
    List<int?> Values, NestedNumberWire Nested);

public sealed record NestedNumberWire(int Count,
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] double Ratio);
