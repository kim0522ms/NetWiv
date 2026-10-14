# Pinned upstream behavior differences

These observations concern `@tauri-apps/api 2.11.1` with `tauri 2.11.5`.
The unchanged guest API and the native backend API must be tested separately
when the pinned pair disagrees with itself. A fulfilled guest promise alone
does not prove that a native property changed.

## Website data-store creation and removal

The pinned `tauri-runtime 2.11.3` conversion from `WindowConfig` to
`WebviewAttributes` does not copy `data_store_identifier`; the attributes default
it to `None`. Both guest `Webview` and `WebviewWindow` creation commands use this
conversion. On physical macOS arm64, the unchanged guest `dataStoreIdentifier`
option therefore used the default store: a fresh identifier did not enumerate,
and its synthetic storage marker persisted into a second random-identifier run.
The Rust SDK's explicit `.data_store_identifier(...)` builder forwards it to Wry.

NetWiv deliberately forwards the public option on macOS 14+ rather than inheriting
that omission. Before macOS 14 it follows Wry's default-store fallback; incognito
still takes precedence and selects a nonpersistent store. The impact is storage
isolation where pinned guest-created Tauri windows shared the default store.
Consumers wanting default storage can omit the identifier; consumers requiring
an identified store on the pinned reference can use its Rust builder. No NetWiv
API migration is required. The independent NetWiv public-option regression is
separate from Tauri compatibility coverage.

`app.website-data-store-sdk-lifecycle` creates its stores through test-only SDK
commands on both hosts, then uses unchanged public app/window guest APIs to verify
enumeration bytes, active deletion rejection, isolated persistence after close,
incognito precedence, deletion and empty recreation. Managed and NativeAOT
comparisons passed on physical macOS arm64. This is app API and lifecycle evidence,
not a public creation-option parity pass. Intel and minimum-OS execution remain
unverified.

The pinned runtime also collapses Wry removal errors into
`runtime error: failed to remove data store`. NetWiv maps only the exact native
in-use error to this string for guest callers. Its C# SDK retains
`data store is currently opened` for existing retry callers. Validation,
availability, cancellation, structured and unrelated failures are preserved.

Sources: pinned `crates/tauri-runtime/src/webview.rs`, `WebviewAttributes::from`
(lines 447-505) and `WebviewAttributes::new` (lines 509-528);
`crates/tauri/src/webview/plugin.rs`, both creation commands;
`crates/tauri-runtime-wry/src/lib.rs`, `RemoveDataStore` and the identifier builder
(lines 3361-3365 and 5112-5114); `crates/tauri-runtime/src/lib.rs`,
`FailedToRemoveDataStore`; `crates/tauri/src/error.rs`, `Error::Runtime`.

## Window effects and frame bridge availability

The `window.effects-validation` fixture compares both public `setEffects`
wrappers and invalid constructor effects with tauri 2.11.5. The pinned
`WindowEffectsConfig` validates effects, state, radius, color and unknown fields
even on Linux, where applying effects is a no-op. Windows selects the first
supported effect in the supplied list. An accepted request does not guarantee
that the current Windows version/theme can display that visual effect.

