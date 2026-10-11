namespace NetWiv;

public sealed partial class AppHandle
{
    /// <summary>Queries live native windows instead of the local lifecycle snapshot.</summary>
    public async Task<IReadOnlyList<Window>> GetAllWindowsAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync("plugin:window|get_all_windows", JsonValue.EmptyObject, cancellationToken);
        return result.EnumerateArray().Select(value => new Window(this, value.GetString()!)).ToArray();
    }

    public async Task<Window?> GetWindowAsync(string label, CancellationToken cancellationToken = default) =>
        (await GetAllWindowsAsync(cancellationToken)).FirstOrDefault(window => window.Label == label);

    public async Task<Window?> GetFocusedWindowAsync(CancellationToken cancellationToken = default)
    {
        foreach (var window in await GetAllWindowsAsync(cancellationToken))
            if (await window.IsFocusedAsync(cancellationToken)) return window;
        return null;
    }

    /// <summary>Matches the pinned guest API, which wraps every native window label, including bare windows.</summary>
    public async Task<IReadOnlyList<WebviewWindow>> GetAllWebviewWindowsAsync(CancellationToken cancellationToken = default) =>
        (await GetAllWindowsAsync(cancellationToken)).Select(window => new WebviewWindow(this, window.Label)).ToArray();

    public async Task<WebviewWindow?> GetWebviewWindowAsync(string label, CancellationToken cancellationToken = default) =>
        (await GetAllWebviewWindowsAsync(cancellationToken)).FirstOrDefault(window => window.Label == label);

    public async Task<IReadOnlyList<Webview>> GetAllWebviewsAsync(CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync("plugin:webview|get_all_webviews", JsonValue.EmptyObject, cancellationToken);
        return result.EnumerateArray().Select(value => new Webview(this,
            value.GetProperty("label").GetString()!, value.GetProperty("window_label").GetString()!)).ToArray();
    }

    public async Task<Webview?> GetWebviewAsync(string label, CancellationToken cancellationToken = default) =>
        (await GetAllWebviewsAsync(cancellationToken)).FirstOrDefault(webview => webview.Label == label);
}
