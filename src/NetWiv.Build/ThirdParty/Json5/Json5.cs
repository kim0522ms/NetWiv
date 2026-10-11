using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Json5;

/// <summary>
/// Provides methods for parsing JSON5 text into System.Text.Json types.
/// </summary>
public static class Json5
{
    // ── JsonNode path (via Json5NodeBuilder) ──────────────────────────

    /// <summary>
    /// Parses a JSON5 string into a <see cref="JsonNode"/>.
    /// </summary>
    public static JsonNode? Parse(string json5, Json5ReaderOptions options = default, JsonNodeOptions? nodeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(json5);
        var utf8 = Encoding.UTF8.GetBytes(json5);
        return Parse(utf8.AsSpan(), options, nodeOptions);
    }

    /// <summary>
    /// Parses JSON5 UTF-8 bytes into a <see cref="JsonNode"/>.
    /// </summary>
    public static JsonNode? Parse(ReadOnlySpan<byte> json5, Json5ReaderOptions options = default, JsonNodeOptions? nodeOptions = null)
        => Json5NodeBuilder.Build(json5, options, nodeOptions ?? new JsonNodeOptions { PropertyNameCaseInsensitive = false });

}
