// Adapted from WebViewNavigationManager.cs at ASP.NET Core commit
// 7413d24330774263a78a4f3837ca53493518455a (.NET 8.0.32), MIT licensed.
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

internal sealed class AotNavigationManager : NavigationManager
{
    private readonly Uri _appBaseUri;
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _send;
    private readonly Action<Exception> _fail;

    internal AotNavigationManager(Uri baseUri, string initialUri, Dispatcher dispatcher, Action<string> send, Action<Exception> fail)
    {
        _appBaseUri = baseUri;
        _dispatcher = dispatcher;
        _send = send;
        _fail = fail;
        Validate(initialUri);
        Initialize(baseUri.AbsoluteUri, initialUri);
    }

    internal void LocationUpdated(string uri, string? state, bool intercepted)
    {
        Validate(uri);
        Uri = uri;
        HistoryEntryState = state;
        NotifyLocationChanged(intercepted);
    }

    internal Task LocationChangingAsync(int callId, string uri, string? state, bool intercepted) =>
        Task.FromException(new NotSupportedException("Navigation locks are not supported by the NativeAOT Blazor host. Use NavigateTo and LocationChanged for static navigation."));

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Validate(ToAbsoluteUri(uri).AbsoluteUri);
        Observe(() =>
        {
            // Never invoke the base handler list: RegisterLocationChangingHandler
            // adds to that list before our unsupported-state hook can reject it.
            _send(AotWire.Message("Navigate", writer =>
            {
                writer.WriteStringValue(uri);
                writer.WriteStartObject();
                // Match the pinned IPC serializer's camelCase NavigationOptions.
                writer.WriteBoolean("forceLoad", options.ForceLoad);
                writer.WriteBoolean("replaceHistoryEntry", options.ReplaceHistoryEntry);
                writer.WriteString("historyEntryState", options.HistoryEntryState);
                writer.WriteEndObject();
            }));
        });
    }

    public override void Refresh(bool forceReload = false) =>
        _send(AotWire.Message("Refresh", writer => writer.WriteBooleanValue(forceReload)));

    protected override void SetNavigationLockState(bool value)
    {
        if (value) throw new NotSupportedException("Navigation locks are not supported by the NativeAOT Blazor host. Use NavigateTo and LocationChanged for static navigation.");
    }

    private void Observe(Action operation) => _ = _dispatcher.InvokeAsync(() =>
    {
        try { operation(); }
        catch (Exception error) { _fail(error); }
    });

    private void Validate(string uri)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var absolute) || absolute.UserInfo.Length != 0 || !_appBaseUri.IsBaseOf(absolute))
            throw new InvalidOperationException("A Blazor Hybrid webview can navigate only within its bundled application origin.");
    }
}

// The official WebView script enables interception without a .NET roundtrip.
internal sealed class AotNavigationInterception : INavigationInterception
{
    public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
}

internal sealed class AotScrollToLocationHash(AotPageServices pageServices) : IScrollToLocationHash
{
    public Task RefreshScrollPositionForHash(string locationAbsolute)
    {
        var hash = locationAbsolute.IndexOf('#');
        return hash >= 0 && hash + 1 < locationAbsolute.Length
            ? ((AotJsRuntime)pageServices.Runtime).InvokeVoidAsync("Blazor._internal.navigationManager.scrollToElement", locationAbsolute[(hash + 1)..])
            : Task.CompletedTask;
    }
}
