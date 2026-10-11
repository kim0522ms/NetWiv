using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class AppHandle
{
    private async ValueTask<AssetResponse> OnProtocolInvokeAsync(AssetRequest request)
    {
        static AssetResponse Respond(int status, string type, ReadOnlyMemory<byte> body, string? result = null, bool preflight = false)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Access-Control-Allow-Origin"] = "*", ["Access-Control-Expose-Headers"] = "Tauri-Response"
            };
            if (result is not null) headers["Tauri-Response"] = result;
            if (preflight) headers["Access-Control-Allow-Headers"] = "*";
            return new(status, type, body, headers);
        }
        if (request.Method == "OPTIONS") return Respond(200, "", ReadOnlyMemory<byte>.Empty, preflight: true);
        if (request.Method != "POST") return Respond(405, "text/plain", "only POST and OPTIONS are allowed"u8.ToArray());

        JsonElement args;
        InvokeBody body;
        string command;
        Uri source;
        try
        {
            if (request.WebviewLabel is null || !IsCurrentDocument(request.WebviewLabel, request.DocumentId))
                throw new InvokeException("failed to acquire webview reference");
            lock (_registryGate)
                if (!_webviews.ContainsKey(request.WebviewLabel)) throw new InvokeException("failed to acquire webview reference");
            command = Uri.UnescapeDataString(request.Uri.AbsolutePath[1..]);
            var contentType = request.Headers.GetValueOrDefault("content-type") ?? "application/octet-stream";
            if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedType)) throw new InvokeException("unknown content type");
            contentType = parsedType.ToString().ToLowerInvariant();
            var bytes = request.Body;
            if (Isolation is not null && !bytes.IsEmpty)
            {
                using var envelope = JsonDocument.Parse(bytes);
                var innerType = envelope.RootElement.GetProperty("contentType").GetString();
                contentType = MediaTypeHeaderValue.TryParse(innerType, out var innerMime) ? innerMime.ToString().ToLowerInvariant() : "application/octet-stream";
                var decrypted = Isolation.Decrypt(envelope.RootElement);
                bytes = contentType == "application/octet-stream"
                    ? decrypted.EnumerateArray().Select(static value => value.GetByte()).ToArray()
                    : Encoding.UTF8.GetBytes(decrypted.GetRawText());
            }
            if (Header("Tauri-Invoke-Key") != _invokeKey) throw new InvokeException("invalid invoke key");
            if (!Uri.TryCreate(Header("Origin"), UriKind.Absolute, out source!)) throw new InvokeException("Origin header is not a valid URL");
            Callback("Tauri-Callback", "Tauri callback");
            Callback("Tauri-Error", "Tauri error");
            if (contentType == "application/octet-stream")
            {
                body = InvokeBody.FromBytes(bytes.Span);
                args = JsonValue.Write(writer => { writer.WriteStartArray(); foreach (var value in bytes.Span) writer.WriteNumberValue(value); writer.WriteEndArray(); });
            }
            else if (contentType == "application/json")
            {
                using var document = JsonDocument.Parse(bytes.IsEmpty ? "{}"u8.ToArray() : bytes);
                args = document.RootElement.Clone();
                body = InvokeBody.FromJson(args);
            }
            else throw new InvokeException($"content type {contentType} is not implemented");
        }
        catch (Exception error)
        {
            CoreLog.RejectedRequest(_ipcLogger, request.WebviewLabel, error.Message);
            return Respond(500, "text/plain", Encoding.UTF8.GetBytes(error.Message));
        }

        try
        {
            var response = await InvokeCoreAsync(command, args, request.WindowLabel ?? request.WebviewLabel!, request.WebviewLabel!, source,
                false, CancellationToken.None, request.DocumentId, new Request(body, request.Headers));
            return response.IsBinary ? Respond(200, "application/octet-stream", response.Binary, "ok") :
                Respond(200, "application/json", Encoding.UTF8.GetBytes(response.Json.GetRawText()), "ok");
        }
        catch (Exception error)
        {
            var payload = error is InvokeException invokeError ? invokeError.ErrorPayload : JsonValue.From(error.Message);
            return Respond(200, "application/json", Encoding.UTF8.GetBytes(payload.GetRawText()), "error");
        }

        string Header(string name) => request.Headers.TryGetValue(name, out var value) ? value : throw new InvokeException($"missing {name} header");
        void Callback(string name, string display)
        {
            if (!uint.TryParse(Header(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                throw new InvokeException($"{display} header value must be a numeric string");
        }
    }
}
