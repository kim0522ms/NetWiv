# Linux: X11 and Wayland

NetWiv's Linux x64 desktop runtime uses Wry/Tao with GTK3 and WebKitGTK 4.1.
It supports X11 and native Wayland sessions. Ubuntu 24.04 is the target baseline;
WSLg provides another execution environment, not a replacement for physical
desktop acceptance. Use the [package quickstart](integration-quickstart.md) to
create an app with the `@netwiv/api` frontend SDK.

## Prerequisites and execution

Application development needs .NET SDK 8 or later, the selected frontend's
toolchain, GTK3, WebKitGTK 4.1 and a reachable desktop session. NativeAOT also
needs the platform compiler/linker dependencies. Ordinary consumers use the
native library from `NetWiv.Platform.Desktop` and do not need Cargo. Framework
maintainers should follow the [native build guide](../native/netwiv-native/README.md)
for source-build dependencies.

From a generated JS/TS application directory:

```sh
npm ci
# Run in a logged-in Wayland session:
GDK_BACKEND=wayland npm run desktop:dev
# Or in an available X11 session:
GDK_BACKEND=x11 npm run desktop:dev
```

These variables select an existing display backend; they do not start a display
server. Wayland needs a reachable `WAYLAND_DISPLAY` socket and, for relative
socket names, `XDG_RUNTIME_DIR`. Desktop services also need the session D-Bus
connection. Tray interaction requires a compatible tray host. A Wayland desktop
may also provide XWayland; having `DISPLAY` set does not by itself identify the
backend used by the app. Maintainer smoke tests assert the actual backend through
`DesktopRuntime.Backend`; see [reproduction commands](development.md#build-and-test).

## Verified scope and limitations

Managed and NativeAOT source-built checks passed on a physical Ubuntu 22.04
GNOME Wayland desktop for window/WebView lifecycle, IPC, assets, menu/tray
interaction, focused keyboard input, Korean IME, resize/maximize/restore,
focus, dragging and cleanup. Linux dialog checks also cover file/folder/save
selection, message buttons, cancellation and lifecycle cleanup. The
[support guide](implementation-status.md#wayland-validation) distinguishes
automated checks, selected upstream comparisons and interactive checks.

This does not establish physical Ubuntu 24.04 Wayland acceptance, coverage of
other compositors/portals, fractional scaling, multi-monitor placement or every
optional plugin. Hosted Linux CI currently uses X11; its result is separate
from native Wayland verification.

- Pinned Tao does not support always-on-top/bottom or absolute window positioning
  on Wayland. Global cursor coordinates return `(0, 0)`; a successful call or
  matching getter is not proof that the compositor applied a requested effect.
- Window-state position restoration and accurate global coordinates are outside
  the verified Wayland baseline. Client-side decorations affect resize geometry;
  see [platform behavior](pinned-upstream-behavior.md#wayland-client-side-decorations-and-resize-geometry).
- The current global-shortcut dependency uses X11 and may connect through
  XWayland. This does not implement native Wayland global shortcuts or establish
  inactive-app input delivery. Focused keyboard input is a separate feature.
- Linux dialog handling is asynchronous and cancels pending requests when their
  caller, window, WebView, document or app ends. Other portal/compositor paths
  and full upstream dialog equivalence remain unverified; see the
  [dialog probe](../tests/NetWiv.Dialog.Smoke/README.md).

Build Linux packages on Linux. The CLI offers deb, rpm and AppImage paths;
consult [deployment](deployment.md) and [release limits](implementation-status.md#packaging-and-distribution)
before treating a successful package build as installed-app acceptance.
