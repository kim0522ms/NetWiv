# Support and validation

NetWiv is a .NET 8 or later desktop framework with optional NativeAOT publishing.
The source includes a working application SDK, native runtime and build tooling;
this does not establish complete TAURI® compatibility or release acceptance.
Package installation is described in the [quickstart](integration-quickstart.md).

## Desktop baseline

| Platform | Runtime dependencies | Scope |
| --- | --- | --- |
| Windows x64 | WebView2 Runtime | Wry desktop runtime and NativeAOT applications. |
| Linux x64 | GTK3 and WebKitGTK 4.1 | X11 and native Wayland. Ubuntu 24.04 is the target baseline; selected managed/NativeAOT Wayland automated and interactive checks passed on Ubuntu 22.04 GNOME. See [Linux setup](linux.md) and [validation limits](#wayland-validation). |
| macOS 12+ arm64 / x64 | System WKWebView and AppKit | Native runtime, CLI and app/DMG support. Hosted NativeAOT integration and package-consumer checks passed on both architectures; selected physical Apple Silicon GUI checks passed. Physical Intel and minimum-OS acceptance remain unverified. See [macOS](macos.md). |
| Android, iOS | — | Excluded from project work scope; not a later milestone. |

Applications use the native libraries delivered in `NetWiv.Platform.Desktop`.
Ordinary JS/TS and Blazor app development does not require Cargo. Native library
builds are a framework-maintainer task; Rust/WASM frontends need their own Rust
toolchain. See [native architecture](native-architecture.md).

## Application development

| Area | Available behavior | Limits |
| --- | --- | --- |
| C# SDK | Commands, state, events, channels, windows, WebViews and managed resources. | Applications must follow cancellation, concurrency and ownership contracts. |
| Frontend SDK | Desktop `@netwiv/api` modules, initially delegating to pinned official Tauri code. New bindings and starters use this facade. | Mobile APIs are excluded. Independent frontend transport is not implemented; direct upstream guests are no longer a supported entry point; plugin guests use `/plugins/*`. See [frontend SDK](frontend-api.md). |
| Frontend bindings | Generated JS calls and TS declarations from local and opted-in referenced C# commands and supported JSON metadata. | Explicit tagged polymorphic roots and sealed variants are supported; custom converters, fallback polymorphism and other advanced JSON mappings remain incomplete. Referenced producers need compatible generated metadata; arbitrary dependency discovery is not supported. |
| Project creation | 18 frontend choices through `netwiv new`; a matching local CLI manifest and SDK references. | Template availability does not establish GUI acceptance for every frontend/toolchain combination. |
| Development | Frontend updates and C# binding regeneration with backend restart. | Rider and Visual Studio solution loading/debugging require further interactive validation. |
| NativeAOT | Release publication for Windows/Linux x64 and macOS arm64/x64. | Successful compilation alone does not verify a working desktop session. |
| Plugins | Managed packages for 24 desktop plugin areas. | Individual commands, permissions and platform behavior remain partial; the updater runtime is excluded from project scope. |

Use the [C# contract](csharp-api.md), [binding contract](frontend-generator.md),
[API status table](api-status.md) and [plugin guide](official-plugins.md)
for feature-specific details. API implementation and comparison coverage are
separate measurements; declaration counts are not completion percentages.
The [core reclassification](api-status.md#implementation-decisions-and-validation-backlog)
separates execution paths, declarations, mobile-only gaps and intentional
differences. Existing macOS adapters with pending GUI checks are not classified
as missing implementations. This does not mark those GUI checks as passed.

## Wayland validation

The source-built desktop smoke passed in managed and NativeAOT modes on a
physical Ubuntu 22.04 x64 GNOME Wayland desktop using its existing logged-in
session, compositor, D-Bus session and tray host. This does not validate the
Ubuntu 24.04 baseline. Both runs asserted the runtime's `wayland` backend;
a deliberate `x11` expectation failed. The automated checks exercise
window/WebView lifecycle, IPC and assets, menu/tray resource
operations and C# SDK calls. They establish that bounded application path;
they do not establish complete Wayland or TAURI compatibility.

Paired comparisons against the same Release Tauri reference passed all 32
selected cases with no skips for both managed and NativeAOT NetWiv hosts.
Both sides asserted the `wayland` backend. This is a focused selection from
the 63-case suite, not a full conformance gate; matching outputs apply only
to the selected cases and do not prove unsupported OS effects.

The [Wayland interaction probe](../tests/NetWiv.Wayland.Smoke/README.md) provides
manual checks for native menu/tray actions, focused keyboard input, Korean IME
composition, window resizing/maximize/restore, focus, dragging, rendering and
cleanup. Both managed and NativeAOT execution passed its automatic assertions
with actual trusted input, the user's explicit visual confirmations and
completed resource cleanup in the desktop above. Multi-monitor placement,
fractional scaling, other IMEs/compositors and exact decorated-window geometry
remain unverified.

Pinned tao marks always-on-top/bottom and absolute window positioning as
unsupported on Wayland; global cursor coordinates return `(0, 0)`.
Matching setter/getter results or no-ops in `window.native-flags` do not prove
compositor stacking effects. These operations, window-state position
restoration and accurate global window coordinates are outside this native
Wayland baseline. The smoke's finite-coordinate assertion does not verify
coordinate accuracy. The current global-shortcut dependency uses an independent
X11 connection and may use XWayland when `DISPLAY` is available; registration
checks do not establish native Wayland global shortcuts or inactive-app input
delivery. These limits and unsupported operations are not compatibility passes.
See [platform behavior](pinned-upstream-behavior.md#wayland-client-side-decorations-and-resize-geometry)
and the [reproduction commands](development.md).

The existing nested X11 automated smoke also passed. That offscreen regression
check does not establish physical X11 desktop interaction acceptance.

Dialog plugin acceptance is separate from the desktop-core comparison above.
Linux uses GTK response callbacks and tracks pending dialogs across caller
cancellation, parent/WebView destruction, navigation and app shutdown. Use the
[dialog probe](../tests/NetWiv.Dialog.Smoke/README.md) for its automated and manual
checks. On the same Ubuntu 22.04 GNOME desktop, managed/NativeAOT interactive
checks passed file/folder selection, save, custom message buttons, cancellation
and scope-granted file access. Pending-request cancellation and lifecycle cleanup
also passed on Wayland and nested X11. These checks do not establish other
portal/compositor behavior or full upstream dialog compatibility.
See [plugin behavior](official-plugins.md#select-read-and-write-a-file)
and [ABI 3 migration](native-abi.md).

## Runtime identity differences

NetWiv contains ports and adaptations of TAURI source code, but does not embed
or identify itself as the TAURI core runtime. On all desktop platforms:

- Omitted window titles default to `NetWiv App` for configured startup windows
  and dynamically created native windows. Explicit titles, including empty
  strings, remain unchanged. Dynamic window options still reject malformed
  title values.
- The official compatibility API's `app.getTauriVersion()` rejects with
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
and desktop smoke tests verify the NetWiv behavior separately.
IPC names, event names and configuration aliases remain internal compatibility
mechanisms. Applications use only `@netwiv/api`, including plugin guests. Runtime
identity helpers and markers (`isTauri`, `isNetWiv`) are removed; see the
[frontend migration guide](frontend-api.md).

## Packaging and distribution

The CLI has Windows NSIS/MSI, Linux deb/rpm/AppImage and macOS app/DMG generation paths.
NSIS and MSI share [installation scope, directory and shortcut settings](windows-installers.md),
with actual Windows lifecycle acceptance in CI; interactive directory browsing
and standard-user UAC consent remain manual acceptance items.
Packaging requires the corresponding platform tools. Distribute the complete
publish output, including native DLL/SO/dylib files and required license notices.
Installed applications do not need Node.js, Vite or the .NET SDK.

Focused integration checks have exercised package-only NativeAOT React apps on
Windows and Linux X11, and Windows NSIS/MSI installation, execution and removal.
Apple Silicon checks additionally exercised source-built NativeAOT WKWebView and
isolation hosts, a locally ad-hoc-signed `.app`, real C# invocation/menu input,
and app/DMG packaging. Hosted release checks additionally passed package-only
managed React execution and windowless NativeAOT consumer execution on both
macOS architectures. Clean-machine installation and package-only NativeAOT GUI
acceptance are separate from those checks.
These checks do not establish acceptance for every package format or environment.
The following release boundaries remain open:

- Windows installation on a machine without WebView2, and actual-certificate
  signing verification. WebView2 automatic installation and Fixed Version
  bundling are not provided by the current installer path.
- Physical macOS Intel GUI and minimum macOS 12 execution, package-only clean-machine
  installation and Developer ID signing. Universal binaries are not implemented.
  Ad-hoc execution does not prove
  Gatekeeper acceptance; see [macOS limits](macos.md#validation-and-limits).
- RPM dependency resolution on an RPM-native distribution and ordinary AppImage
  FUSE mounting; extraction-based execution is a separate test.
- Installed-app restart, deep-link activation and all optional plugin OS flows.
- Full interactive window, tray, input, focus and multi-monitor acceptance.
  Linux tray behavior requires an actual tray host; WSLg alone is insufficient.
- Exact Wayland decorated-window geometry. Global positioning, position
  restoration and native global shortcuts are outside the verified baseline;
  see [Wayland limits](#wayland-validation).
- Performance measurements for startup, memory and application size under matched
  conditions. No performance parity or size guarantee is made.

Mobile targets, the updater runtime and expansion of distribution notarization
are excluded work, not outstanding release tasks. Notarization automation is not
implemented; existing signing support does not establish notarized distribution.

## Compatibility and security validation

The reference versions and checksums are pinned in
[`upstream-lock.json`](../compatibility/upstream-lock.json). Internal behavior
comparisons use unchanged `@tauri-apps/api`; applications use the NetWiv facade
only. Direct upstream imports and private internals have no public guarantee. Deliberate C# differences are documented separately.

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

Run on each affected target OS/display backend and summarize the tested scope,
results and skips in the PR. Tests against an earlier package or native library
do not validate a later build. Use repository sources and reproducible commands;
do not retain per-run reports or environment inventories in the repository.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
