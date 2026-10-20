using System.Text.Json.Serialization;

namespace NetWiv.BlazorHybrid;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(BlazorNavigationRequest))]
internal partial class BlazorJsonContext : JsonSerializerContext;

internal sealed record BlazorNavigationRequest(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("url")] string Url);
