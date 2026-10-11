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
# On macOS, replace linux-x64 with osx-arm64 (Apple Silicon) or osx-x64 (Intel).
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

## Linux tray validation

The pinned `tray-icon` 0.24.2 GTK backend uses
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
