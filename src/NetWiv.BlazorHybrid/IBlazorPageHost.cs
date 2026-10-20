using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid;

// Both hosts share native document identity, origin checks, asset policy and cleanup.
internal interface IBlazorPageHost : IAsyncDisposable
{
    Task InitializeAsync();
    Task ReceiveAsync(Uri source, string message);
    AssetResponse? TryGetResponse(AssetRequest request);
    void Retire();
    bool IsExpectedDisconnect(Exception error);
}

internal sealed class ManagedBlazorPageHost : IBlazorPageHost
{
    private readonly BlazorWebViewManager _manager;
    private readonly BlazorSessionTransport _transport;

    internal ManagedBlazorPageHost(IServiceProvider services, BlazorDispatcher dispatcher,
        BlazorWebViewOptions options, Action<Uri> navigate, Action<string> send, Action<Task> observe)
    {
        _transport = new(send);
        _manager = new(services, dispatcher, options.AppBaseUri, options, navigate, message =>
        {
            var operation = _transport.SendAsync(message);
            if (!operation.IsCompletedSuccessfully) observe(operation);
        });
        _transport.Attach(_manager);
    }

    public Task InitializeAsync() => _manager.InitializeAsync();
    public Task ReceiveAsync(Uri source, string message)
    {
        _transport.Received(message);
        _manager.ReceiveMessage(source, message);
        return Task.CompletedTask;
    }
    public AssetResponse? TryGetResponse(AssetRequest request) => _manager.TryGetResponse(request);
    public void Retire() => _transport.Retire();
    public bool IsExpectedDisconnect(Exception error) => _transport.IsExpectedDisconnect(error);
    public async ValueTask DisposeAsync()
    {
        try { await _transport.DisconnectAsync(); }
        finally { await _manager.DisposeAsync(); }
    }
}
