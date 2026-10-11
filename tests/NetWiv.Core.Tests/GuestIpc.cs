using System.Text;
using NetWiv;
using NetWiv.Runtime;

internal static class GuestIpc
{
    internal static Task<AssetResponse> Invoke(FakeRuntime runtime, string command, string payload, ulong documentId = 0, string webviewLabel = "main")
    {
        const string marker = "headers.set('Tauri-Invoke-Key', '";
        var script = runtime.Context!.InitializationScript;
        var key = script.Substring(script.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tauri-Invoke-Key"] = key, ["Tauri-Callback"] = "1", ["Tauri-Error"] = "2",
            ["Origin"] = "tauri://localhost", ["Content-Type"] = "application/json"
        };
        return runtime.Context.InvokeProtocol!(new(new Uri("ipc://localhost/" + Uri.EscapeDataString(command)),
            "POST", headers, Encoding.UTF8.GetBytes(payload), WebviewLabel: webviewLabel, WindowLabel: webviewLabel, DocumentId: documentId)).AsTask();
    }

    internal static string Body(AssetResponse response) => Encoding.UTF8.GetString(response.Body.Span);
}
