using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Websocket;
using NetWiv.Runtime;

internal static class WebsocketPluginTests
{
    internal static async Task LocalEcho()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var server = EchoTwo(listener);
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new WebsocketPlugin())
                .Capability(new("main", ["websocket:default"], Windows: ["main"])).Build();
            app.Run();
            const string marker = "headers.set('Tauri-Invoke-Key', '";
            var script = runtime.Context!.InitializationScript;
            var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
                ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
            };
            async Task<AssetResponse> Invoke(string name, string payload) => await runtime.Context.InvokeProtocol!(new(
                new Uri("ipc://localhost/plugin%3Awebsocket%7C" + name), "POST", headers,
                Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main"));
            var url = $"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var connect = await Invoke("connect", "{\"url\":" + JsonValue.From(url).GetRawText() +
                ",\"onMessage\":\"__CHANNEL__:31\"}");
            Assert.Equal("ok", connect.Headers!["Tauri-Response"]);
            var id = JsonDocument.Parse(connect.Body).RootElement.GetInt32();
            var send = await Invoke("send", $"{{\"id\":{id},\"message\":{{\"type\":\"Text\",\"data\":\"hello\"}}}}");
            Assert.Equal("ok", send.Headers!["Tauri-Response"]);
            var received = await runtime.NextScript();
            Assert.Contains("Text", received);
            Assert.Contains("hello", received);
            var binary = await Invoke("send", $"{{\"id\":{id},\"message\":{{\"type\":\"Binary\",\"data\":[0,127,255]}}}}");
            Assert.Equal("ok", binary.Headers!["Tauri-Response"]);
            var receivedBinary = await runtime.NextScript();
            Assert.Contains("Binary", receivedBinary);
            Assert.Contains("[0,127,255]", receivedBinary);
            var echoed = await server;
            Assert.Equal("hello", Encoding.UTF8.GetString(echoed[0]));
            Assert.True(echoed[1].SequenceEqual(new byte[] { 0, 127, 255 }), "Binary frame changed in transit.");
            runtime.Context.OnDocumentChanged!(new("main", 1, false));
            var stale = await Invoke("send", $"{{\"id\":{id},\"message\":{{\"type\":\"Text\",\"data\":\"stale\"}}}}");
            Assert.Equal("error", stale.Headers!["Tauri-Response"]);
        }
        finally { listener.Stop(); }
    }

    private static async Task<byte[][]> EchoTwo(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? key = null;
        while (await reader.ReadLineAsync() is { Length: > 0 } line)
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)) key = line.Split(':', 2)[1].Trim();
        if (key is null) throw new Exception("WebSocket handshake key was missing.");
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
        await stream.WriteAsync(response);
        var messages = new byte[2][];
        for (var number = 0; number < messages.Length; number++)
        {
            var header = new byte[2];
            await stream.ReadExactlyAsync(header);
            var length = header[1] & 0x7f;
            Assert.True((header[1] & 0x80) != 0 && length < 126, "Client WebSocket did not mask its short frame.");
            var mask = new byte[4];
            await stream.ReadExactlyAsync(mask);
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload);
            for (var index = 0; index < payload.Length; index++) payload[index] ^= mask[index % 4];
            messages[number] = payload;
            byte[] frame = [(byte)(0x80 | (header[0] & 0x0f)), (byte)length, .. payload];
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
        }
        await Task.Delay(100);
        return messages;
    }
}
