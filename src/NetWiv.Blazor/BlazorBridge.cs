using System.Text.Json;

namespace NetWiv.Blazor;

internal static class BlazorBridge
{
    internal const string Prefix = "__NETWIV_BLAZOR__";

    // Microsoft's blazor.webview.js expects the same two transport functions on each platform.
    // The renderer and wire format remain entirely owned by Microsoft's package.
    internal const string Script = """
        (() => {
          let receive;
          const external = window.external || {};
          external.sendMessage = message => window.__NETWIV_IPC__.postMessage('__NETWIV_BLAZOR__' + message);
          external.receiveMessage = callback => { receive = callback; };
          Object.defineProperty(window, 'external', { value: external, configurable: true });
          Object.defineProperty(window, '__netwivBlazorReceiveMessage', {
            value: message => { if (receive) receive(message); }, configurable: false
          });
        })();
        """;

    internal static string GetReceiveScript(string message) =>
        $"window.__netwivBlazorReceiveMessage({JsonSerializer.Serialize(message)});";
}
