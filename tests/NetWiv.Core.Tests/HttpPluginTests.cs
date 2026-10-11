using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Http;
using NetWiv.Runtime;

internal static class HttpPluginTests
{
    internal static async Task Fetch()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://127.0.0.1:{port}/data";
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new HttpPlugin())
                .Capability(new("main", ["http:default"], Windows: ["main"],
                    PermissionScopes: [new("http:default", [JsonValue.Parse("{\"url\":" + JsonValue.From(url).GetRawText() + "}")], [])])).Build();
            app.Run();
            const string marker = "headers.set('Tauri-Invoke-Key', '";
            var script = runtime.Context!.InitializationScript;
            var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
                ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
            };
            async Task<AssetResponse> Invoke(string command, string payload) => await runtime.Context.InvokeProtocol!(new(
                new Uri("ipc://localhost/plugin%3Ahttp%7C" + command), "POST", headers,
                Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main"));

            var denied = await Invoke("fetch", """{"clientConfig":{"url":"http://127.0.0.1:1/private","method":"GET"}}""");
            Assert.Equal("error", denied.Headers!["Tauri-Response"]);
            Assert.Contains("scope", Encoding.UTF8.GetString(denied.Body.Span));

            var serving = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                var stream = socket.GetStream();
                var read = new byte[4096];
                _ = await stream.ReadAsync(read);
                var content = "hello from NetWiv";
                var response = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(content)}\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n{content}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            });
            var fetch = await Invoke("fetch", "{\"clientConfig\":{\"url\":" + JsonValue.From(url).GetRawText() + ",\"method\":\"GET\"}}");
            Assert.Equal("ok", fetch.Headers!["Tauri-Response"]);
            var rid = JsonDocument.Parse(fetch.Body).RootElement.GetUInt32();
            var send = await Invoke("fetch_send", $"{{\"rid\":{rid}}}");
            Assert.Equal("ok", send.Headers!["Tauri-Response"]);
            var bodyId = JsonDocument.Parse(send.Body).RootElement.GetProperty("rid").GetUInt32();
            var chunk = await Invoke("fetch_read_body", $"{{\"rid\":{bodyId}}}");
            Assert.Equal("ok", chunk.Headers!["Tauri-Response"]);
            Assert.Equal("hello from NetWiv", Encoding.UTF8.GetString(chunk.Body.Span[..^1]));
            Assert.Equal((byte)0, chunk.Body.Span[^1]);
            var end = await Invoke("fetch_read_body", $"{{\"rid\":{bodyId}}}");
            Assert.Equal((byte)1, end.Body.Span[^1]);
            await serving;

            var redirectServer = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                var stream = socket.GetStream();
                _ = await stream.ReadAsync(new byte[4096]);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(
                    "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:1/blocked\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            });
            var second = await Invoke("fetch", "{\"clientConfig\":{\"url\":" + JsonValue.From(url).GetRawText() + ",\"method\":\"GET\"}}");
            var redirectRid = JsonDocument.Parse(second.Body).RootElement.GetUInt32();
            var redirect = await Invoke("fetch_send", $"{{\"rid\":{redirectRid}}}");
            Assert.Equal("error", redirect.Headers!["Tauri-Response"]);
            Assert.Contains("scope", Encoding.UTF8.GetString(redirect.Body.Span));
            await redirectServer;
        }
        finally { listener.Stop(); }
    }
}
