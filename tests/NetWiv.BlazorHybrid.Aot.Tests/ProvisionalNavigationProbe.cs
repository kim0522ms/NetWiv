using System.Net;
using System.Net.Sockets;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// Hold a real provisional navigation open while the committed document renders,
// then cancel it from the browser without committing a replacement document.
internal sealed class ProvisionalNavigationProbe : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Finished { get; }
    internal string Url { get; }

    internal ProvisionalNavigationProbe()
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/failed-navigation";
        Finished = Serve();
    }

    private async Task Serve()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var reader = new StreamReader(client.GetStream());
            if (string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) return;
            Requested.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        Finished.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
