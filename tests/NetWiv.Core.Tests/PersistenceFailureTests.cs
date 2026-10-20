using System.Net;
using System.Net.Sockets;
using System.Text;
using NetWiv;
using NetWiv.Plugin.Store;
using NetWiv.Plugin.Upload;

internal static class PersistenceFailureTests
{
    internal static async Task StoreReload()
    {
        var name = "netwiv-p1-" + Guid.NewGuid().ToString("N") + ".json";
        using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new StorePlugin()).Build();
        var path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppData, name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"answer\":42}");
            var rid = (await app.Handle.InvokeAsync("plugin:store|load", JsonValue.Parse(
                "{\"path\":" + JsonValue.From(name).GetRawText() + "}"))).GetUInt32();
            File.WriteAllText(path, "{broken");
            try { await app.Handle.InvokeAsync("plugin:store|reload", JsonValue.Parse($"{{\"rid\":{rid}}}")); throw new Exception("Corrupt store accepted."); }
            catch (InvokeException) { }
            var value = await app.Handle.InvokeAsync("plugin:store|get", JsonValue.Parse($"{{\"rid\":{rid},\"key\":\"answer\"}}"));
            Assert.True(value[1].GetBoolean(), "Failed reload discarded the previous value.");
            Assert.Equal(42, value[0].GetInt32());
        }
        finally { File.Delete(path); }
    }

    internal static async Task StoreReplace()
    {
        var name = "netwiv-p1-" + Guid.NewGuid().ToString("N") + ".json";
        using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new StorePlugin()).Build();
        var path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppData, name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"answer\":1}");
            var rid = (await app.Handle.InvokeAsync("plugin:store|load", JsonValue.Parse(
                "{\"path\":" + JsonValue.From(name).GetRawText() + "}"))).GetUInt32();
            using var previous = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await app.Handle.InvokeAsync("plugin:store|set", JsonValue.Parse($"{{\"rid\":{rid},\"key\":\"answer\",\"value\":42}}"));
            await app.Handle.InvokeAsync("plugin:store|save", JsonValue.Parse($"{{\"rid\":{rid}}}"));
            using var reader = new StreamReader(previous);
            Assert.Equal("{\"answer\":1}", reader.ReadToEnd());
            Assert.Equal("{\"answer\":42}", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    internal static async Task InterruptedDownload()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p1-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "data.txt");
        File.WriteAllText(path, "previous complete file");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            _ = await stream.ReadAsync(new byte[4096]);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\npartial"));
        });
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new UploadPlugin()).Build();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/file";
            try
            {
                await app.Handle.InvokeAsync("plugin:upload|download", JsonValue.Parse(
                    "{\"url\":" + JsonValue.From(url).GetRawText() + ",\"filePath\":" + JsonValue.From(path).GetRawText() + ",\"onProgress\":\"__CHANNEL__:1\"}"));
                throw new Exception("Truncated download succeeded.");
            }
            catch (Exception error) when (error is IOException or HttpRequestException) { }
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("previous complete file", File.ReadAllText(path));
            Assert.Equal(1, Directory.GetFiles(root).Length);
        }
        finally { listener.Stop(); Directory.Delete(root, true); }
    }
}
