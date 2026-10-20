# NetWiv native ABI v3

The authoritative declarations are `native/netwiv-native/include/netwiv.h`.
All exports use the C calling convention. ABI version is 3. Nonzero status means
failure; `netwiv_last_error` returns a UTF-8 error owned by the caller and released
with `netwiv_buffer_free`. No exception or panic may cross the boundary.

The .NET runtime creates one opaque runtime, calls run on the application's main
STA/UI thread, and destroys it after run returns and all exported calls finish.
On macOS this must be the process main thread; worker-thread startup returns an error.
Run owns the event loop and every native window, webview, menu and tray object.
Submit, cancel, dispatch, eval, respond_asset and exit copy their input and may be called
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
Both options were introduced as additive ABI 2 JSON fields; older native builds reject unknown
fields rather than silently running without the requested protocol.
Core resolves the two
data roots separately: the first is the OS local data root, the second includes
the app identifier. They must not be interchanged when applying TAURI® config.
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
replace `isAuthorized` or grant a capability. This distinction was introduced in
ABI 2; the current ABI version check also requires the cancellation export added
in ABI 3. Rebuild and distribute matching managed and native adapters together.

`netwiv_runtime_cancel(runtime, request_id)` queues cancellation of a pending Linux
GTK dialog on the UI thread. IDs must be nonzero; unknown or completed requests
are no-ops. A successful return only acknowledges queueing. A cancelled dialog
completes with `{ok:false,cancelled:true,error:string}`; dismissing a dialog through
its UI retains its ordinary result. Windows and macOS currently use synchronous
dialogs and this cancellation event is a no-op there.

The managed adapter forwards caller-token cancellation only for `plugin:dialog|open`,
`plugin:dialog|save` and `plugin:dialog|message`, and marks their tasks cancelled
after native cancellation is confirmed. Tokens cancelled before submission reject
the call immediately. Other accepted UI mutations retain their results, including
created resource handles, even if their caller later cancels. Cancellation
registrations are disposed after completion outside the runtime lock.

Callback kinds:

1. IPC: `{webviewLabel,windowLabel,source,documentId,json}` where json is the
   original guest message string after removing the private transport envelope.
   Labels, document identity and source authority are owned by native code.
   Windows and macOS use native sender metadata. On Linux, WebKitGTK's raw
   message callback does not identify its sending frame; native code first
   authenticates the main document's transport, then supplies its native current
   URI as source, with its origin anchored to the committed document origin.
   During provisional navigation, the authenticated document's
   source snapshot remains in effect until a replacement commits.
   Unauthenticated, retired or mismatched-document messages do not reach this
   callback. A custom scheme's opaque JavaScript origin does not
   grant authority: the authenticated native current URI supplies source identity.
2. Event: `{eventName,label,payload,target?}`. For `tauri://close-requested`, the
   callback returns 1 when Core has matching listeners, preventing default close.
   With `trackWebviewParents`, `netwiv://webview-reparented` has the WebView label
   and `{windowLabel}` payload. Core consumes this adapter-only notification
   without emitting a guest or plugin event. It precedes operation completion.
   New managed adapters require this config flag; older native builds reject the
   unknown field at startup. Native IPC and protocol requests always resolve the
   current parent from the owned document registry after a successful move.
3. Completion: `{ok:true,value:JSON,isBinary:false}` or `{ok:true,isBinary:true}`
   with binary span, or `{ok:false,error:JSON}`. The optional `cancelled:true`
   marker identifies acknowledged dialog cancellation and maps to a cancelled
   managed task. Exactly one per accepted submit while the runtime remains active.
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
Core uses the bridge's private `fetchIpc` on Linux and ordinary fetch on
Windows/macOS against the IPC protocol, returning JSON, text or binary from
its HTTP response. A browser/CSP transport failure switches that document to Wry
postMessage; its body/header semantics follow the pinned Tauri fallback.
Frame/origin authenticity is an acceptance requirement, not established merely
by trusting guest-provided labels, a page URL, or an invoke token.

On Linux, the main-frame bridge captures the transport in a closure and binds
it to a native-generated cryptographic capability and document epoch. During
provisional navigation, the current document retains its binding and source
snapshot so outstanding renderer acknowledgments and interop can complete.
Commit rotates the binding and a document handshake activates the new bridge.
Committed replacement and close invalidate the previous binding.
On `pagehide`, the captured JavaScript bridge clears its active grant and
changes its handshake identity. If a browser history entry restores that same
JavaScript context, ordinary Core calls wait for a fresh native grant before
dispatch. Hybrid separately uses a [full reload on cached restoration](blazor-hybrid.md#page-lifetime-and-cleanup).
Native code also checks the committed origin before forwarding a message. This
applies to both postMessage and IPC HTTP requests. Private authentication
metadata is removed before forwarding HTTP headers to managed code; preflight
requests do not dispatch commands. The captured fetch transport accepts only
the private IPC endpoint and rejects redirects. It captures native URL accessors
before application scripts run and passes a validated URL string to fetch;
changing `URL.prototype` cannot redirect authentication headers.
Credential-bearing envelopes, headers and fetch options have null prototypes,
so inherited serialization hooks, header iterators and option getters cannot
observe those credentials.
For an authenticated HTTP
request, native code supplies the authenticated native current document URI as
the Origin header while anchoring its origin to the committed document. An opaque custom-scheme
browser origin cannot determine command authority.
Hostless documents such as `about:` and `data:` require an exact parsed URL
match alongside the capability and epoch. They do not share authority merely
because their JavaScript origins serialize to `null`; Core command permissions
still apply.
The native `about:blank` message path is covered by a Linux browser regression.
Some `data:` URLs fail in the pinned Wry WebKitGTK request-URI parser before
NetWiv receives the message; hostless authentication does not remove that
upstream limitation or establish `data:` browser IPC support.

Linux path-based capability checks use the committed source path while a
document is loading or navigation is provisional. Same-origin history changes
made during loading become visible to these checks after successful load
completion and native activation; history changes in an idle document are
reflected immediately. Failed or cancelled navigation retains the previous
authenticated source and never adopts the uncommitted target URI. This timing preserves
the authenticated document's source while a provisional target is uncommitted.
Applications must not rely on an early history mutation to change command
permissions before loading finishes.

Raw `window.ipc` messages on Linux are outside this authenticated path and are
rejected. Applications continue to use `@netwiv/api`; `__NETWIV_IPC__`, transport
envelopes and authentication handshakes are private implementation details.
An application may register `ipc` as a custom URI scheme. That registration
owns its document and asset requests on every platform; Core commands use the
message transport instead of the built-in HTTP transport. An unhandled custom
request falls back to asset resolution, never to built-in command dispatch.
Private document capability headers are removed before custom handlers run,
and Linux's credential-bearing fetch helper is disabled for that registration.
Windows per-frame sender authorization and macOS native sender checks retain
their existing behavior. This hardening does not change ABI 3's external
callback or protocol payload format.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
