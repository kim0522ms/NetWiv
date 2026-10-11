using System.Net;
using System.Net.Sockets;
using System.Text;
using NetWiv;
using NetWiv.Plugin.Upload;

internal static class UploadPluginTests
{
    internal static async Task Transfer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var source = Path.GetTempFileName();
        var destination = Path.GetTempFileName();
        File.WriteAllText(source, "file from NetWiv");
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new UploadPlugin()).Build();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/file";
            foreach (var method in new[] { "POST", "PUT", "PATCH" })
            {
                var server = Serve(listener, 200, "uploaded");
                var result = await app.Handle.InvokeAsync("plugin:upload|upload", JsonValue.Parse(
                    "{\"url\":" + JsonValue.From(url).GetRawText() + ",\"filePath\":" + JsonValue.From(source).GetRawText() +
                    ",\"method\":\"" + method + "\",\"onProgress\":\"__CHANNEL__:21\"}"));
                Assert.Equal("uploaded", result.GetString());
                var request = await server;
                Assert.Contains(method + " /file", request.Headers);
                Assert.Equal("file from NetWiv", request.Body);
                Assert.Contains("progressTotal", await runtime.NextScript());
                Assert.Contains("end", await runtime.NextScript());
            }
            var failure = Serve(listener, 400, "bad upload");
            try
            {
                await app.Handle.InvokeAsync("plugin:upload|upload", JsonValue.Parse(
                    "{\"url\":" + JsonValue.From(url).GetRawText() + ",\"filePath\":" + JsonValue.From(source).GetRawText() +
                    ",\"onProgress\":\"__CHANNEL__:22\"}"));
                throw new Exception("HTTP 400 upload was accepted.");
            }
            catch (InvokeException error) { Assert.Contains("400", error.Message); }
            await failure;
            _ = await runtime.NextScript();
            _ = await runtime.NextScript();

            var download = Serve(listener, 200, "downloaded");
            await app.Handle.InvokeAsync("plugin:upload|download", JsonValue.Parse(
                "{\"url\":" + JsonValue.From(url).GetRawText() + ",\"filePath\":" + JsonValue.From(destination).GetRawText() +
                ",\"onProgress\":\"__CHANNEL__:23\"}"));
            Assert.Equal("downloaded", File.ReadAllText(destination));
            Assert.Contains("progressTotal", await runtime.NextScript());
            Assert.Contains("end", await runtime.NextScript());
            Assert.Contains("GET /file", (await download).Headers);
        }
        finally { listener.Stop(); File.Delete(source); File.Delete(destination); }
    }

    private static async Task<(string Headers, string Body)> Serve(TcpListener listener, int status, string content)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var headerBytes = new MemoryStream();
        var last = new byte[4];
        var received = 0;
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0) throw new IOException("HTTP headers ended early.");
            headerBytes.WriteByte((byte)value);
            last[received++ % 4] = (byte)value;
            if (received >= 4 && last[(received - 4) % 4] == 13 && last[(received - 3) % 4] == 10 &&
                last[(received - 2) % 4] == 13 && last[(received - 1) % 4] == 10) break;
        }
        var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLength = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var length = contentLength is null ? 0 : int.Parse(contentLength.Split(':', 2)[1].Trim());
        var body = new byte[length];
        await stream.ReadExactlyAsync(body);
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(bytes);
        return (headers, Encoding.UTF8.GetString(body));
    }
}
