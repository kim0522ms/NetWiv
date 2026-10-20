using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class JsonTextIpcTests
{
    private const string Numbers = """{"long":9223372036854775807,"ulong":18446744073709551615,"decimal":0.1234567890123456789012345678,"nested":{"values":[9007199254740993,null,"quoted \" text"]}}""";

    public static async Task PrecisionAndIsolation()
    {
        foreach (var isolated in new[] { false, true })
        {
            var runtime = new FakeRuntime();
            var builder = new Builder().UseRuntime(runtime);
            if (isolated) builder.UseEmbeddedConfig(assembly: typeof(IsolationTests).Assembly, resourceName: "isolation-config.json")
                .UseEmbeddedAssets(typeof(IsolationTests).Assembly);
            builder.RegisterCommand("echo", (context, args, _) =>
            {
                Assert.Equal(Numbers, args.GetRawText());
                Assert.Equal(long.MaxValue, args.GetProperty("long").GetInt64());
                Assert.Equal(ulong.MaxValue, args.GetProperty("ulong").GetUInt64());
                Assert.Equal(0.1234567890123456789012345678m, args.GetProperty("decimal").GetDecimal());
                Assert.Equal(Numbers, context.Request.Body().Json.GetRawText());
                Assert.Equal("custom", context.Request.Headers()["x-test"]);
                return ValueTask.FromResult(args);
            });
            using var app = builder.Build();
            app.Run();
            var payload = JsonValue.From(Numbers);
            if (isolated) payload = IsolationTests.Encrypt(IsolationTests.Key(app.Handle.Isolation!),
                Encoding.UTF8.GetBytes(payload.GetRawText()), false);
            Send(runtime, "echo", payload);
            Assert.Equal($"window.__TAURI_INTERNALS__.runCallback(1,{JsonValue.From(Numbers).GetRawText()});", await runtime.NextScript());
            if (isolated)
            {
                Send(runtime, "echo", JsonValue.From(Numbers));
                Assert.Contains("runCallback(2,", await runtime.NextScript());
            }
        }
    }

    public static async Task Boundaries()
    {
        var runtime = new FakeRuntime();
        var calls = 0;
        using var app = new Builder().UseRuntime(runtime)
            .RegisterCommand("echo", (_, args, _) => { calls++; return ValueTask.FromResult(args); })
            .RegisterCommand("null", (_, _, _) => ValueTask.FromResult(JsonValue.Null))
            .RegisterCommand("fail", (_, _, _) => throw new InvokeException(JsonValue.Parse("""{"code":"expected"}""")))
            .InvokeHandler(commands => commands.RegisterResponse("binary",
                (_, _, _) => ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(new byte[] { 1, 2, 255 }))))
            .Build();
        app.Run();
        foreach (var payload in new[] { JsonValue.EmptyObject, JsonValue.From("{"), JsonValue.From("null"), JsonValue.From("[]"), JsonValue.From("42") })
        {
            Send(runtime, "echo", payload);
            Assert.Contains("runCallback(2,", await runtime.NextScript());
        }
        Send(runtime, "echo", JsonValue.From(Numbers), key: "wrong");
        Assert.Contains("invalid invoke key", await runtime.NextScript());
        Send(runtime, "echo", JsonValue.From(Numbers), source: "https://untrusted.example");
        Assert.Contains("not allowed", await runtime.NextScript());
        Assert.Equal(0, calls);
        Send(runtime, "null", JsonValue.From("{}"));
        Assert.Contains("runCallback(1,\"null\")", await runtime.NextScript());
        Send(runtime, "fail", JsonValue.From("{}"));
        Assert.Contains("runCallback(2,{\"code\":\"expected\"})", await runtime.NextScript());
        Send(runtime, "binary", JsonValue.From("{}"));
        Assert.Contains("does not support binary responses", await runtime.NextScript());
        runtime.Context!.OnDocumentChanged!(new("main", 2, false));
        Send(runtime, "echo", JsonValue.From(Numbers), document: 1);
        Assert.Equal(0, runtime.PendingScripts);
        Assert.Equal(0, calls);

        var deniedRuntime = new FakeRuntime();
        using var denied = new Builder().UseRuntime(deniedRuntime).AppCommandManifest("echo")
            .RegisterCommand("echo", (_, args, _) => { calls++; return ValueTask.FromResult(args); }).Build();
        denied.Run();
        Send(deniedRuntime, "echo", JsonValue.From(Numbers));
        Assert.Contains("not allowed", await deniedRuntime.NextScript());
        Assert.Equal(0, calls);
    }

    private static void Send(FakeRuntime runtime, string command, JsonElement payload,
        string? key = null, string source = "tauri://localhost", ulong document = 0) =>
        runtime.Context!.OnMessage(new("main", new Uri(source),
            $$$$"""{"cmd":"{{{{command}}}}","callback":1,"error":2,"__NETWIV_INVOKE_KEY__":"{{{{key ?? IsolationTests.InvokeKey(runtime)}}}}","__NETWIV_JSON__":true,"payload":{{{{payload.GetRawText()}}}},"options":{"headers":{"x-test":"custom"}}}""",
            "main", document));
}
