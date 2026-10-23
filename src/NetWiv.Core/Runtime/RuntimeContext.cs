namespace NetWiv.Runtime;

public sealed record RuntimeContext(
    IReadOnlyList<WindowOptions> Windows,
    string InitializationScript,
    Action<RuntimeMessage> OnMessage,
    Action<RuntimeEvent> OnEvent,
    Func<AssetRequest, AssetResponse?> ResolveAsset,
    Func<uint, object?>? ResolveResource = null,
    Func<string, string?, bool>? HasEventListeners = null,
    Action<RuntimeDocumentChange>? OnDocumentChanged = null,
    Action? OnReady = null,
    ImageData? DefaultWindowIcon = null,
    string? LocalDataDirectory = null,
    string? AppLocalDataDirectory = null,
    bool AssetProtocolEnabled = false,
    string? IsolationProtocol = null,
    Func<AssetRequest, ValueTask<AssetResponse>>? InvokeProtocol = null,
    IReadOnlyList<string>? UriSchemeProtocols = null,
    Func<AssetRequest, ValueTask<AssetResponse?>>? ResolveUriScheme = null)
{
    internal bool UsesDefaultAppIdentifier { get; init; }
    /// <summary>Exits after the last window is closed or destroyed. Resident applications require DesktopRuntime.</summary>
    public bool ExitOnLastWindowClose { get; init; } = true;

    /// <summary>Encoded PNG/ICO default icon, used when DefaultWindowIcon is unset.</summary>
    public ReadOnlyMemory<byte>? EncodedDefaultWindowIcon { get; init; }

    /// <summary>
    /// Applies the application's configured response headers and CSP to bundled content
    /// supplied by a runtime integration. Apply once before returning that content;
    /// responses from ResolveAsset already include this policy.
    /// </summary>
    public Func<AssetRequest, AssetResponse, AssetResponse>? ApplyAssetPolicy { get; init; }
}
