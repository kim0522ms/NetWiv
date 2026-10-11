# Native desktop architecture

NetWiv uses wry, tao, muda and tray-icon through a versioned C ABI. The C# SDK
owns application policy and tooling; the Rust adapter owns native desktop
objects. Production templates, samples and the conformance host use
`NetWiv.Platform.Desktop`. Legacy direct platform bindings are archival source,
not an additional supported production backend.

## Responsibility boundary

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
