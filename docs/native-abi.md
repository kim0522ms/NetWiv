# NetWiv native ABI v2

The authoritative declarations are `native/netwiv-native/include/netwiv.h`.
All exports use the C calling convention. ABI version is 2. Nonzero status means
failure; `netwiv_last_error` returns a UTF-8 error owned by the caller and released
with `netwiv_buffer_free`. No exception or panic may cross the boundary.

The .NET runtime creates one opaque runtime, calls run on the application's main
STA/UI thread, and destroys it after run returns and all exported calls finish.
On macOS this must be the process main thread; worker-thread startup returns an error.
Run owns the event loop and every native window, webview, menu and tray object.
Submit, dispatch, eval, respond_asset and exit copy their input and may be called
from other threads while run is active. Destroy must not race these calls.
Callbacks borrow byte spans only for their duration; managed code copies before
returning. No native callback may occur after run returns. Only native code frees
native allocations. Submission accepted does not imply operation succeeded.

Run config is `{windows:[{label,title,url,width,height,extraConfig,webviews}],
initializationScript:string,defaultWindowIcon?:{width,height,rgba},
localDataDirectory?:string,appLocalDataDirectory?:string,assetProtocolEnabled?:bool,
isolationProtocol?:string,ipcProtocolEnabled?:bool,uriSchemeProtocols?:string[],
trackWebviewParents?:bool}`.
The optional asset switch defaults to false; Core owns the file scope and HTTP
response policy. Native code registers the `asset` scheme only when enabled.
`isolationProtocol` registers the per-app randomized isolation scheme. Its
opaque iframe, encryption key, CSP and decryption remain owned by Core.
`ipcProtocolEnabled` registers the `ipc` HTTP transport and defaults to false.
Both options are additive ABI 2 JSON fields; older native builds reject unknown
fields rather than silently running without the requested protocol.
Core resolves the two
data roots separately: the first is the OS local data root, the second includes
the app identifier. They must not be interchanged when applying Tauri config.
The optional
icon contains RGBA bytes as JSON integers and is copied during initialization.
Webview entries are
`{label,url,x,y,width,height,extraConfig}`. Core supplies JSON via explicit writers.
URLs relative to bundled assets use `tauri://localhost/` on Linux/macOS and
`http://tauri.localhost/` on Windows, or `https://tauri.localhost/` when that
WebView enables `useHttpsScheme` (wry custom protocol convention).

Submit payload is `{command,args,webviewLabel,windowLabel,source,isAuthorized,scope,documentId,isBackend}`.
Only Core-authorized native commands may execute. Native code implements the
platform operation; it does not duplicate the capability policy. Command errors
are completion errors, never fabricated successful results. Optional binary
payload is separate from JSON. Request IDs are unique within a runtime.

`isBackend` distinguishes direct trusted C# calls from authorized guest calls;
Core derives it from its invocation path, never from guest JSON. Backend menu
and tray creation may omit a guest Channel and use the native event hooks.
Guest creation still requires the upstream handler shape. This flag does not
replace `isAuthorized` or grant a capability. ABI 2 prevents new managed code
from silently pairing with older native adapters that lack this distinction.

Callback kinds:

1. IPC: `{webviewLabel,windowLabel,source,documentId,json}` where json is the
   original guest message string and source comes from the native IPC request.
2. Event: `{eventName,label,payload,target?}`. For `tauri://close-requested`, the
   callback returns 1 when Core has matching listeners, preventing default close.
   With `trackWebviewParents`, `netwiv://webview-reparented` has the WebView label
   and `{windowLabel}` payload. Core consumes this adapter-only notification
   without emitting a guest or plugin event. It precedes operation completion.
   New managed adapters require this config flag; older native builds reject the
   unknown field at startup. Native IPC and protocol requests always resolve the
   current parent from the owned document registry after a successful move.
3. Completion: `{ok:true,value:JSON,isBinary:false}` or `{ok:true,isBinary:true}`
   with binary span, or `{ok:false,error:JSON}`. Exactly one per accepted submit.
4. Dispatch: request ID identifies a managed action to execute on the UI thread.
5. Protocol: request ID and `{uri,method,headers,windowOrigin?,webviewLabel,windowLabel,documentId}`, with request body in the binary
   span. Managed resolver responds with
   `{statusCode,contentType,headers}` plus body via respond_asset. Native code
   holds the protocol responder until that response or runtime teardown. The
   origin is the WebView's native initial URL origin, looked up by Wry's WebView
   ID even when Linux WebViews share a context. It is not the request's Origin
   header. Empty `contentType` omits that header; a headers entry may append a
   second Content-Type, as required by the pinned multipart asset response.
   Responses are delivered through the UI event queue; exported respond_asset calls
   never synchronously wait for AppKit. The same callback carries IPC HTTP requests. C# may respond asynchronously
   after command completion; the native loop must stay unblocked. Labels and
   document IDs come from the Wry WebView ID lookup, never request JSON.
   Late completion after runtime shutdown is discarded, with no callback.
6. Document: `{webviewLabel,documentId,closed:false}` on navigation, or
   `{webviewLabel,documentId,closed:true}` on destruction. IDs change on document
   replacement. IPC replies and callbacks use eval with their captured ID; stale
   IDs must never evaluate in a newer document. ID 0 explicitly targets current.
7. Fatal: `{error:string}`. The runtime stops cleanly and run fails.
8. Ready: `{backend:"windows"|"x11"|"wayland"|"macos"}` once on the UI thread when the initial native objects exist and
   the event proxy accepts work. Core runs plugin and application Setup here.
   The backend value reports the actual native display backend, not an environment-variable guess.

Callbacks must not block waiting for queued UI work. Native close callbacks are
synchronous only for determining whether to prevent the default close action.
All other operations complete asynchronously through the event loop. Shutdown
rejects pending managed requests and releases protocol responders and callbacks.

The guest bridge sets `window.__NETWIV_IPC__` before Core's initialization script,
with postMessage, os, windowLabel, webviewLabel, protocolScheme and protocolIpc. The frozen
protocolScheme selects HTTP or HTTPS in convertFileSrc on Windows. When enabled,
Core uses fetch against the IPC protocol and returns JSON, text or binary from
its HTTP response. A browser/CSP transport failure switches that document to Wry
postMessage; its body/header semantics follow the pinned Tauri fallback.
Frame/origin authenticity is an acceptance requirement, not established merely
by trusting guest-provided labels, a page URL, or an invoke token.
