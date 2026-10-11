// Assertion port: tauri/src/ipc/protocol.rs::tests::parse_invoke_request.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class ProtocolIpcTests
{
    // tauri/src/webview/mod.rs::remote_origin_blocked_for_custom_commands_without_app_manifest.
    public static async Task RemoteOriginWithoutAppManifest()
    {
        foreach (var registered in new[] { false, true })
        {
            var runtime = new FakeRuntime();
            var calls = 0;
            var builder = new Builder().UseRuntime(runtime);
            if (registered) builder.RegisterCommand("any_custom_command", (_, _, _) =>
            {
                calls++;
                return ValueTask.FromResult(JsonValue.From("ok"));
            });
            using var app = builder.Build();
            app.Run();
            var headers = Headers(runtime);
            headers["Content-Type"] = "application/json";
            headers["Origin"] = "https://evil.com";
            async ValueTask<AssetResponse> Send() => await runtime.Context!.InvokeProtocol!(new(
                new Uri("ipc://localhost/any_custom_command"), "POST", headers, "{}"u8.ToArray(),
                WebviewLabel: "main", WindowLabel: "main"));
            var remote = await Send();
            Equal("error", remote.Headers!["Tauri-Response"]);
            Assert.Contains("not allowed", Encoding.UTF8.GetString(remote.Body.Span));
            Equal(0, calls);
            headers["Origin"] = "tauri://localhost";
            var local = await Send();
            var message = Encoding.UTF8.GetString(local.Body.Span);
            Assert.True(!message.Contains("not allowed", StringComparison.Ordinal),
                "The local request reached the remote-origin guard: " + message);
            Equal(registered ? "ok" : "error", local.Headers!["Tauri-Response"]);
            Equal(registered ? 1 : 0, calls);
        }
    }

    public static async Task ParseInvokeRequest()
    {
        var runtime = new FakeRuntime();
        InvokeContext? captured = null;
        using var app = new Builder().UseRuntime(runtime).InvokeHandler(commands => commands.Register("write_something", (context, _, _) =>
        {
            captured = context;
            return ValueTask.FromResult(JsonValue.From("ok"));
        })).Build();
        app.Run();
        var headers = Headers(runtime);
        foreach (var (contentType, bytes) in new[] {
            ("application/octet-stream", new byte[] { 123, 31, 45 }),
            ("application/json", "{\"key\":1,\"anotherKey\":\"asda\"}"u8.ToArray()) })
        {
            headers["Content-Type"] = contentType;
            var response = await runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/write_something"), "POST", headers, bytes, WebviewLabel: "main", WindowLabel: "main"));
            Equal("ok", response.Headers!["Tauri-Response"]);
            Equal(new Uri("tauri://localhost"), captured!.Source);
            var request = captured.Request;
            Equal(headers.Count, request.Headers().Count);
            foreach (var (name, value) in headers) Equal(value, request.Headers()[name]);
            Equal(contentType == "application/octet-stream", request.Body().IsBinary);
            if (request.Body().IsBinary)
            {
                if (!request.Body().Bytes.Span.SequenceEqual(bytes)) throw new Exception("Raw invoke bytes changed.");
            }
            else Equal(Encoding.UTF8.GetString(bytes), request.Body().Json.GetRawText());
        }
    }

    public static async Task Boundaries()
    {
        var runtime = new FakeRuntime();
        var calls = 0;
        using var app = new Builder().UseRuntime(runtime).InvokeHandler(commands =>
        {
            commands.Register("write_something", (_, _, _) => { calls++; return ValueTask.FromResult(JsonValue.From("ok")); });
            commands.Register("fail", (_, _, _) => throw new InvokeException(JsonValue.Parse("{\"code\":\"expected\",\"detail\":42}")));
            commands.RegisterResponse("raw", (_, _, _) => ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(new byte[] { 1, 2, 3 })));
        }).Build();
        app.Run();
        var headers = Headers(runtime);
        headers["Content-Type"] = "application/json";
        async ValueTask<AssetResponse> Send(string method = "POST", string command = "write_something", ReadOnlyMemory<byte> body = default, ulong document = 0) =>
            await runtime.Context!.InvokeProtocol!(new(new Uri("ipc://localhost/" + command), method, headers, body, WebviewLabel: "main", WindowLabel: "main", DocumentId: document));

        var preflight = await Send("OPTIONS");
        Equal(200, preflight.StatusCode);
        Equal("*", preflight.Headers!["Access-Control-Allow-Headers"]);
        Equal("*", preflight.Headers["Access-Control-Allow-Origin"]);
        Equal("Tauri-Response", preflight.Headers["Access-Control-Expose-Headers"]);
        Equal(405, (await Send("GET")).StatusCode);
        foreach (var name in new[] { "Tauri-Invoke-Key", "Origin", "Tauri-Callback", "Tauri-Error" })
        {
            var saved = headers[name]; headers.Remove(name);
            Equal("missing " + name + " header", Encoding.UTF8.GetString((await Send()).Body.Span));
            headers[name] = saved;
        }
        headers["Tauri-Callback"] = "4294967296";
        Equal("Tauri callback header value must be a numeric string", Encoding.UTF8.GetString((await Send()).Body.Span));
        headers["Tauri-Callback"] = "12378123";
        headers["Origin"] = "null";
        Equal("Origin header is not a valid URL", Encoding.UTF8.GetString((await Send()).Body.Span));
        headers["Origin"] = "https://untrusted.example";
        Equal("error", (await Send()).Headers!["Tauri-Response"]);
        headers["Origin"] = "tauri://localhost";
        headers["Content-Type"] = "invalid";
        Equal("unknown content type", Encoding.UTF8.GetString((await Send()).Body.Span));
        headers["Content-Type"] = "text/plain";
        Equal("content type text/plain is not implemented", Encoding.UTF8.GetString((await Send()).Body.Span));
        headers["Content-Type"] = "application/json";
        Equal(500, (await Send(body: "{"u8.ToArray())).StatusCode);
        Equal(0, calls);
        Equal("ok", (await Send(command: "write%5fsomething")).Headers!["Tauri-Response"]);
        Equal(1, calls);
        var failure = await Send(command: "fail");
        Equal(200, failure.StatusCode);
        Equal("error", failure.Headers!["Tauri-Response"]);
        Equal("{\"code\":\"expected\",\"detail\":42}", Encoding.UTF8.GetString(failure.Body.Span));
        var raw = await Send(command: "raw");
        Equal("application/octet-stream", raw.ContentType);
        if (!raw.Body.Span.SequenceEqual(new byte[] { 1, 2, 3 })) throw new Exception("Binary response changed.");
        runtime.Context!.OnDocumentChanged!(new("main", 2, false));
        Equal(500, (await Send(document: 1)).StatusCode);
        runtime.Context.OnDocumentChanged!(new("main", 3, true));
        Equal(500, (await Send(document: 0)).StatusCode);
        Equal(1, calls);
    }

    private static Dictionary<string, string> Headers(FakeRuntime runtime)
    {
        const string marker = "headers.set('Tauri-Invoke-Key', '";
        var script = runtime.Context!.InitializationScript;
        var key = script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
        return new(StringComparer.OrdinalIgnoreCase) { ["Tauri-Invoke-Key"] = key, ["Tauri-Callback"] = "12378123", ["Tauri-Error"] = "6243", ["Origin"] = "tauri://localhost" };
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
