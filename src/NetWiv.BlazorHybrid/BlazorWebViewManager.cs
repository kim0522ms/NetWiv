using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid;

internal sealed class BlazorWebViewManager : WebViewManager
{
    private readonly Action<Uri> _navigate;
    private readonly Action<string> _send;
    private readonly Uri _appBaseUri;
    private readonly BlazorRootComponent[] _roots;
    private readonly IDisposable? _ownedFileProvider;

    internal BlazorWebViewManager(IServiceProvider services, Dispatcher dispatcher, Uri appBaseUri,
        BlazorWebViewOptions options, Action<Uri> navigate, Action<string> send)
        : this(services, dispatcher, appBaseUri, options, ResolveFiles(options), navigate, send) { }

    private BlazorWebViewManager(IServiceProvider services, Dispatcher dispatcher, Uri appBaseUri,
        BlazorWebViewOptions options, HostFiles files, Action<Uri> navigate, Action<string> send)
        : base(services, dispatcher, appBaseUri,
            new CompositeFileProvider(BlazorFrameworkFiles.Create(), files.Provider),
            new JSComponentConfigurationStore(), files.HostPage)
    {
        _navigate = navigate;
        _send = send;
        _appBaseUri = appBaseUri;
        _roots = options.RootComponents.ToArray();
        _ownedFileProvider = files.Owned ? (IDisposable)files.Provider : null;
    }

    internal async Task InitializeAsync()
    {
        foreach (var root in _roots)
            await AddRootComponentAsync(root.ComponentType, root.Selector,
                ParameterView.FromDictionary(new Dictionary<string, object?>(root.Parameters)));
    }

    internal void ReceiveMessage(Uri sourceUri, string message)
    {
        if (BlazorAppOrigin.Matches(sourceUri, _appBaseUri)) MessageReceived(sourceUri, message);
    }

    internal AssetResponse? TryGetResponse(AssetRequest request)
    {
        if (!BlazorAppOrigin.Matches(request.Uri, _appBaseUri)) return null;
        if (request.Method is not ("GET" or "HEAD"))
            return new AssetResponse(405, "text/plain", "Method not allowed"u8.ToArray());
        var acceptsHtml = request.Headers.Any(header =>
            header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) &&
            header.Value.Contains("text/html", StringComparison.OrdinalIgnoreCase));
        var allowFallback = acceptsHtml || !Path.HasExtension(request.Uri.AbsolutePath);
        // The pinned provider interprets query strings as part of the file name.
        if (!TryGetResponseContent(request.Uri.GetLeftPart(UriPartial.Path), allowFallback,
            out var status, out _, out var content, out var headers)) return null;
        using (content)
        using (var buffer = new MemoryStream())
        {
            content.CopyTo(buffer);
            var responseHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            // Native responses emit ContentType separately from the remaining headers.
            var contentType = responseHeaders.Remove("Content-Type", out var value) ? value : "application/octet-stream";
            return new AssetResponse(status, contentType, buffer.ToArray(), responseHeaders);
        }
    }

    protected override void NavigateCore(Uri absoluteUri)
    {
        if (!BlazorAppOrigin.Matches(absoluteUri, _appBaseUri))
            throw new InvalidOperationException("A Blazor Hybrid webview can navigate only within its bundled application origin.");
        _navigate(absoluteUri);
    }

    protected override void SendMessage(string message) => _send(message);

    protected override async ValueTask DisposeAsyncCore()
    {
        try { await base.DisposeAsyncCore(); }
        finally { _ownedFileProvider?.Dispose(); }
    }

    private static HostFiles ResolveFiles(BlazorWebViewOptions options)
    {
        EnsureSupportedFrameworkVersion();
        options.Validate();
        if (options.FileProvider is not null)
            return new(options.FileProvider, options.HostPage.Replace('\\', '/'), false);
        var hostPage = Path.GetFullPath(options.HostPage, AppContext.BaseDirectory);
        if (!File.Exists(hostPage)) throw new FileNotFoundException("The Blazor Hybrid host page was not found.", hostPage);
        return new(new PhysicalFileProvider(Path.GetDirectoryName(hostPage)!), Path.GetFileName(hostPage), true);
    }

    internal static void EnsureSupportedFrameworkVersion()
    {
        BlazorFrameworkVersion.Check(typeof(WebViewManager).Assembly);
    }

    private sealed record HostFiles(IFileProvider Provider, string HostPage, bool Owned);
}
