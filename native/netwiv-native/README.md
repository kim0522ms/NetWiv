# netwiv-native

`netwiv-native` owns the desktop event loop and the wry, tao, muda, and
tray-icon objects used by the .NET runtime. Its public boundary is the
versioned C ABI in `include/netwiv.h`.

The complete resolved Cargo license inventory is generated at
`../THIRD-PARTY-NOTICES.md`; rerun `node native/generate-third-party-notices.mjs`
after changing `Cargo.lock`.

Stronghold's `libsodium-sys-stable` builds a pinned native libsodium source.
For framework source builds, prepare the inputs from the repository root:

```powershell
node scripts/prepare-native-dependencies.mjs win-x64 artifacts/native-source/ci
$env:SODIUM_DIST_DIR = (Resolve-Path artifacts/native-source/ci).Path
./scripts/build-native.ps1
```

On Linux/macOS (both build from the same signed source archive):

```sh
# On macOS, replace linux-x64 with osx-arm64 (Apple Silicon).
node scripts/prepare-native-dependencies.mjs linux-x64 artifacts/native-source/ci
export SODIUM_DIST_DIR="$PWD/artifacts/native-source/ci"
bash scripts/build-native.sh
```

The helper checks SHA-256 values from `../dependency-lock.json`. It extracts
`LATEST.tar.gz` and its signature from the checksum-pinned
`libsodium-sys-stable` crate instead of downloading a changing `LATEST` archive.
Windows also uses the official 1.0.22 release ZIP and signature, saved under the
`libsodium-1.0.22-stable-msvc.zip` names expected by the crate without changing
their bytes. The crate verifies the upstream minisign signatures during build.
All desktop targets use libsodium 1.0.22; updating these inputs requires reviewing
their hashes and rerunning native and NativeAOT validation.
The archive's ISC license is retained at `../LIBSODIUM-LICENSE.txt` and included
in the Wry NuGet package.
macOS requires Xcode Command Line Tools and builds a signed dylib for the host
architecture with `scripts/build-native.sh`; see [macOS](../../docs/macos.md).
Linux additionally needs a C compiler and `make`. Using
`SODIUM_USE_PKG_CONFIG=1` instead links the system `libsodium.so.23`; that
binary then requires the system libsodium runtime on consumer machines.
Published NetWiv packages include the built library, so application builds
do not run Cargo or require the source archive.

The workspace release profile strips symbols (`strip = "symbols"`) and uses
one codegen unit, trading a slower native rebuild for a smaller adapter. Dynamic
exports stay: `scripts/build-native.sh` fails when the `netwiv_*` exports differ
from `include/netwiv.h`, and macOS signs the dylib after stripping. Caught
panics still return their message through `netwiv_last_error`, and the default
panic hook and test harness still print the source location, but release
backtraces, including `cargo test --release`, no longer name Rust frames. Set
`CARGO_PROFILE_RELEASE_STRIP=debuginfo` for a local build that needs symbolized
backtraces; do not ship that build.

## Linux tray validation

The pinned `tray-icon` 0.25.1 `libappindicator` backend uses
`libappindicator::AppIndicator`. A bare WSLg Wayland session does not provide a
StatusNotifierWatcher or another tray host. Ayatana AppIndicator then falls
back to `GtkStatusIcon`, which is not a usable tray implementation on pure
Wayland. With `G_DEBUG=fatal-criticals`, the resulting upstream stack is:

```text
gtk_widget_get_scale_factor
gtk_status_icon_set_from_file
libayatana-appindicator3
glib main-context iteration
tao EventLoop::run_return
```

This is separate from WebView and window teardown. Wayland core validation may
mark tray interaction as skipped when no tray host exists. Tray creation,
interaction, visibility changes, and removal must be tested in a prepared
desktop with a StatusNotifier host; the first milestone also records the X11
result separately.

## Menu and tray dependency update

