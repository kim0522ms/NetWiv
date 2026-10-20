using System.Text.Json.Serialization;

namespace NetWiv.BlazorHybrid.Contracts;

public sealed record Greeting(string Message, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class SampleJsonContext : JsonSerializerContext;
