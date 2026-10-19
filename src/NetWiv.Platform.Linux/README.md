# Legacy direct Linux bindings

This directory is archival source, not the production Linux backend. Current
applications use `NetWiv.Platform.Desktop` and the Wry/Tao native adapter on X11
or Wayland. Start with the [Linux guide](../../docs/linux.md) and
[native architecture](../../docs/native-architecture.md). The implementation
notes and limitations below refer only to these legacy direct bindings.

This backend uses GTK 3 and WebKitGTK 4.1 directly through source-generated
`LibraryImport` bindings. It has no Rust dependency and does not use GTK 4.

Ubuntu 24.04 build dependencies:

```sh
sudo apt install clang zlib1g-dev libgtk-3-dev libwebkit2gtk-4.1-dev libayatana-appindicator3-dev
```

Build artifacts must be kept outside a Windows-owned `obj` or `artifacts`
directory when building from WSL:

```sh
dotnet publish tests/NetWiv.Linux.Smoke/NetWiv.Linux.Smoke.csproj \
  -c Release --artifacts-path "$HOME/netwiv-artifacts"
"$HOME/netwiv-artifacts/publish/NetWiv.Linux.Smoke/release_linux-x64/NetWiv.Linux.Smoke"
```

Set `NETWIV_SMOKE_HOLD_MS` to keep the verified windows open for visual
inspection. Values are clamped to 120 seconds.

The smoke executable verifies an actual `linux-x64` NativeAOT process with two
GTK windows, two child webviews in one window, a third webview in the other,
document-start injection, authenticated IPC, `tauri://localhost` resources,
request and response plumbing, window and webview sizing, script evaluation,
GTK menu attachment, Ayatana AppIndicator lifetime, cleanup, and a second run
on the same runtime instance.

Implemented TAURI® command strings cover the central window lifecycle, title,
visibility, focus, maximize/minimize/fullscreen state, size, position,
decorations, resizability and always-on-top operations; webview focus,
visibility, zoom, size, position and close operations; explicit GTK menu
resource creation/append/window attachment; and Ayatana tray creation, menu,
title, visibility, lookup and removal. Native resource close is also supported.

Unsupported commands throw `NotSupportedException`; they never report a fake
success. Current gaps are dynamic window and webview creation, close-request
interception, webview reparenting and auto-resize, inline or mutable menu trees,
menu activation channels, check/icon menu item behavior, tray image conversion
from `ImageData`, and tray activation channels. AppIndicator cannot remove an
already assigned menu. GPU acceleration is not part of the smoke gate.

The bindings and command names follow these upstream interfaces:

- [WebKitGTK 4.1 user message handlers](https://webkitgtk.org/reference/webkit2gtk/stable/method.UserContentManager.register_script_message_handler.html)
- [WebKitGTK custom URI schemes](https://webkitgtk.org/reference/webkit2gtk/stable/method.WebContext.register_uri_scheme.html)
- [WebKitGTK document-start user scripts](https://webkitgtk.org/reference/webkit2gtk/stable/ctor.UserScript.new.html)
- [GTK 3 API](https://docs.gtk.org/gtk3/)
- [Tauri 2.11.1 JavaScript window API](https://github.com/tauri-apps/tauri/blob/tauri-v2.11.1/packages/api/src/window.ts)
- [Tauri 2.11.1 JavaScript webview API](https://github.com/tauri-apps/tauri/blob/tauri-v2.11.1/packages/api/src/webview.ts)
- [Tauri 2.11.1 JavaScript menu API](https://github.com/tauri-apps/tauri/tree/tauri-v2.11.1/packages/api/src/menu)
- [Tauri 2.11.1 JavaScript tray API](https://github.com/tauri-apps/tauri/blob/tauri-v2.11.1/packages/api/src/tray.ts)

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
