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
    Func<AssetRequest, ValueTask<AssetResponse?>>? ResolveUriScheme = null);
