using System.Text;
using NetWiv;
using NetWiv.Plugin.Opener;
using NetWiv.Runtime;

internal static class OpenerPluginTests
{
    internal static async Task ScopeBoundaries()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime).Plugin(new OpenerPlugin())
            .Capability(new("main", ["opener:default"], Windows: ["main"])).Build();
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
            new Uri("ipc://localhost/plugin%3Aopener%7Copen_url"), "POST", headers,
            Encoding.UTF8.GetBytes(payload), WebviewLabel: "main", WindowLabel: "main"));
        var local = await Invoke("""{"url":"file:///etc/passwd"}""");
        Assert.Equal("error", local.Headers!["Tauri-Response"]);
        Assert.Contains("not allowed", Encoding.UTF8.GetString(local.Body.Span));
        var arbitraryProgram = await Invoke("""{"url":"https://tauri.app","with":"untrusted-program"}""");
        Assert.Equal("error", arbitraryProgram.Headers!["Tauri-Response"]);
        Assert.Contains("not implemented", Encoding.UTF8.GetString(arbitraryProgram.Body.Span));
    }
}
