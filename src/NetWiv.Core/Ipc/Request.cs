using System.Collections.ObjectModel;
using System.Text.Json;

namespace NetWiv;

/// <summary>The JSON or binary body supplied by the invoking frontend.</summary>
public sealed class InvokeBody
{
    private InvokeBody(JsonElement json, ReadOnlyMemory<byte> bytes, bool isBinary) { Json = json; Bytes = bytes; IsBinary = isBinary; }
    public bool IsBinary { get; }
    public JsonElement Json { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public static InvokeBody FromJson(JsonElement value) => new(value.Clone(), default, false);
    public static InvokeBody FromBytes(ReadOnlySpan<byte> value) => new(default, value.ToArray(), true);
}

/// <summary>A command's body and HTTP-compatible invocation headers, injected by the command generator.</summary>
public sealed class Request
{
    private readonly InvokeBody _body;
    private readonly IReadOnlyDictionary<string, string> _headers;
    internal Request(InvokeBody body, IReadOnlyDictionary<string, string>? headers = null)
    {
        _body = body;
        _headers = new ReadOnlyDictionary<string, string>(headers is null ? new(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
    }
    public InvokeBody Body() => _body;
    public IReadOnlyDictionary<string, string> Headers() => _headers;
}
