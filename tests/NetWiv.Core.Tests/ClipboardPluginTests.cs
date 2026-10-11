using System.Text;
using NetWiv;
using NetWiv.Plugin.ClipboardManager;
using NetWiv.Runtime;

internal static class ClipboardPluginTests
{
    internal static async Task PermissionsAndForwarding()
    {
        var runtime = new FakeRuntime { InvokeResult = JsonValue.From("from system") };
        using var app = new Builder().UseRuntime(runtime).Plugin(new ClipboardManagerPlugin())
            .Capability(new("main", ["clipboard-manager:default", "clipboard-manager:allow-read-text"], Windows: ["main"])).Build();
        var value = await app.Handle.InvokeAsync("plugin:clipboard-manager|read_text", JsonValue.EmptyObject);
        Assert.Equal("from system", value.GetString());
        Assert.Equal("plugin:clipboard-manager|read_text", runtime.LastCommand);
        app.Run();
        const string marker = "headers.set('Tauri-Invoke-Key', '";
        var script = runtime.Context!.InitializationScript;
        var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tauri-Invoke-Key"] = script.Substring(start, 64), ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
            ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
        };
        async Task<AssetResponse> Invoke(string command, string payload) => await runtime.Context.InvokeProtocol!(new(
            new Uri("ipc://localhost/plugin%3Aclipboard-manager%7C" + command), "POST", headers,
            Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main"));
        var permitted = await Invoke("read_text", "{}");
        Assert.Equal("ok", permitted.Headers!["Tauri-Response"]);
        var denied = await Invoke("write_text", """{"text":"secret"}""");
        Assert.Equal("error", denied.Headers!["Tauri-Response"]);
    }
}
