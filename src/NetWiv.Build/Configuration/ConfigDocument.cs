using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

public sealed record ConfigDocument(
    string BasePath,
    string ConfigDirectory,
    string ProjectDirectory,
    JsonObject Root,
    IReadOnlyList<string> Sources)
{
    public IReadOnlyList<string> Overlays { get; init; } = [];

    public JsonNode? CliPatch { get; init; }

    public string? String(params string[] path) => Node(path)?.GetValue<string>();

    public bool? Boolean(params string[] path) => Node(path)?.GetValue<bool>();

    public JsonNode? Node(params string[] path)
    {
        JsonNode? current = Root;
        foreach (var part in path)
            current = current?[part];
        return current;
    }

    public IReadOnlyList<string> Strings(params string[] path) =>
        Node(path) is JsonArray values
            ? values.Select(value => value?.GetValue<string>() ?? string.Empty).ToArray()
            : [];
}