On macOS, NetWiv replaces its previous vibrancy view before applying a supported
material. Repeated `setEffects` calls therefore keep one effect view, and
`clearEffects` removes it. Empty effect lists and lists containing only
non-macOS materials leave the current effect unchanged. This deliberately
differs from the pinned Tauri 2.11.5 [macOS effect adapter](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri/src/vibrancy/macos.rs),
which adds a view on each supported application, and its
[null-effect dispatch](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri/src/vibrancy/mod.rs),
which only clears Windows effects. Replacement avoids accumulating native views
and retaining an old material after a clear. Keeping the accumulated views was
rejected because it makes subsequent effect changes and removal unreliable.
Callers can keep using the same guest and C# APIs; code depending on stacked
vibrancy views must use its own native composition instead.
The [focused AppKit probe](../tests/NetWiv.Desktop.Smoke/MacOsEffectsProbe.cs)
checks view count, material/state and close/recreate behavior. This difference
is not a Tauri compatibility pass or a resolution of the documented AppKit
SIGTRAP; see [macOS validation limits](macos.md#window-effects).

The `security.frame-origins` fixture uses unchanged public guest functions in
local, trusted remote, untrusted remote and opaque sandbox documents. WebView2
injects the bridge into child frames; each request is authorized using its
browser-provided Origin. WebKitGTK exposes the pinned TAURI® bridge only in the
main frame, on both X11 and Wayland. NetWiv follows this platform distinction.
Tauri's manager marks its initialization scripts main-frame-only; wry 0.55.1
documents that WebView2 ignores that flag and always injects into subframes.

The comparison includes successful local/trusted-window controls, denial of a
separate restricted command, and spoofed origin fields in guest JSON. It is a
standard protocol IPC fixture. Isolation retains NetWiv's existing main-frame
guard; isolation child-frame and forced fallback-transport equivalence remain
outside this claim. URLPattern separately matches the pinned urlpattern 0.3.0
oracle across 363 patterns and 3364 URL decisions.

## File asset protocol

The profile explicitly enables Tauri's `protocol-asset` feature and configures
`app.security.assetProtocol`. This is separate from bundled `tauri://` assets.
The pinned HTTP handler accepts all methods, handles Range before HEAD, caps each
range at 1,024,000 bytes, and returns empty 403/404 responses without Content-Type.
Its 416 response omits CORS headers. Multipart ranges return status 200, append a
second Content-Type header, and repeat a part separator as the final boundary
without the closing `--`. The Core regression preserves those wire behaviors.

Browser access is a separate boundary: WebView2 rejects suffix and multipart
Range fetches at CORS preflight, while WebKitGTK permits them for the custom
scheme. The frontend fixture first requires successful GET, HEAD and single
Range responses, then records the platform result. Only a fetch TypeError is an
accepted boundary rejection; timeouts and application errors fail the test.
Reference and candidate results are compared on the same OS/display backend.

## Detached checked menu items on Windows

Pinned muda 0.19.3 reads the Win32 checked state while a check item has a parent.
After removing its last parent, `is_checked` falls back to `enabled` instead of
the saved `checked` field. The menu lifecycle fixture verifies a successful
checked-state change while attached, then separately records the detached
result. The Windows fallback must not be counted as proof of a correct detached
checked-state implementation. Linux is compared independently.

Source: `muda-0.19.3/src/platform_impl/windows/mod.rs`, `MenuChild::is_checked`.

NetWiv's native adapter now uses muda 0.20.0 with tray-icon 0.25.1 to adopt
the upstream macOS 27 tray left-click fix. This version preserves the saved
checked state after detachment on Windows. The raw `checkedDetached` result
therefore deliberately differs from the pinned reference; matching the
reference's enabled-state fallback is not a compatibility goal. Applications
may continue to query `isChecked()` after removal without a migration.
Unsupported predefined items also render as disabled entries on Windows/GTK3;
the [native dependency notes](../native/netwiv-native/README.md#menu-and-tray-dependency-update)
list affected kinds and the omission alternative. These deliberate differences
must be reported separately from passing differential comparisons.

## Background colors

The guest `Window.setBackgroundColor` and `Webview.setBackgroundColor` send
`{ color }`. The runtime setter macros deserialize `value: Option<Color>`.
Consequently the guest calls resolve even for malformed colors and pass `None`
to the platform instead of the requested color. `WebviewWindow` calls both
setters in sequence and inherits this discrepancy.

The `window.color-union` differential fixture exercises that guest behavior,
then separately invokes the registered setters with `value` to verify valid
and invalid native color decoding. Constructor `backgroundColor` also uses
the native color deserializer. Visual color changes remain a separate GUI gate.

The C# backend `SetBackgroundColorAsync` methods use `value` and an explicit
target label, corresponding to the functional Rust backend setter. They do not
inherit the JavaScript argument-name defect. `WebviewWindow` applies the window
setter and then the webview setter, stopping if the first operation fails.

Sources: pinned `packages/api/src/window.ts`, `packages/api/src/webview.ts`,
and `crates/tauri/src/{window,webview}/plugin.rs`.

## WebView parent labels

The runtime `get_all_webviews` serializes `WebviewRef.window_label` without
a camel-case rename. The guest `getAllWebviews` reads `windowLabel` and creates
a parent Window wrapper whose label is therefore undefined. A missing
reparent destination still succeeds without changing the actual parent.

The `webview.child-lifecycle` fixture checks both the raw public invoke response
and the unchanged guest wrapper. The C# live lookup reads the actual native
`window_label` field so backend code can access the parent; this is an explicit
C# counterpart difference from the pinned JavaScript wrapper defect.

Sources: pinned `crates/tauri/src/webview/plugin.rs` and
`packages/api/src/webview.ts`. This must be rechecked before expanding the
supported upstream versions.

## Linux minimize/maximize button flags

The reference runtime's `tao 0.35.3` and NetWiv's `tao 0.36.0` GTK
implementations leave `set_minimizable` and `set_maximizable` empty and return
`true` from both corresponding getters.
NetWiv preserves those Linux results. This is separate from the `maximize`,
`unmaximize` and fullscreen operations, which do request actual window states.
Windows button setters and getters do change their native window flags.

The `window.native-flags` fixture checks the unsupported Linux setters' actual
unchanged values, alongside closable/decorated/enabled/topmost and visibility
roundtrips. It does not count the Linux behavior as working button suppression.
Sources: `tao-0.35.3` and `tao-0.36.0`'s `src/platform_impl/{linux,windows}/window.rs` and
Tauri's `packages/api/src/window.ts` platform-specific API notes.

## GTK topmost hint and cached getter

On the Ubuntu 24.04 X11 test desktop, both pinned Tauri and NetWiv return false
from `isAlwaysOnTop()` after setting it true on the test window. A separate
X11 property probe shows `_NET_WM_STATE_ABOVE` on that same native window.
Thus a false getter here does not demonstrate that the setter did nothing.
The pinned tao getter reads an atomic cache populated by GTK window-state
events; its setter sends `gtk_window_set_keep_above` to the UI loop.

The fixture records and compares the actual getter values on Linux, while
retaining strict flag roundtrips on Windows. Getter invocation errors still
fail the fixture. It does not claim reliable Linux topmost readback or use a
matching pair of timeouts as successful proof.
GTK documents that
[keep-above is a window-manager request](https://docs.gtk.org/gtk3/method.Window.set_keep_above.html).
This behavior must be rechecked when changing tao, GTK or the test compositor.

## Wayland client-side decorations and resize geometry

Pinned tao uses a GTK header bar on Wayland, sends `GtkWindow.resize` for a
size request and caches GDK configure-event dimensions for its getter.
Client-side decorations and shadows can affect the reported geometry. Exact
decorated-window size equivalence is a separate acceptance gap.

The strict `window.resize-and-dpi` and WebView resize fixtures use undecorated
windows to compare native pixel dimensions without that ambiguity. Successive
maximize toggles require the requested state and geometry to remain stable for
150 ms before issuing the inverse operation, within the same five-second
deadline. A timeout is a failure, not equivalent behavior.

## WebView auto-resize

The pinned runtime makes an implicit root WebView fill its parent. On Windows,
an explicitly positioned child retains its x, y, width and height ratios when
auto-resize is enabled. Re-enabling captures current native bounds; changing position or size
updates only the corresponding ratios. The calculation uses logical `f32`
coordinates before converting back to native pixels.

The pinned public creation command deserializes `WindowConfig` and rejects an
`autoResize` constructor option. Guest code must call `setAutoResize(true)`.
The two resize fixtures use this public path.

On Linux, pinned Tauri builds both roots and children inside tao's default
`GtkBox`, using `pack_start(webview, true, true, 0)`. Wry's GTK `set_bounds`
only changes allocation for a `GtkFixed` or an X11 child-window path, so it
does not override this Box layout. Its GTK position getter also reports zero.
A sole child therefore fills the Box even with auto-resize disabled. This is
checked explicitly on Linux, rather than claiming position/size control.
NetWiv uses the same default Box for creation and reparenting, preserving
menu-bar allocation as well.

Sources: the pinned runtime's `create_webview`, and wry 0.55.1
`src/webkitgtk/mod.rs` (`add_to_container`, `bounds`, `set_bounds`, `reparent`).

## Creation options and initially hidden parents

Pinned `WindowConfig` accepts both `userAgent` and its `user-agent` alias for
root and child creation. NetWiv passes the validated, canonicalized config
to native construction so aliases and defaults cannot disappear between parsing
and application. Optional null settings are omitted before calling setters.
Root WebView controllers retain wry's visible default even when their parent is
initially hidden, so showing the parent activates its document correctly.
The hidden-parent fixture checks document visibility and requestAnimationFrame
delivery after show; it does not establish manual pixel or input acceptance.

The trusted C# `WebviewBuilder.AutoResize()` option is handled separately from
the pinned guest config contract. Guest `autoResize` still rejects as unknown.
Guest child creation does not use WindowConfig's `visible` field, matching
Tauri's `WebviewAttributes::from`; the C# child builder retains its explicit
visibility extension. NativeAOT smoke exercises repeated C# AutoResize creation.

## Image decoding and menu ownership

The native adapter uses pinned Tauri's `image 0.25.10` with only PNG/ICO codec
features. Four deterministic image fixtures compare exact dimensions/pixels
through arrays, Uint8Array, ArrayBuffer and paths. Decoder and missing-file
errors retain Tauri's formatting, including the platform's own I/O message.

Replacing an app or window menu detaches the previous native menu first,
including reattaching the same menu. A window's inherited app menu is returned
when replaced explicitly. Later app changes preserve an explicit window menu.
Windows installs tao's message hook and translates against the current active
menus' accelerator tables, following pinned Tauri `app.rs` and `manager/menu.rs`.
Changing an item's accelerator is observed without reinstalling its root menu.

The explicit native test `runtime::tests::native_menu_replacement_and_accelerator_dispatch`
checks menu replacement and removal inside a test-owned window. On Linux it
checks GTK menu-bar counts of one then zero. On Windows it posts key messages
only to the hidden test window, checks actual menu events, changes the binding
and verifies clearing the active stash prevents further dispatch. This is an
automated native-message test, not physical keyboard or manual GUI acceptance.
Run it explicitly with `cargo test native_menu_replacement_and_accelerator_dispatch
-- --ignored --nocapture` in a prepared native desktop environment. Ordinary
Rust tests leave this environment-dependent case ignored.

## C# DPI and error strings

C# physical coordinates use the Rust `dpi 0.1.2` integer conversion contract.
Negative midpoint rounding is away from zero, range overflow saturates, NaN
converts to zero, and the scale must be positive and normal (subnormal values
are rejected). Guest
`@tauri-apps/api/dpi` objects preserve fractions; the 55-fixture suite compares
their constructors, fields, wrappers, conversions and JSON/IPC hooks separately.
The maintainer oracle is `compatibility/oracles/dpi`, pinned by its Cargo.lock;
its generated 144 conversion and 24 scale vectors are embedded in Core tests.
Non-finite C# logical values can be converted to integer pixels, but sending
non-finite logical values directly through JSON still has serializer constraints.

Tauri command errors use Rust Debug string formatting, which differs from JSON
escaping for NUL, controls, combining characters, format characters and emoji.
`compatibility/scripts/generate-rust-debug-ranges.rs` pins the formatter to Rust
1.95.0/Unicode 17 and emits an exhaustive scalar-string oracle. Backend invalid-event-name
errors share this formatter; guest argument validation keeps its own upstream text.

## macOS evidence boundary

The Windows and GTK observations above remain specific to those engines and
desktops. Do not infer WKWebView frame injection, CORS/range behavior, geometry,
menu roles or error equivalence from Linux's WebKitGTK results. NetWiv now
implements WKWebView/AppKit through the shared Wry adapter; Apple Silicon has
NativeAOT integration and focused GUI evidence, not a completed macOS differential
conformance report.

macOS applies simple fullscreen, title-bar styles, shadows and vibrancy through
Tao/window-vibrancy. App visibility and Dock visibility are application-wide.
`skipTaskbar=true` for an individual window returns an explicit unsupported error.
Website data-store identifier enumeration/removal is implemented asynchronously
on macOS 14+; earlier macOS versions receive an availability error. The monitor
work area converts NSScreen's visible frame to top-left physical coordinates;
multi-monitor and mixed-DPI acceptance remain open. The AppKit menu tests run in
the real main-thread smoke host; the ignored Windows/Linux Rust desktop-menu
test is not a macOS verification command. See [macOS](macos.md) for the complete
implemented/unsupported/unverified split.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
