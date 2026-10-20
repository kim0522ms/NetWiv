using System.Text.Json.Serialization;

// The app (src-netwiv) and the frontend serialize with the same types and options, so a
// renamed property or command changes both builds instead of silently breaking the wire.
public sealed record Greeting(string Message, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class AppJsonContext : JsonSerializerContext;
