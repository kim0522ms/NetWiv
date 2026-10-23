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
    /// <inheritdoc cref="AppHandle.OnOpened(Func{AppHandle, OpenedEvent, Task})"/>
    public void OnOpened(Func<AppHandle, OpenedEvent, Task> handler) => Handle.OnOpened(handler);
    /// <inheritdoc cref="AppHandle.OnOpened(Func{AppHandle, OpenedEvent, Task})"/>
    public void OnOpened(Action<AppHandle, OpenedEvent> handler) => Handle.OnOpened(handler);
    /// <inheritdoc cref="AppHandle.OnExitRequested(Func{AppHandle, ExitRequestedEvent, Task})"/>
    public void OnExitRequested(Func<AppHandle, ExitRequestedEvent, Task> handler) => Handle.OnExitRequested(handler);
    /// <inheritdoc cref="AppHandle.OnExitRequested(Func{AppHandle, ExitRequestedEvent, Task})"/>
    public void OnExitRequested(Action<AppHandle, ExitRequestedEvent> handler) => Handle.OnExitRequested(handler);
    /// <inheritdoc cref="AppHandle.RequestExitAsync"/>
    public Task<bool> RequestExitAsync(int exitCode = 0, CancellationToken cancellationToken = default) => Handle.RequestExitAsync(exitCode, cancellationToken);
    /// <inheritdoc cref="AppHandle.OnReopen(Func{AppHandle, ReopenEvent, Task})"/>
    public void OnReopen(Func<AppHandle, ReopenEvent, Task> handler) => Handle.OnReopen(handler);
    /// <inheritdoc cref="AppHandle.OnReopen(Func{AppHandle, ReopenEvent, Task})"/>
    public void OnReopen(Action<AppHandle, ReopenEvent> handler) => Handle.OnReopen(handler);

    public void Run()
    {
        if (RunEventLoop() is { } restart)
        {
            Dispose();
            using var process = System.Diagnostics.Process.Start(restart) ?? throw new InvokeException("The application could not restart.");
        }
    }

    /// <summary>Runs the native event loop and returns a pending restart without disposing or relaunching.</summary>
    internal System.Diagnostics.ProcessStartInfo? RunEventLoop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.Run(Handle.CreateRuntimeContext(() =>
        {
            foreach (var setup in _setup) setup(this);
        }));
        return Handle.TakeRestart();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Handle.Dispose();
    }
}