NetWiv pins `tray-icon` 0.25.1 and its shared menu dependency `muda` 0.20.0.
The tray update includes the [macOS 27 left-click fix](https://github.com/tauri-apps/tray-icon/pull/365):
the status item's menu is attached only while it is presented, so AppKit does
not swallow left-click events when `showMenuOnLeftClick` is false. Linux keeps
GTK3/AppIndicator (`libappindicator`), and Windows keeps common controls v6
(`muda-common-controls-v6`); the optional `ksni` and GTK4 backends are not enabled.
The C ABI and managed/guest APIs do not change. Framework maintainers must
rebuild every native RID; package consumers continue to use prebuilt assets.

The required muda update changes observable behavior relative to the pinned
Tauri reference. On Windows, detached check items retain their checked state.
Unsupported predefined items now remain visible as disabled entries instead
of being omitted: `Fullscreen`, `HideOthers`, `ShowAll`, `Services` and
`BringAllToFront` on Windows; `Undo`, `Redo`, `Minimize`, `Maximize`, `Fullscreen`,
`Hide`, `HideOthers`, `ShowAll`, `CloseWindow`, `Quit`, `Services` and
`BringAllToFront` on GTK3. Applications that want to omit these entries should
leave them out of the platform's menu definition. These differences are
deliberate consequences of adopting the maintained upstream tray fix rather
than maintaining a local fork; they are not Tauri compatibility passes.
macOS supports all predefined kinds exposed by NetWiv.

Validation must cover creation and `setMenu`, toggling `setShowMenuOnLeftClick`,
left-click events without a popup when false, left-click/right-click menu
presentation, and hide/show/remove lifetimes. Menu replacement and accelerator
dispatch, checked-state retention and unsupported-item rendering require
separate regressions. A compile or serialized event test does not prove AppKit
mouse routing; use the menu/tray desktop probe on the affected macOS version.

## IPC frame boundary

The Wry 0.55.1 Linux IPC handler constructs the request URI from the current
top-level `WebKitWebView` URI, so it cannot identify which iframe posted a
message ([upstream source](https://github.com/tauri-apps/wry/blob/v0.55.1/src/webkitgtk/mod.rs#L630-L650)).
WebView2 supplies the message source URI through `WebMessageReceivedEventArgs.Source`
([upstream source](https://github.com/tauri-apps/wry/blob/v0.55.1/src/webview2/mod.rs#L877-L915)).
The NetWiv initialization scripts therefore check that a child frame is
same-origin before capturing the Core invoke key. Isolation mode still uses a
strict top-frame guard. Remote iframe grants are not considered compatible
until an adversarial frame suite proves the boundary on each engine, including
WKWebView. NetWiv's macOS isolation/remote-origin smoke is separate from the
full pinned TAURI® differential acceptance.

## Pinned-source audit

The current command literals cover the Windows, Linux and macOS window, webview, menu,
tray, image, and resource plugin handlers. This is a source inventory and does
not mean every branch has been exercised in a GUI. The latest source-alignment
batch is:

| Area | Adapter behavior | Pinned upstream behavior |
| --- | --- | --- |
| `set_simple_fullscreen` | Uses Tao's macOS simple fullscreen; ordinary borderless fullscreen on Windows/Linux. | Tauri falls back to ordinary fullscreen outside macOS ([source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri/src/window/mod.rs#L2129-L2148)). |
| `set_title_bar_style` | Applies title-bar transparency/full-size content on macOS; no window mutation on Windows/Linux. | The desktop command is accepted, while `tauri-runtime-wry` mutates only on macOS ([source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri-runtime-wry/src/lib.rs#L3642-L3655)). |
| Window creation | Applies `parent`, creation-time `preventOverflow`, and Windows `windowClassname`. | These are target-applicable `WindowConfig` fields ([source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri-utils/src/config.rs#L1917-L2170)). |
| Webview creation | Applies `proxyUrl`; shares Wry contexts by resolved `dataDirectory` and keeps each context alive until its last webview closes. | Tauri resolves safe relative data directories under LocalData/label, preserves the original value when that replacement is not made, and forwards the result with the proxy ([source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri/src/webview/mod.rs#L389-L504), [runtime source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri-runtime-wry/src/lib.rs#L4787-L4817)). |
| Window icon validation | Rejects a missing or null `set_icon` image; nullable overlay icons remain supported on Windows. | The command requires a non-null `JsImage`; only the Windows overlay icon accepts `None` ([source](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri/src/window/plugin.rs#L169-L210)). |

Linux window effects and shadows intentionally complete without a native
mutation, matching the pinned Tauri dispatch paths. Windows window effects use
the same pinned `window-vibrancy` 0.6.0 implementation as Tauri. macOS uses its
vibrancy material/state/radius path and Tao shadow setter. AppKit requires the
process main thread; protocol responses are queued there to avoid a managed/native
lock deadlock. Build and smoke instructions are in the [macOS guide](../../docs/macos.md).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
