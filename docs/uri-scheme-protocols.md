# Custom URI scheme protocols

Register custom protocol handlers before building the application:

```csharp
using NetWiv;
using NetWiv.Runtime;

builder.RegisterUriSchemeProtocol("example", (context, request) =>
    new AssetResponse(200, "text/plain", "Hello from C#"u8.ToArray()));

builder.RegisterAsynchronousUriSchemeProtocol("data-store", async (context, request) =>
{
    var bytes = await File.ReadAllBytesAsync("data.bin");
    return new AssetResponse(200, "application/octet-stream", bytes);
});
```

`UriSchemeContext.AppHandle` and `.WebviewLabel` identify the app and originating
WebView. `AssetRequest` contains URI, method, headers, binary body and native
document metadata. An `AssetResponse` supplies status, content type, headers and
binary body. Returning a `Task<AssetResponse>` replaces Rust's asynchronous
responder ownership. Completion after native shutdown releases managed state
without calling a destroyed native responder. Handler exceptions produce HTTP 500.
Synchronous work runs on the callback thread; use asynchronous I/O or explicitly
offload CPU work for a long operation.

Use unchanged `convertFileSrc('path', 'example')` from `@tauri-apps/api/core` to
construct frontend URLs. Windows uses `http://example.localhost/path` by default
(or HTTPS when configured); Linux/macOS use `example://localhost/path`. Cross-origin
fetches still need response CORS headers and the configured page CSP must permit
them. Custom HTML responses are supplied as returned by the handler.

Registered protocols count as local for IPC authorization. On Windows the host
must be exactly `<registered-scheme>.localhost` over HTTP(S); a lookalike external
domain does not qualify. On Linux/macOS the URI scheme must be registered. Application
manifest and capability requirements still apply. Registration replaces an earlier
handler of the same name and snapshots when `Build` runs. Custom `tauri`, `asset`
and `ipc` handlers replace those default handlers, following upstream's precedence.

The C ABI is version 2; its JSON run configuration includes
`uriSchemeProtocols`. An older library rejects an unknown field at startup, so
SDK and native package versions must match. The desktop fixtures `protocol.custom-handlers` and `protocol.custom-document`
compare binary POST responses, asynchronous completion, context labels and IPC
from a second custom-protocol WebView against pinned Tauri.

On macOS, native responses are delivered through the AppKit event queue. A
managed worker does not synchronously wait for the UI while holding the runtime
lifetime lock. The NativeAOT WKWebView smoke exercises asynchronous asset/IPC
responses and shutdown. This does not establish complete custom-protocol
conformance against Tauri on macOS.
