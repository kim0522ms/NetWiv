using System.Text.Json.Serialization;

namespace NetWiv.BlazorHybrid.Aot.Tests;

public sealed class ProbeDto
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public ProbeLeaf?[]? Items { get; set; }
    public ProbeLeaf? Optional { get; set; }
}

public sealed class ProbeLeaf
{
    public string Text { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Value { get; set; }
}

public sealed class UnregisteredDto
{
    public string Value { get; set; } = "";
}
