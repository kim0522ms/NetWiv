using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class IsolationTests
{
    public static Task CreateKeys()
    {
        using var first = new IsolationPattern(typeof(IsolationTests).Assembly);
        using var second = new IsolationPattern(typeof(IsolationTests).Assembly);
        Assert.True(first.Scheme != second.Scheme, "Isolation origins must be unique.");
        Assert.True(!Key(first).SequenceEqual(Key(second)), "Isolation AES keys must be independently generated.");
        return Task.CompletedTask;
    }

    public static Task CipherAndProtocolBoundaries()
    {
        using var pattern = new IsolationPattern(typeof(IsolationTests).Assembly);
        var key = Key(pattern);
        var raw = Encrypt(key, [1, 41, 65, 12, 78], true);
        Assert.Equal("[1,41,65,12,78]", pattern.Decrypt(raw).GetRawText());
        const string json = "{\"key\":1,\"anotherKey\":\"string\"}";
        Assert.Equal(json, pattern.Decrypt(Encrypt(key, Encoding.UTF8.GetBytes(json), false)).GetRawText());
        var modifiedKey = key.ToArray(); modifiedKey[0] ^= 1;
        Assert.Throws<InvokeException>(() => pattern.Decrypt(Encrypt(modifiedKey, [1, 2], true)));
        Assert.Throws<InvokeException>(() => pattern.Decrypt(JsonValue.Parse("{\"nonce\":[0],\"payload\":[],\"contentType\":\"application/json\"}")));
        Assert.Throws<InvokeException>(() => pattern.Decrypt(JsonValue.Parse("{\"nonce\":[0,0,0,0,0,0,0,0,0,0,0,0],\"payload\":[],\"contentType\":\"application/json\"}")));
        Assert.Throws<JsonException>(() => pattern.Decrypt(Encrypt(key, [0xff], false)));
        var response = Response(pattern);
        Assert.True(!response.Headers!.ContainsKey("Access-Control-Allow-Origin"), "Isolation key response must not grant cross-origin reads.");
        var csp = ContentSecurityPolicy.Parse(response.Headers["Content-Security-Policy"]);
        Assert.Equal("'none'", csp["default-src"].Single());
        Assert.True(csp["script-src"].Any(static item => item.StartsWith("'nonce-", StringComparison.Ordinal)), "Isolation runtime script needs a nonce.");
        var text = Encoding.UTF8.GetString(response.Body.Span);
        Assert.DoesNotContain("__TEMPLATE_", text);
        Assert.DoesNotContain("__RAW_", text);
        Assert.DoesNotContain("src=\"hook.js\"", text);
        Assert.Contains("async payload => payload", text);
        Assert.Equal(404, pattern.Resolve(new(new Uri(pattern.Scheme + "://localhost/index.html"), "GET", new Dictionary<string, string>(), WindowOrigin: "tauri://localhost"), new HashSet<string>(), new Dictionary<string, string>()).StatusCode);
        pattern.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pattern.Decrypt(raw));
        return Task.CompletedTask;
    }

    // The same raw/JSON bodies, callbacks, key and custom-header assertions as
    // tauri/src/ipc/protocol.rs::parse_invoke_request_isolation; native URI attests origin.
    // Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
    // SPDX-License-Identifier: Apache-2.0 OR MIT
    public static async Task ParseInvokeRequestIsolation()
    {
        var runtime = new FakeRuntime();
        Request? request = null;
        Uri? source = null;
        using var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig(assembly: typeof(IsolationTests).Assembly, resourceName: "isolation-config.json")
            .UseEmbeddedAssets(typeof(IsolationTests).Assembly)
            .RegisterCommand("write_something", (context, args, _) => { request = context.Request; source = context.Source; return ValueTask.FromResult(args); }).Build();
        app.Run();
        var key = Key(app.Handle.Isolation!);
        Assert.DoesNotContain("[" + string.Join(',', key) + "]", runtime.Context!.InitializationScript);
        Assert.Equal(app.Handle.Isolation!.Scheme, runtime.Context.IsolationProtocol);
        foreach (var binary in new[] { true, false })
        {
            var bytes = binary ? new byte[] { 1, 41, 65, 12, 78 } : Encoding.UTF8.GetBytes("{\"key\":1,\"anotherKey\":\"string\"}");
            Send(runtime, Encrypt(key, bytes, binary), 12378123, 6243);
            Assert.Contains("runCallback(12378123,", await runtime.NextScript());
            Assert.Equal(new Uri("tauri://localhost"), source);
            Assert.Equal(binary, request!.Body().IsBinary);
            if (binary) Assert.True(request.Body().Bytes.Span.SequenceEqual(bytes), "Decrypted binary body differs.");
            else Assert.Equal(Encoding.UTF8.GetString(bytes), request.Body().Json.GetRawText());
            Assert.Equal("application/json", request.Headers()["content-type"]);
            Assert.Equal("12378123", request.Headers()["tauri-callback"]);
            Assert.Equal("6243", request.Headers()["tauri-error"]);
            Assert.Equal(InvokeKey(runtime), request.Headers()["tauri-invoke-key"]);
            Assert.Equal("tauri://localhost", request.Headers()["origin"]);
            Assert.Equal("custom value", request.Headers()["x-test"]);
        }
        request = null;
        Send(runtime, JsonValue.Parse("{\"unfiltered\":true}"));
        Assert.Contains("runCallback(2,", await runtime.NextScript());
        Assert.True(request is null, "Unencrypted IPC bypassed the isolation hook.");
        var oldKey = key.ToArray(); oldKey[0] ^= 1;
        Send(runtime, Encrypt(oldKey, Encoding.UTF8.GetBytes("{}"), false));
        Assert.Contains("AES-GCM", await runtime.NextScript());
        Assert.True(request is null, "Invalid ciphertext invoked application code.");
        var html = runtime.Context.ResolveAsset(new(new Uri("tauri://localhost/"), "GET", new Dictionary<string, string>()))!;
        Assert.Contains(app.Handle.Isolation.Scheme, html.Headers!["Content-Security-Policy"]);
        Assert.Contains(HtmlAssets.Hash(IsolationPattern.IframeStyle), html.Headers["Content-Security-Policy"]);
    }

    private static AssetResponse Response(IsolationPattern pattern) => pattern.Resolve(
        new(new Uri(pattern.Scheme + "://localhost/"), "GET", new Dictionary<string, string>(), WindowOrigin: "tauri://localhost"), new HashSet<string>(), new Dictionary<string, string>());
    internal static byte[] Key(IsolationPattern pattern)
    {
        var html = Encoding.UTF8.GetString(Response(pattern).Body.Span);
        const string marker = "const aesGcmKeyRaw = new Uint8Array(";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        using var document = JsonDocument.Parse(html[start..html.IndexOf(')', start)]);
        return document.RootElement.EnumerateArray().Select(static element => element.GetByte()).ToArray();
    }
    internal static JsonElement Encrypt(byte[] key, byte[] bytes, bool binary)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[bytes.Length]; var tag = new byte[16];
        using var cipher = new AesGcm(key, 16);
        cipher.Encrypt(nonce, bytes, ciphertext, tag);
        return JsonValue.Parse($$"""{"nonce":[{{string.Join(',', nonce)}}],"payload":[{{string.Join(',', ciphertext.Concat(tag))}}],"contentType":"{{(binary ? "application/octet-stream" : "application/json")}}"}""");
    }
    internal static string InvokeKey(FakeRuntime runtime)
    {
        const string marker = "__NETWIV_INVOKE_KEY__: '";
        return runtime.Context!.InitializationScript.Substring(runtime.Context.InitializationScript.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
    }
    private static void Send(FakeRuntime runtime, JsonElement envelope, uint callback = 1, uint error = 2) =>
        runtime.Context!.OnMessage(new("main", new Uri("tauri://localhost"), $$$$"""{"cmd":"write_something","callback":{{{{callback}}}},"error":{{{{error}}}},"__NETWIV_INVOKE_KEY__":"{{{{InvokeKey(runtime)}}}}","payload":{{{{envelope.GetRawText()}}}},"options":{"headers":{"x-test":"custom value"}}}""", "main"));
}
