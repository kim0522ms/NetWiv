using System.Net;
using System.Net.Sockets;
using System.Text;
using NetWiv;
using NetWiv.Plugin.Sql;
using NetWiv.Plugin.Upload;

internal static class PluginSecurityTests
{
    internal static async Task UploadGrantsAndDenies()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p1-grants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "data.txt"); File.WriteAllText(path, "old");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/file";
        var pathRule = JsonValue.Parse("{\"path\":" + JsonValue.From(path).GetRawText() + "}");
        var urlRule = JsonValue.Parse("{\"url\":" + JsonValue.From(url).GetRawText() + "}");
        try
        {
            foreach (var policy in new[] { "missing", "deny-path", "deny-url", "allow" })
            {
                var runtime = new FakeRuntime();
                using var app = new Builder().UseRuntime(runtime).Plugin(new UploadPlugin())
                    .Capability(new("main", ["upload:default"], Windows: ["main"], PermissionScopes:
                    [new("upload:default", policy == "missing" ? [] : [urlRule, pathRule],
                        policy == "deny-path" ? [pathRule] : policy == "deny-url" ? [urlRule] : [])])).Build();
                app.Run();
                foreach (var command in new[] { "download", "upload" })
                {
                    Task? server = null;
                    if (policy == "allow") server = Task.Run(async () =>
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                        var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var requestLine = await reader.ReadLineAsync(timeout.Token);
                        Assert.True(requestLine!.StartsWith(command == "upload" ? "POST " : "GET ", StringComparison.Ordinal), "Unexpected HTTP method.");
                        var length = 0;
                        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line)
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Split(':')[1]);
                        if (length > 0)
                        {
                            var body = new char[length]; var read = 0;
                            while (read < length) read += await reader.ReadAsync(body.AsMemory(read), timeout.Token);
                            Assert.Equal("new", new string(body));
                        }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nnew"), timeout.Token);
                    });
                    var result = await GuestIpc.Invoke(runtime, "plugin:upload|" + command,
                        "{\"url\":" + JsonValue.From(url).GetRawText() + ",\"filePath\":" + JsonValue.From(path).GetRawText() + ",\"onProgress\":\"__CHANNEL__:1\"}");
                    if (server is not null) await server;
                    Assert.Equal(policy == "allow" ? "ok" : "error", result.Headers!["Tauri-Response"]);
                    if (policy != "allow") { Assert.Contains("scope", GuestIpc.Body(result)); Assert.True(!listener.Pending(), "Denied request reached network."); }
                    Assert.Equal(policy == "allow" ? "new" : "old", File.ReadAllText(path));
                }
            }
        }
        finally { listener.Stop(); Directory.Delete(root, true); }
    }

    internal static async Task UploadScopes()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-p1-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "allowed.txt");
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(file, "allowed"); File.WriteAllText(outside, "private");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new UploadPlugin())
                .Capability(new("main", ["upload:default"], Windows: ["main"], PermissionScopes:
                [new("upload:default", [JsonValue.Parse("{\"url\":" + JsonValue.From(url + "allowed").GetRawText() + "}"),
                    JsonValue.Parse("{\"path\":" + JsonValue.From(file).GetRawText() + "}")], [])])).Build();
            app.Run();
            foreach (var test in new[] { (Url: url + "blocked", Path: file), (Url: url + "allowed", Path: outside), (Url: url + "allowed", Path: file) })
            {
                // The permitted request redirects outside the allowed URL. No body may be saved there either.
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var server = Task.Run(async () =>
                {
                    try
                    {
                        using var client = await listener.AcceptTcpClientAsync(stop.Token);
                        var stream = client.GetStream(); _ = await stream.ReadAsync(new byte[4096], stop.Token);
                        var response = test.Path == file && test.Url.EndsWith("allowed", StringComparison.Ordinal)
                            ? "HTTP/1.1 302 Found\r\nLocation: /blocked\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                            : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stop.Token);
                    }
                    catch (OperationCanceledException) { }
                });
                var result = await GuestIpc.Invoke(runtime, "plugin:upload|download", "{\"url\":" + JsonValue.From(test.Url).GetRawText() +
                    ",\"filePath\":" + JsonValue.From(test.Path).GetRawText() + ",\"onProgress\":\"__CHANNEL__:1\"}").WaitAsync(TimeSpan.FromSeconds(5));
                stop.Cancel(); await server;
                Assert.Equal("error", result.Headers!["Tauri-Response"]);
                Assert.Contains("scope", GuestIpc.Body(result));
                Assert.Equal("allowed", File.ReadAllText(file)); Assert.Equal("private", File.ReadAllText(outside));
            }
        }
        finally { listener.Stop(); Directory.Delete(root, true); }
    }

    internal static async Task SqlReadOnly()
    {
        var identifier = "dev.netwiv.p1-sql-" + Guid.NewGuid().ToString("N");
        var runtime = new FakeRuntime();
        string? path = null;
        try
        {
            using var app = new Builder().UseRuntime(runtime).Metadata(new("P1", "1.0", identifier)).Plugin(new SqlPlugin())
                .Capability(new("main", ["sql:default"], Windows: ["main"])).Build();
            app.Run(); path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, "test.db");
            await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:test.db"}"""));
            await app.Handle.InvokeAsync("plugin:sql|execute", JsonValue.Parse("""{"db":"sqlite:test.db","query":"CREATE TABLE items (id INTEGER)"}"""));
            foreach (var query in new[] { "INSERT INTO items VALUES (42)", "DROP TABLE items", "ATTACH DATABASE ':memory:' AS other", "PRAGMA query_only=OFF" })
            {
                var result = await GuestIpc.Invoke(runtime, "plugin:sql|select", "{\"db\":\"sqlite:test.db\",\"query\":" + JsonValue.From(query).GetRawText() + "}");
                Assert.Equal("error", result.Headers!["Tauri-Response"]);
            }
            var rows = await GuestIpc.Invoke(runtime, "plugin:sql|select", """{"db":"sqlite:test.db","query":"SELECT COUNT(*) AS count FROM items"}""");
            Assert.Equal("ok", rows.Headers!["Tauri-Response"]);
            Assert.Contains("\"count\":0", GuestIpc.Body(rows));
            await app.Handle.InvokeAsync("plugin:sql|execute", JsonValue.Parse("""{"db":"sqlite:test.db","query":"INSERT INTO items VALUES (7)"}"""));
        }
        finally { if (path is not null && Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    internal static async Task SqlCancellation()
    {
        var identifier = "dev.netwiv.p1-cancel-" + Guid.NewGuid().ToString("N");
        string? path = null;
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime()).Metadata(new("P1", "1.0", identifier)).Plugin(new SqlPlugin()).Build();
            app.Run(); path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, "test.db");
            await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:test.db"}"""));
            using var stop = new CancellationTokenSource();
            var pending = app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse("""{"db":"sqlite:test.db","query":"WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<20000000) SELECT sum(x) FROM n"}"""), stop.Token).AsTask();
            await Task.Delay(50); stop.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("SQLite ignored in-flight cancellation."); }
            catch (OperationCanceledException) { }
            var result = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse("""{"db":"sqlite:test.db","query":"SELECT 1 AS value"}"""));
            Assert.Equal(1, result[0].GetProperty("value").GetInt32());
        }
        finally { if (path is not null && Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
