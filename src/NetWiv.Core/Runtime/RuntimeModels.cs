using System.Text.Json;

namespace NetWiv.Runtime;

public sealed record WindowOptions(
    string Label,
    string Title,
    string Url,
    double Width,
    double Height,
    JsonElement ExtraConfig,
    IReadOnlyList<WebviewOptions>? Webviews = null);

public sealed record WebviewOptions(
    string Label,
    string Url,
    double X,
    double Y,
    double Width,
    double Height,
    JsonElement ExtraConfig);

public sealed record RuntimeMessage(string WebviewLabel, Uri Source, string Json, string? WindowLabel = null, ulong DocumentId = 0);

public sealed record RuntimeDocumentChange(string WebviewLabel, ulong DocumentId, bool Closed);

public sealed record RuntimeEvent(
    string EventName,
    string? Label,
    JsonElement Payload,
    RuntimeEventTarget? Target = null);

public sealed record RuntimeEventTarget(string Kind, string? Label = null)
{
    public static RuntimeEventTarget Any { get; } = new("Any");
    public static RuntimeEventTarget App { get; } = new("App");

    internal static bool Matches(RuntimeEventTarget listener, RuntimeEventTarget emitted)
    {
        if (emitted.Kind == "Any" || listener.Kind == "Any") return true;
        if (emitted.Kind == "App" || listener.Kind == "App") return emitted.Kind == listener.Kind;
        if (!string.Equals(emitted.Label, listener.Label, StringComparison.Ordinal)) return false;
        return emitted.Kind == "AnyLabel" || listener.Kind == "AnyLabel" || emitted.Kind == listener.Kind;
    }
}

/// <summary>Native dispatch metadata. IsAuthorized attests that Core already validated the request.</summary>
public sealed record RuntimeCommandContext(
    string WebviewLabel,
    Uri Source,
    bool IsAuthorized,
    string? WindowLabel = null,
    RuntimeCommandScope? Scope = null,
    ulong DocumentId = 0,
    bool IsBackend = false);

public sealed record RuntimeCommandScope(
    IReadOnlyList<JsonElement> Allow,
    IReadOnlyList<JsonElement> Deny)
{
    public static RuntimeCommandScope Empty { get; } = new([], []);
}

public sealed record AssetRequest(
    Uri Uri,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body = default,
    string? WindowOrigin = null,
    string? WebviewLabel = null,
    string? WindowLabel = null,
    ulong DocumentId = 0);

public sealed record AssetResponse(
    int StatusCode,
    string ContentType,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    // Embedded routing can serve an HTML fallback under a non-HTML request name.
    internal bool? IsHtmlAsset { get; init; }
}
