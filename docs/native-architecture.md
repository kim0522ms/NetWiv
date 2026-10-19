# Native desktop architecture

NetWiv uses wry, tao, muda and tray-icon through a versioned C ABI. The C# SDK
owns application policy and tooling; the Rust adapter owns native desktop
objects. Production templates, samples and the conformance host use
`NetWiv.Platform.Desktop`. Legacy direct platform bindings are archival source,
not an additional supported production backend.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../assets/diagrams/architecture-dark.svg">
  <img alt="NetWiv architecture: web frontend, .NET, native adapter and operating system layers" src="../assets/diagrams/architecture-light.svg" width="100%">
</picture>

## Responsibility boundary

The desktop JavaScript/TypeScript entry point is `@netwiv/api`, maintained in
`packages/api`. Generated C# command bindings import its core module. The initial
facade delegates to pinned official Tauri code; unchanged official guest tests
remain the compatibility reference. See [frontend SDK](frontend-api.md).

| .NET | Native Rust adapter |
| --- | --- |
| Public SDK, source generation, state and plugins | Window and WebView ownership |
| IPC validation, ACL, scope, CSP and assets | OS event loop and UI dispatch |
| Events, channels and managed resources | Native menus, tray and resource handles |
| CLI, configuration, MSBuild and packaging | C ABI and protocol responders |

Core callback targets include a native document generation. Navigation and
closure invalidate old command replies, event listeners and captured channels.
Native evaluation checks the generation again at execution time. C# validates
permissions before dispatching native commands; adopting TAURI® ecosystem libraries
does not automatically supply Tauri's IPC or security policy.

See the [native ABI](native-abi.md) for threading, memory ownership, callbacks
and shutdown, and [isolation](isolation.md) for the supported IPC transport.

## Internal module boundaries

The native adapter's modules divide implementation responsibilities without
introducing additional runtimes or resource stores:

| Location | Responsibility |
| --- | --- |
| `runtime.rs` | Own `LocalRuntime`, window/WebView/context maps, the event loop, callbacks and ordered shutdown. |
| `runtime/commands.rs` | Validate submission document identity and route commands to the existing handlers. |
| `runtime/window.rs`, `runtime/webview.rs` | Create native objects and implement window/WebView commands, resize, evaluation and reparenting. |
| `runtime/protocol.rs` | Track document origins and generations, construct the guest bridge and deliver asset responses through `Shared`. |
| `runtime/options.rs`, `runtime/platform.rs` | Decode existing wire values and apply OS-specific window operations. |
| `runtime/menus.rs` | Attach application/window menus, synchronize accelerators and show context menus. |
| `runtime/dialogs.rs`, `dialog/linux.rs` | Track pending Linux dialogs, asynchronous GTK responses, cancellation and document/window/app cleanup. |
| `resources.rs` | Own the single native RID allocator/table, route resource commands and close resources/channels. |
| `resources/menus.rs`, `resources/trays.rs`, `resources/images.rs` | Implement each resource kind against that same table. |

Child modules are private. Their handlers operate on the existing owner rather
than acquiring another copy of its state. `LocalRuntime` and native desktop
objects remain on the event-loop thread; only the existing `Shared` callback,
protocol and event-proxy state crosses threads. Keep document invalidation,
WebView-before-WebContext destruction, resource alias ownership and asset
responder draining in their current order when extending these modules.

The C# `CommandGenerator` similarly uses private partial declarations:
`CommandGenerator.cs` wires the incremental pipeline and coordinates output;
`Analysis`, `Diagnostics`, `Models`, `Plugins` and `Emission` contain semantic
validation, diagnostic definitions, internal models, plugin discovery and C#
output respectively. These are parts of one compile-time generator, not runtime
services. Frontend binding emission remains in `FrontendBindingEmitter`.
Generated registrations, serializers, DI invocation scopes and disposal behavior
must remain NativeAOT-safe.

For a structural change, run the native unit tests, generator tests, managed and
NativeAOT core tests, and desktop smoke scenarios. Include stale-document replies,
reparenting, menu aliases, resource closure and invalid payloads; unchanged ABI
declarations alone do not establish unchanged behavior across C#, Rust and JS.
See [contributing](../CONTRIBUTING.md#validation-and-real-device-evidence) for
real-device evidence requirements.

## Windows lifecycle behavior

Initial WebView focus is best effort. Windows can reject `MoveFocus` with
`0x80070057` when the parent is minimized; this must not discard an otherwise
valid WebView controller. NetWiv creates the controller without initial focus,
then attempts focus unless `focus: false` was requested. This follows the
[upstream WRY fix](https://github.com/tauri-apps/wry/pull/1799) while retaining
the pinned dependency version. Explicit `SetFocusAsync()` calls continue to
report native focus errors. Creating a child does not restore a minimized parent.

TAO is pinned to 0.36.0 to include the upstream
[TaskbarCreated deadlock fix](https://github.com/tauri-apps/tao/pull/1264).
The window-state lock must be released before taskbar COM calls, which can
re-enter the window procedure. Desktop smoke exercises concurrent taskbar
recreation messages on its own window, without restarting Explorer.

## Builds and delivery

Framework maintainers build native binaries explicitly for each RID and package
them in `NetWiv.Platform.Desktop`. Ordinary NuGet consumers do not build the Rust
adapter or download native libraries at startup. Use matching SDK and native
package versions; older native libraries may reject newer run-config fields even
within the same ABI version.

Desktop targets include Windows x64 with WebView2, Linux x64 with GTK3 and
WebKitGTK 4.1, and macOS arm64/x64 with WKWebView/AppKit. See [macOS](macos.md)
for the distinction between implementation and completed validation. [Framework development](development.md) describes native builds
and package creation. [Support and validation](implementation-status.md) lists
remaining acceptance boundaries; native library reuse is not a full Tauri
compatibility claim.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
