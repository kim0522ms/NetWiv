using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class OriginProbe : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _server;
    private readonly List<Task> _clients = [];
    internal readonly TaskCompletionSource<bool> RemoteDenied = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string Url { get; }

    internal OriginProbe()
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _server = Serve();
    }

    internal static async Task CheckIdleConnectionAsync()
    {
        var server = new OriginProbe();
        using var idle = new TcpClient();
        try
        {
            using (var abandoned = new TcpClient())
                await abandoned.ConnectAsync(IPAddress.Loopback, new Uri(server.Url).Port);
            // Browsers may preconnect without sending an HTTP request.
            await idle.ConnectAsync(IPAddress.Loopback, new Uri(server.Url).Port);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string page = await http.GetStringAsync(server.Url);
            if (!page.Contains("netwiv-origin-probe", StringComparison.Ordinal))
                throw new InvalidOperationException("Origin probe did not serve a request alongside an idle connection.");
            await http.GetStringAsync(server.Url + "/result?denied=false");
            if (await server.RemoteDenied.Task.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("Origin probe reported a failed denial as success.");
        }
        finally { server.Dispose(); } // Must also stop while the idle socket is still open.
        Console.WriteLine("PASS origin probe serves concurrent requests and cancels idle connections");
    }

    private async Task Serve()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _clients.Add(ServeClient(client));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { RemoteDenied.TrySetException(error); }
        finally { await Task.WhenAll(_clients); }
    }

    private async Task ServeClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                string? request = await reader.ReadLineAsync(_stop.Token);
                if (string.IsNullOrEmpty(request)) return;
                string? line;
                do { line = await reader.ReadLineAsync(_stop.Token); } while (!string.IsNullOrEmpty(line));
                string route = request.Split(' ').ElementAtOrDefault(1) ?? "/";
                if (route.StartsWith("/result?", StringComparison.Ordinal))
                    RemoteDenied.TrySetResult(route == "/result?denied=true");
                string page = route.StartsWith("/top", StringComparison.Ordinal) ? TopPage : FramePage;
                byte[] body = Encoding.UTF8.GetBytes(page);
                byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { } // A speculative browser connection may close without a request.
            catch (Exception error) { RemoteDenied.TrySetException(error); }
        }
    }

    private const string FramePage = """
        <!doctype html><meta charset="utf-8"><script>
        (() => {
          let parentReadable = false;
          try { parentReadable = !!parent.__TAURI_INTERNALS__; } catch {}
          const bridge = typeof window.__TAURI_INTERNALS__?.invoke === 'function';
          try {
            window.ipc?.postMessage(JSON.stringify({cmd:'sensitive',callback:991,error:992,payload:{},__NETWIV_INVOKE_KEY__:'forged'}));
          } catch {}
          parent.postMessage({kind:'netwiv-origin-probe',bridge,parentReadable}, '*');
        })();
        </script>
        """;

    private const string TopPage = """
        <!doctype html><meta charset="utf-8"><h1>Remote origin check</h1><pre id="result">Testing…</pre><script>
        (async () => {
          let denied = false;
          try { await window.__TAURI_INTERNALS__.invoke('sensitive'); }
          catch (error) { denied = String(error).includes('not allowed'); }
          document.querySelector('#result').textContent = denied ? 'PASS remote command denied' : 'FAIL remote command allowed or unavailable';
          await fetch('/result?denied=' + denied);
        })();
        </script>
        """;

    public void Dispose()
    {
        _stop.Cancel(); _listener.Stop();
        _server.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
