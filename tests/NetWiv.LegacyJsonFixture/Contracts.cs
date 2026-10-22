using System.Text.Json.Serialization;

namespace NetWiv.LegacyJsonFixture;

public sealed record LegacyRequest(string Id, string? Note = null, int Count = 0, int? OptionalCount = null);
public sealed record LegacyOptional(string? Note);
public sealed class LegacyRequired { public required string? Note { get; set; } }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LegacyRequest))]
[JsonSerializable(typeof(LegacyOptional))]
[JsonSerializable(typeof(LegacyRequired))]
[JsonSerializable(typeof(string))]
public partial class LegacyJson : JsonSerializerContext;
