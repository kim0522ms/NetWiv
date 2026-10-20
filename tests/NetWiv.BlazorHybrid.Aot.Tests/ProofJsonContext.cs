using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWiv.BlazorHybrid.Aot.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProbeDto))]
[JsonSerializable(typeof(ProbeDto[]))]
[JsonSerializable(typeof(ProbeLeaf))]
[JsonSerializable(typeof(ProbeLeaf[]))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(object[]))]
internal partial class ProofJsonContext : JsonSerializerContext;
