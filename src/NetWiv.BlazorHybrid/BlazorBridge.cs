using System.Text.Json;

namespace NetWiv.BlazorHybrid;

internal static class BlazorBridge
{
    internal const string Prefix = "__NETWIV_BLAZOR__";

    // Microsoft's blazor.webview.js expects the same two transport functions on each platform.
    // The embedded script and wire format are pinned to Microsoft's package.
    internal const string Script = """
        (() => {
          let receive;
          let active = true;
          const external = window.external || {};
          external.sendMessage = message => { if (active) window.__NETWIV_IPC__.postMessage('__NETWIV_BLAZOR__' + message); };
          external.receiveMessage = callback => { receive = callback; };
          Object.defineProperty(window, 'external', { value: external, configurable: true });
          Object.defineProperty(window, '__netwivBlazorReceiveMessage', {
            value: message => { if (active && receive) receive(message); }, configurable: false
          });
          // A cached DOM belongs to a retired .NET renderer. Restore through the
          // normal host bootstrap instead of replaying that renderer's messages.
          window.addEventListener('pagehide', () => { active = false; });
          window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
        })();
        """;

    internal static string GetReceiveScript(string message) =>
        $"window.__netwivBlazorReceiveMessage({JsonSerializer.Serialize(message, BlazorJsonContext.Default.String)});";

    internal static string GetInitializationScript(string webviewLabel, Uri appBaseUri)
    {
        static string Json(string value) => JsonSerializer.Serialize(value, BlazorJsonContext.Default.String);
        // Custom schemes can have the opaque origin "null" in WebKit. Compare URL components;
        // the native transport separately authenticates the main-frame URI and document identity.
        return "(()=>{const source=new URL(location.href);if(window===window.top&&window.__NETWIV_IPC__?.webviewLabel===" + Json(webviewLabel) +
            "&&source.protocol===" + Json(appBaseUri.Scheme + ":") +
            "&&source.hostname===" + Json(appBaseUri.IdnHost) +
            "&&source.port===" + Json(appBaseUri.IsDefaultPort ? "" : appBaseUri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
            "&&source.username===\"\"&&source.password===\"\"){" + Script + "}})();";
    }
}
