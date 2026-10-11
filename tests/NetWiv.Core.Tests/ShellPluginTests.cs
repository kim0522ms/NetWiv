using System.Text;
using System.Text.Json;
using NetWiv;
using NetWiv.Plugin.Shell;
using NetWiv.Runtime;

internal static class ShellPluginTests
{
    internal static async Task ScopedExecute()
    {
        var windows = OperatingSystem.IsWindows();
        var command = windows ? "cmd.exe" : "/bin/echo";
        var arguments = windows ? new[] { "/c", "echo", "hello" } : new[] { "hello" };
        var scope = JsonValue.Parse("{\"name\":\"test-echo\",\"cmd\":" + JsonValue.From(command).GetRawText() +
            ",\"args\":[" + string.Join(',', arguments.Select(value => JsonValue.From(value).GetRawText())) + "]}");
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new ShellPlugin())
            .Capability(new("main", ["shell:allow-execute", "shell:allow-spawn"], Windows: ["main"],
                PermissionScopes: [new("shell:allow-execute", [scope], []), new("shell:allow-spawn", [scope], [])])).Build();
        app.Run();
        const string marker = "headers.set('Tauri-Invoke-Key', '";
        var script = runtime.Context!.InitializationScript;
        var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
            ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
        };
        async Task<AssetResponse> Invoke(string payload) => await runtime.Context.InvokeProtocol!(new(
            new Uri("ipc://localhost/plugin%3Ashell%7Cexecute"), "POST", headers,
            Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main"));
        var denied = await Invoke("""{"program":"unscoped","args":[]} """);
        Assert.Equal("error", denied.Headers!["Tauri-Response"]);
        Assert.Contains("scope", Encoding.UTF8.GetString(denied.Body.Span));
        var payload = "{\"program\":\"test-echo\",\"args\":[" +
            string.Join(',', arguments.Select(value => JsonValue.From(value).GetRawText())) + "]}";
        var executed = await Invoke(payload);
        Assert.True(executed.Headers!["Tauri-Response"] == "ok", "Shell execute failed: " + Encoding.UTF8.GetString(executed.Body.Span));
        var result = JsonDocument.Parse(executed.Body).RootElement;
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        Assert.Contains("hello", result.GetProperty("stdout").GetString()!);
        var otherArgs = await Invoke("""{"program":"test-echo","args":["unexpected"]}""");
        Assert.Equal("error", otherArgs.Headers!["Tauri-Response"]);

        var spawn = await runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/plugin%3Ashell%7Cspawn"),
            "POST", headers, Encoding.UTF8.GetBytes(payload[..^1] + ",\"onEvent\":\"__CHANNEL__:17\"}"),
            WebviewLabel: "main", WindowLabel: "main"));
        Assert.True(spawn.Headers!["Tauri-Response"] == "ok", "Shell spawn failed: " + Encoding.UTF8.GetString(spawn.Body.Span));
        Assert.True(JsonDocument.Parse(spawn.Body).RootElement.GetInt32() > 0, "Shell spawn did not return a PID.");
        var first = await runtime.NextScript();
        var second = await runtime.NextScript();
        Assert.True(first.Contains("Stdout", StringComparison.Ordinal) || second.Contains("Stdout", StringComparison.Ordinal),
            "Shell spawn did not send stdout to its channel.");
        Assert.True(first.Contains("Terminated", StringComparison.Ordinal) || second.Contains("Terminated", StringComparison.Ordinal),
            "Shell spawn did not send termination to its channel.");
    }
}
