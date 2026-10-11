using NetWiv;
using NetWiv.Plugin.Sql;

internal static class SqlPluginTests
{
    internal static async Task IntegerBinding()
    {
        var identifier = "dev.netwiv.sql-integer-" + Guid.NewGuid().ToString("N");
        string? directory = null;
        try
        {
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Metadata(new("SQL test", "1.0", identifier)).Plugin(new SqlPlugin())
                .Capability(new("main", ["sql:default", "sql:allow-execute"], Windows: ["main"])).Build();
            app.Run();
            directory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig);
            await Invoke("load", "{}");
            await Invoke("execute", "{\"query\":\"CREATE TABLE numbers (value)\"}");
            foreach (var value in new[] { "0", "-1", "9007199254740993", "-9007199254740993", "9223372036854775807", "-9223372036854775808" })
            {
                await Invoke("execute", "{\"query\":\"INSERT INTO numbers VALUES ($1)\",\"values\":[" + value + "]}");
                var row = (await Invoke("select", "{\"query\":\"SELECT CAST(value AS TEXT) AS value, typeof(value) AS type FROM numbers ORDER BY rowid DESC LIMIT 1\"}"))[0];
                Assert.Equal(value, row.GetProperty("value").GetString());
                Assert.Equal("integer", row.GetProperty("type").GetString());
            }
            var fraction = (await Invoke("select", "{\"query\":\"SELECT $1 AS value, typeof($1) AS type\",\"values\":[2.5]}"))[0];
            Assert.Equal(2.5, fraction.GetProperty("value").GetDouble());
            Assert.Equal("real", fraction.GetProperty("type").GetString());
            foreach (var value in new[] { "9223372036854775808", "-9223372036854775809" })
            {
                try { await Invoke("execute", "{\"query\":\"INSERT INTO numbers VALUES ($1)\",\"values\":[" + value + "]}"); }
                catch (InvokeException error) { Assert.Contains("64-bit", error.Message); continue; }
                throw new Exception("Out-of-range integer was silently converted to floating point.");
            }

            async Task<System.Text.Json.JsonElement> Invoke(string command, string args)
            {
                var response = await GuestIpc.Invoke(runtime, "plugin:sql|" + command, "{\"db\":\"sqlite:integers.db\"" + (args.Length > 2 ? "," + args[1..] : "}"));
                if (response.Headers!["Tauri-Response"] == "error") throw new InvokeException(GuestIpc.Body(response));
                return JsonValue.Parse(GuestIpc.Body(response));
            }
        }
        finally { if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    internal static async Task SqliteLifecycle()
    {
        var identifier = "dev.netwiv.sql-test-" + Guid.NewGuid().ToString("N");
        var database = "sqlite:test.db";
        string? path = null;
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime())
                .Metadata(new AppMetadata("SQL Test", "1.0.0", identifier))
                .Plugin(new SqlPlugin()).Build();
            app.Run();
            path = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppConfig, "test.db");
            Assert.Equal(database, (await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:test.db"}"""))).GetString());
            await app.Handle.InvokeAsync("plugin:sql|execute", JsonValue.Parse(
                """{"db":"sqlite:test.db","query":"CREATE TABLE items (id INTEGER PRIMARY KEY, title TEXT, score REAL, data BLOB)","values":[]}"""));
            var inserted = await app.Handle.InvokeAsync("plugin:sql|execute", JsonValue.Parse(
                """{"db":"sqlite:test.db","query":"INSERT INTO items (title, score) VALUES ($1, $2)","values":["hello",2.5]}"""));
            Assert.Equal(1, inserted[0].GetInt32());
            Assert.Equal(1, inserted[1].GetInt64());
            var rows = await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                """{"db":"sqlite:test.db","query":"SELECT id, title, score FROM items WHERE title = $1","values":["hello"]}"""));
            Assert.Equal(1, rows.GetArrayLength());
            Assert.Equal("hello", rows[0].GetProperty("title").GetString());
            Assert.Equal(2.5, rows[0].GetProperty("score").GetDouble());
            Assert.True((await app.Handle.InvokeAsync("plugin:sql|close", JsonValue.Parse("""{"db":"sqlite:test.db"}"""))).GetBoolean(),
                "Close did not confirm success.");
            try
            {
                await app.Handle.InvokeAsync("plugin:sql|select", JsonValue.Parse(
                    """{"db":"sqlite:test.db","query":"SELECT 1","values":[]}"""));
                throw new Exception("A closed database was still queried.");
            }
            catch (InvokeException) { }
            try
            {
                await app.Handle.InvokeAsync("plugin:sql|load", JsonValue.Parse("""{"db":"sqlite:../outside.db"}"""));
                throw new Exception("A database outside the app config directory was accepted.");
            }
            catch (InvokeException) { }
        }
        finally
        {
            if (path is not null && File.Exists(path)) File.Delete(path);
            if (path is not null && Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
