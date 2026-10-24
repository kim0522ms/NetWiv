using NetWiv;
using NetWiv.Plugin.Fs;
using NetWiv.Runtime;
using System.Text;

internal static class FsPluginTests
{
    internal static async Task FileOperations()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-fs-test-").FullName;
        try
        {
            using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new FsPlugin()).Build();
            var folder = Path.Combine(root, "nested");
            await app.Handle.InvokeAsync("plugin:fs|mkdir", Args("path", folder));
            try
            {
                await app.Handle.InvokeAsync("plugin:fs|mkdir", Args("path", folder));
                throw new Exception("Existing non-recursive directory was accepted.");
            }
            catch (InvokeException) { }
            var file = Path.Combine(folder, "hello.txt");
            var rid = (await app.Handle.InvokeAsync("plugin:fs|open", JsonValue.Parse("{\"path\":" + JsonValue.From(file).GetRawText() + ",\"options\":{\"write\":true,\"create\":true}}"))).GetUInt32();
            Assert.Equal(2, (await app.Handle.InvokeAsync("plugin:fs|write", JsonValue.Parse($"{{\"rid\":{rid},\"data\":[65,66]}}"))).GetInt32());
            await app.Handle.InvokeAsync("plugin:resources|close", JsonValue.Parse($"{{\"rid\":{rid}}}"));
            var result = await app.Handle.InvokeResponseAsync("plugin:fs|read_file", Args("path", file));
            Check(result.IsBinary);
            Assert.Equal("AB", System.Text.Encoding.UTF8.GetString(result.Binary.Span));
            var text = await app.Handle.InvokeResponseAsync("plugin:fs|read_text_file", Args("path", file));
            Check(text.IsBinary);
            Assert.Equal("AB", Encoding.UTF8.GetString(text.Binary.Span));
            var utf16 = Path.Combine(folder, "utf16.txt");
            File.WriteAllText(utf16, "A\nB", Encoding.Unicode);
            var lineRid = (await app.Handle.InvokeAsync("plugin:fs|read_text_file_lines", JsonValue.Parse(
                "{\"path\":" + JsonValue.From(utf16).GetRawText() + ",\"options\":{\"encoding\":\"utf-16le\"}}"))).GetUInt32();
            var firstLine = await app.Handle.InvokeResponseAsync("plugin:fs|read_text_file_lines_next", JsonValue.Parse($"{{\"rid\":{lineRid}}}"));
            Assert.Equal("A", Encoding.Unicode.GetString(firstLine.Binary.Span[..^1]));
            Check((await app.Handle.InvokeAsync("plugin:fs|exists", Args("path", file))).GetBoolean());
            var entries = await app.Handle.InvokeAsync("plugin:fs|read_dir", Args("path", folder));
            Assert.True(entries.EnumerateArray().Any(entry => entry.GetProperty("name").GetString() == "hello.txt"), "Directory listing omitted hello.txt.");
            var copy = Path.Combine(folder, "copy.txt");
            await app.Handle.InvokeAsync("plugin:fs|copy_file", JsonValue.Parse("{\"fromPath\":" + JsonValue.From(file).GetRawText() + ",\"toPath\":" + JsonValue.From(copy).GetRawText() + "}"));
            Assert.Equal(2L, (await app.Handle.InvokeAsync("plugin:fs|size", Args("path", copy))).GetInt64());
            Assert.Equal(4L + new FileInfo(utf16).Length, (await app.Handle.InvokeAsync("plugin:fs|size", Args("path", folder))).GetInt64());
            Check((await app.Handle.InvokeAsync("plugin:fs|stat", Args("path", copy))).GetProperty("isFile").GetBoolean());
            await app.Handle.InvokeAsync("plugin:fs|remove", Args("path", copy));
            Check(!File.Exists(copy));
            try
            {
                await app.Handle.InvokeAsync("plugin:fs|remove", Args("path", copy));
                throw new Exception("Missing path was accepted for removal.");
            }
            catch (InvokeException) { }
            try
            {
                await app.Handle.InvokeAsync("plugin:fs|exists", JsonValue.Parse("""{"path":"../outside","options":{"baseDir":12}}"""));
                throw new Exception("Parent traversal was accepted.");
            }
            catch (InvokeException error) { Assert.Contains("parent traversal", error.Message); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static Task ScopePermissions()
    {
        var definitions = new List<PermissionDefinition>();
        var context = new PluginContext("fs", new CommandRegistry(), definitions, JsonValue.Null);
        new FsPlugin().Initialize(context);
        context.Complete();
        var path = JsonValue.Parse("""{"path":"$TEMP/allowed/**"}""");
        var denied = JsonValue.Parse("""{"path":"$TEMP/allowed/private/**"}""");
        var capabilities = new[]
        {
            new Capability("main", ["fs:allow-read-file", "fs:scope"], Windows: ["main"],
                PermissionScopes: [new PermissionScopeExtension("fs:scope", [path], [denied])])
        };
        var authorizer = new AclAuthorizer(definitions, capabilities, new HashSet<string> { "tauri://localhost" }, false);
        var allowed = authorizer.Authorize(new("plugin:fs|read_file", "main", "main", new Uri("tauri://localhost")));
        Check(allowed.Allowed);
        Check(allowed.Scope.Allow.Any(item => item.GetProperty("path").GetString() == "$TEMP/allowed/**"));
        Check(allowed.Scope.Deny.Any(item => item.GetProperty("path").GetString() == "$TEMP/allowed/private/**"));
        Check(!authorizer.Authorize(new("plugin:fs|write_file", "main", "main", new Uri("tauri://localhost"))).Allowed);
        return Task.CompletedTask;
    }

    internal static async Task BinaryGuestIpc()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-fs-ipc-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var runtime = new FakeRuntime();
            var scope = JsonValue.From(path);
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin())
                .Capability(new("main", ["fs:allow-write-file", "fs:allow-write-text-file", "fs:allow-read-file", "fs:allow-read-text-file", "fs:scope"], Windows: ["main"],
                    PermissionScopes: [new PermissionScopeExtension("fs:scope", [scope], [])])).Build();
            app.Run();
            const string keyMarker = "headers.set('Tauri-Invoke-Key', '";
            var script = runtime.Context!.InitializationScript;
            var start = script.IndexOf(keyMarker, StringComparison.Ordinal) + keyMarker.Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
                ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/octet-stream",
                ["path"] = Uri.EscapeDataString(path), ["options"] = "{}"
            };
            var bytes = new byte[] { 0, 1, 127, 255 };
            foreach (var command in new[] { "write_file", "write_text_file" })
            foreach (var options in new string?[] { null, "", "undefined", "{}", "null" })
            {
                if (options is null) headers.Remove("options");
                else headers["options"] = options;
                var write = await runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Afs%7C" + command), "POST",
                    headers, bytes, WebviewLabel: "main", WindowLabel: "main"));
                Assert.Equal("ok", write.Headers!["Tauri-Response"]);
                Assert.True(File.ReadAllBytes(path).SequenceEqual(bytes), "Binary write did not preserve bytes.");
            }
            headers["options"] = "{broken";
            var invalid = await runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Afs%7Cwrite_file"), "POST",
                headers, new byte[] { 42 }, WebviewLabel: "main", WindowLabel: "main"));
            Assert.Equal("error", invalid.Headers!["Tauri-Response"]);
            Assert.True(File.ReadAllBytes(path).SequenceEqual(bytes), "Malformed options changed the file.");
            headers.Remove("path"); headers.Remove("options"); headers["Content-Type"] = "application/json";
            var read = await runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Afs%7Cread_file"), "POST",
                headers, Encoding.UTF8.GetBytes(Args("path", path).GetRawText()), WebviewLabel: "main", WindowLabel: "main"));
            Assert.Equal("ok", read.Headers!["Tauri-Response"]);
            Assert.True(read.Body.Span.SequenceEqual(bytes), "Binary read did not preserve bytes.");
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
            {
                var contents = encoding.GetPreamble().Concat(encoding.GetBytes("Hello 한글 😀\0\r\n")).ToArray();
                await File.WriteAllBytesAsync(path, contents);
                var text = await GuestIpc.Invoke(runtime, "plugin:fs|read_text_file", Args("path", path).GetRawText());
                Assert.Equal("ok", text.Headers!["Tauri-Response"]);
                Assert.Equal("application/octet-stream", text.ContentType);
                Assert.True(text.Body.Span.SequenceEqual(contents), "Text read must preserve bytes for guest decoding, including BOM.");
            }
        }
        finally { File.Delete(path); }
    }

    internal static async Task SymlinkScope()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-fs-symlink-").FullName;
        try
        {
            var allowed = Directory.CreateDirectory(Path.Combine(root, "allowed")).FullName;
            var outside = Path.Combine(root, "outside.txt");
            File.WriteAllText(outside, "secret");
            var link = Path.Combine(allowed, "link.txt");
            File.CreateSymbolicLink(link, outside);
            var runtime = new FakeRuntime();
            using var app = new Builder().UseRuntime(runtime).Plugin(new FsPlugin())
                .Capability(new("main", ["fs:allow-read-file", "fs:scope"], Windows: ["main"],
                    PermissionScopes: [new PermissionScopeExtension("fs:scope", [JsonValue.From(allowed + "/**")], [])])).Build();
            app.Run();
            const string marker = "headers.set('Tauri-Invoke-Key', '";
            var script = runtime.Context!.InitializationScript;
            var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
                ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
            };
            var response = await runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Afs%7Cread_file"), "POST",
                headers, Encoding.UTF8.GetBytes(Args("path", link).GetRawText()), WebviewLabel: "main", WindowLabel: "main"));
            Assert.True(response.Headers!["Tauri-Response"] != "ok", "A scoped symlink exposed a file outside the allowed directory.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static System.Text.Json.JsonElement Args(string name, string value) =>
        JsonValue.Parse("{" + JsonValue.From(name).GetRawText() + ":" + JsonValue.From(value).GetRawText() + "}");

    private static void Check(bool condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null) =>
        Assert.True(condition, expression ?? "Filesystem assertion failed.");
}
