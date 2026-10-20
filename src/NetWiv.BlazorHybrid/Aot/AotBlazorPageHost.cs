using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot.Hosting;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot;

internal sealed class AotBlazorPageHost : IBlazorPageHost
{
    private readonly AotWebViewSession _session;
    private readonly AotStaticFiles _files;
    private volatile bool _retired;

    internal AotBlazorPageHost(IServiceProvider services, BlazorDispatcher dispatcher,
        BlazorWebViewOptions options, BlazorAotOptions aot, Action<string> send, Action<Exception> fail)
    {
        _files = new(options);
        try { _session = new(services, dispatcher, options.AppBaseUri, options, aot, send, fail); }
        catch
        {
            _files.Dispose();
            throw;
        }
    }

    public Task InitializeAsync() => _session.InitializeAsync();
    public Task ReceiveAsync(Uri source, string message) => _session.ReceiveAsync(source, message);
    public AssetResponse? TryGetResponse(AssetRequest request) => _files.TryGetResponse(request);
    public void Retire()
    {
        _retired = true;
        _session.Disconnect();
    }
    public bool IsExpectedDisconnect(Exception error) =>
        _retired && (error is JSDisconnectedException or TaskCanceledException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0 &&
            aggregate.InnerExceptions.All(IsExpectedDisconnect));

    public async ValueTask DisposeAsync()
    {
        try { await _session.DisposeAsync(); }
        finally { _files.Dispose(); }
    }
}
