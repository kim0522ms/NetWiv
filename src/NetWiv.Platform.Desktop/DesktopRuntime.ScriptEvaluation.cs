using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv.Platform.Desktop;

public sealed partial class DesktopRuntime
{
    public async Task<JsonElement> EvaluateScriptAsync(string webviewLabel, string script, ulong documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webviewLabel);
        ArgumentNullException.ThrowIfNull(script);
        if (documentId == 0) throw new ArgumentOutOfRangeException(nameof(documentId), "Script results require a live document.");
        var args = JsonValue.Parse("{\"script\":" + JsonValue.From(script).GetRawText() + "}");
        var context = new RuntimeCommandContext(webviewLabel, new Uri("netwiv://native"), true, DocumentId: documentId, IsBackend: true);
        var response = await InvokeResponseAsync("netwiv:evaluate_script", args, context, cancellationToken).ConfigureAwait(false);
        var value = response.Json;
        if (value.GetProperty("kind").GetString() == "error")
            throw new JavaScriptException(value.GetProperty("name").GetString()!, value.GetProperty("message").GetString()!,
                value.TryGetProperty("stack", out var stack) && stack.ValueKind == JsonValueKind.String ? stack.GetString() : null);
        return value.GetProperty("value").Clone();
    }
}
