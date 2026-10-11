using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class App : IDisposable
{
    private readonly IDesktopRuntime _runtime;
    private readonly IReadOnlyList<Action<App>> _setup;
    private bool _disposed;

    internal App(
        IDesktopRuntime runtime,
        AppMetadata metadata,
        IReadOnlyList<WindowOptions> windows,
        StateStore state,
        CommandRegistry commands,
        IReadOnlyList<PermissionDefinition> permissions,
        IReadOnlyList<Capability> capabilities,
        IReadOnlySet<string> allowedOrigins,
        bool hasAppCommandManifest,
        Func<AssetRequest, AssetResponse?> assetResolver,
        IReadOnlyList<IPlugin> plugins,
        bool freezePrototype,
        IReadOnlyList<Action<App>> setup,
        IReadOnlyDictionary<string, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>>> uriSchemeProtocols)
    {
        _runtime = runtime;
        _setup = setup;
        Handle = new AppHandle(runtime, metadata, windows, state, commands, permissions, capabilities, allowedOrigins, hasAppCommandManifest, assetResolver, plugins, freezePrototype, uriSchemeProtocols);
    }

    public AppHandle Handle { get; }
    public Window? GetWindow(string label) => Handle.GetWindow(label);
    public Webview? GetWebview(string label) => Handle.GetWebview(label);
    public WebviewWindow? GetWebviewWindow(string label) => Handle.GetWebviewWindow(label);
    public IReadOnlyCollection<Window> Windows => Handle.Windows;
    public IReadOnlyCollection<Webview> Webviews => Handle.Webviews;
    public IReadOnlyCollection<WebviewWindow> WebviewWindows => Handle.WebviewWindows;
    public FileScope AssetProtocolScope() => Handle.AssetProtocolScope();
    public State<T> State<T>() where T : notnull => Handle.State<T>();
    public State<T>? TryState<T>() where T : notnull => Handle.TryState<T>();
    public bool Manage<T>(T value) where T : notnull => Handle.Manage(value);
    public bool Unmanage<T>([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) where T : notnull => Handle.Unmanage(out value);
    public void OnMenuEvent(Action<AppHandle, MenuEvent> handler) => Handle.OnMenuEvent(handler);
    public void OnTrayIconEvent(Action<AppHandle, TrayIconEvent> handler) => Handle.OnTrayIconEvent(handler);

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.Run(Handle.CreateRuntimeContext(() =>
        {
            foreach (var setup in _setup) setup(this);
        }));
        if (Handle.TakeRestart() is { } restart)
        {
            Dispose();
            using var process = System.Diagnostics.Process.Start(restart) ?? throw new InvokeException("The application could not restart.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Handle.Dispose();
    }
}
