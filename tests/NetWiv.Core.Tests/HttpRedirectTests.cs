using System.Net;
using System.Net.Sockets;
using System.Text;
using NetWiv;
using NetWiv.Plugin.Http;

internal static class HttpRedirectTests
{
    internal static async Task CredentialsStayOnOrigin()
    {
        foreach (var destination in new[] { "same", "port", "host" })
        {
            var first = new TcpListener(IPAddress.Loopback, 0);
            var second = new TcpListener(IPAddress.Loopback, 0);
            first.Start(); second.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var port = ((IPEndPoint)first.LocalEndpoint).Port;
                var otherPort = ((IPEndPoint)second.LocalEndpoint).Port;
                var from = $"http://127.0.0.1:{port}/start";
                var to = destination == "port" ? $"http://127.0.0.1:{otherPort}/end" :
                    $"http://{(destination == "host" ? "localhost" : "127.0.0.1")}:{port}/end";
                var server = Task.Run(async () =>
                {
                    using (var client = await first.AcceptTcpClientAsync(timeout.Token))
                    {
                        _ = await Headers(client.GetStream(), timeout.Token);
                        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 302 Found\r\nLocation: {to}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
                    }
                    using var redirected = await (destination == "port" ? second : first).AcceptTcpClientAsync(timeout.Token);
                    var headers = await Headers(redirected.GetStream(), timeout.Token);
                    await redirected.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), timeout.Token);
                    return headers;
                });
                var runtime = new FakeRuntime();
                using var app = new Builder().UseRuntime(runtime).Plugin(new HttpPlugin())
                    .Capability(new("main", ["http:default"], Windows: ["main"], PermissionScopes:
                    [new("http:default", [JsonValue.Parse("{\"url\":" + JsonValue.From(from).GetRawText() + "}"),
                        JsonValue.Parse("{\"url\":" + JsonValue.From(to).GetRawText() + "}")], [])])).Build();
                app.Run();
                var created = await GuestIpc.Invoke(runtime, "plugin:http|fetch", "{\"clientConfig\":{\"url\":" +
                    JsonValue.From(from).GetRawText() + ",\"headers\":[[\"Authorization\",\"Bearer synthetic\"],[\"Cookie\",\"session=synthetic\"],[\"Proxy-Authorization\",\"Basic synthetic\"],[\"X-Request-Id\",\"audit\"]]}}");
                Assert.Equal("ok", created.Headers!["Tauri-Response"]);
                var sent = await GuestIpc.Invoke(runtime, "plugin:http|fetch_send", "{\"rid\":" + GuestIpc.Body(created) + "}");
                Assert.Equal("ok", sent.Headers!["Tauri-Response"]);
                var received = await server;
                foreach (var name in new[] { "Authorization", "Cookie", "Proxy-Authorization" })
                    Assert.True(received.ContainsKey(name) == (destination == "same"), $"{name} redirect policy failed for {destination}.");
                Assert.Equal("audit", received["X-Request-Id"]);
            }
            finally { first.Stop(); second.Stop(); }
        }
    }

    private static async Task<Dictionary<string, string>> Headers(NetworkStream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        _ = await reader.ReadLineAsync(token);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':');
            headers.Add(line[..colon], line[(colon + 1)..].Trim());
        }
        return headers;
    }
}
