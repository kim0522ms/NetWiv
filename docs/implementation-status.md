# Support and validation

NetWiv is a .NET 10 NativeAOT desktop framework under active development.
The source includes a working application SDK, native runtime and build tooling;
this does not establish complete TAURI® compatibility or release acceptance.
Package installation is described in the [quickstart](integration-quickstart.md).

## Desktop baseline

| Platform | Runtime dependencies | Scope |
| --- | --- | --- |
| Windows x64 | WebView2 Runtime | Wry desktop runtime and NativeAOT applications. |
| Linux x64 | GTK3 and WebKitGTK 4.1 | Ubuntu 24.04 is the baseline environment; X11 and Wayland require separate validation. |
| macOS 12+ arm64 / x64 | System WKWebView and AppKit | Native runtime, CLI and app/DMG support. Apple Silicon smoke verified; Intel execution remains unverified. See [macOS](macos.md). |
| Android, iOS | — | Outside the current desktop baseline. |

Applications use the native libraries delivered in `NetWiv.Platform.Desktop`.
Ordinary JS/TS and Blazor app development does not require Cargo. Native library
builds are a framework-maintainer task; Rust/WASM frontends need their own Rust
toolchain. See [native architecture](native-architecture.md).

## Application development

| Area | Available behavior | Limits |
| --- | --- | --- |
| C# SDK | Commands, state, events, channels, windows, WebViews and managed resources. | Applications must follow cancellation, concurrency and ownership contracts. |
| Frontend bindings | Generated JS calls and TS declarations from C# commands and supported JSON metadata. | Custom converters, polymorphism and referenced-command aggregation are incomplete. |
| Project creation | 18 frontend choices through `netwiv new`; a matching local CLI manifest and SDK references. | Template availability does not establish GUI acceptance for every frontend/toolchain combination. |
| Development | Frontend updates and C# binding regeneration with backend restart. | Rider and Visual Studio solution loading/debugging require further interactive validation. |
| NativeAOT | Release publication for Windows/Linux x64 and macOS arm64/x64. | Successful compilation alone does not verify a working desktop session. |
| Plugins | Managed packages for 24 desktop plugin areas. | Individual commands, permissions and platform behavior remain partial; the updater runtime is not implemented. |

Use the [C# contract](csharp-api.md), [binding contract](frontend-generator.md),
[API status table](api-status.md) and [plugin guide](official-plugins.md)
for feature-specific details. API implementation and comparison coverage are
separate measurements; declaration counts are not completion percentages.

## Runtime identity differences

NetWiv contains ports and adaptations of TAURI source code, but does not embed
or identify itself as the TAURI core runtime. On all desktop platforms:

- Omitted window titles default to `NetWiv App` for configured startup windows
  and dynamically created native windows. Explicit titles, including empty
  strings, remain unchanged. Dynamic window options still reject malformed
  title values.
- `app.getTauriVersion()` rejects with
  `NetWiv does not embed the TAURI framework; its version is unavailable.`
  The `plugin:app|tauri_version` command and its permission identifiers remain
  recognized. Returning the pinned comparison version or the NetWiv package
  version would misstate the official API's meaning. Callers must handle the
  rejection; `app.getVersion()` still returns the application's own version.
- `netwiv signer sign` labels its minisign comment as a NetWiv signature.
  The key/signature encoding and existing signing environment variables remain
  compatible; tests verify signatures and reject tampered payloads.

These are deliberate identity differences, not upstream compatibility passes.
The unchanged shared conformance cases `app.metadata`, `window.current` and
`window.configured-startup` expose these differences against the pinned runtime;
full comparison results must continue to report them. Dedicated Core, native
and desktop smoke tests verify the NetWiv behavior separately. Frontend imports,
IPC names, event names, configuration aliases and `isTauri()` bridge detection
remain available for existing guest code; bridge detection does not identify the
underlying framework as an official TAURI distribution.

## Packaging and distribution

The CLI has Windows NSIS/MSI, Linux deb/rpm/AppImage and macOS app/DMG generation paths.
Packaging requires the corresponding platform tools. Distribute the complete
publish output, including native DLL/SO/dylib files and required license notices.
Installed applications do not need Node.js, Vite or the .NET SDK.

Focused integration checks have exercised package-only NativeAOT React apps on
Windows and Linux X11, and Windows NSIS/MSI installation, execution and removal.
Apple Silicon checks additionally exercised source-built NativeAOT WKWebView and
isolation hosts, a locally ad-hoc-signed `.app`, real C# invocation/menu input,
and app/DMG packaging. macOS package-only installation has not been verified.
These checks do not establish acceptance for every package format or environment.
The following release boundaries remain open:

- Windows installation on a machine without WebView2, and actual-certificate
  signing verification. WebView2 automatic installation and Fixed Version
  bundling are not provided by the current installer path.
- macOS Intel and minimum macOS 12 execution, package-only clean-machine
  installation, Developer ID signing and notarization. Universal binaries and
  notarization automation are not implemented. Ad-hoc execution does not prove
  Gatekeeper acceptance; see [macOS limits](macos.md#validation-and-limits).
- RPM dependency resolution on an RPM-native distribution and ordinary AppImage
  FUSE mounting; extraction-based execution is a separate test.
- Installed-app restart, deep-link activation and all optional plugin OS flows.
- Full interactive window, tray, input, focus and multi-monitor acceptance.
  Linux tray behavior requires an actual tray host; WSLg alone is insufficient.
- Wayland geometry and window-state position restoration, including decorated
  windows. See [platform behavior](pinned-upstream-behavior.md).
- Performance measurements for startup, memory and application size under matched
  conditions. No performance parity or size guarantee is made.

## Compatibility and security validation

The reference versions and checksums are pinned in
[`upstream-lock.json`](../compatibility/upstream-lock.json). Public compatibility
checks use unchanged `@tauri-apps/api`; direct use of private internals is outside
the guarantee. Deliberate C# differences are documented separately.

IPC authorization, document/resource ownership, isolation and remote URL patterns
have implementation and focused tests. Complete coverage across arguments,
errors, permissions, origins and lifecycle transitions remains incomplete.
Consult [`implementation-evidence.json`](../compatibility/implementation-evidence.json)
and [upstream test coverage](upstream-business-tests.md) for the reviewed scope.
Skipped cases, unsupported operations and matching failures are not passes.

## Reproducing validation

Start with [framework development](development.md). The repository provides:

- Managed Core, API, generator and CLI suites under `tests/`.
- A NativeAOT desktop host at `tests/NetWiv.Desktop.Smoke`.
- A [differential conformance runner](../tests/conformance/README.md) and a separate
  [isolation suite](isolation.md#automatic-differential-suite).
- A [package consumer harness](frontend-generator.md#verification) for generated
  bindings, TypeScript checks and React-to-C# IPC in a real WebView.

Run on each target OS/display backend and record source revision, package and
binary hashes, toolchain versions, results and skips. Tests against an earlier
package or native library do not validate a later build. Validation instructions refer to repository sources and reproducible commands.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
