# Wayland desktop interaction probe

This standalone functional probe needs a person in a logged-in GNOME Wayland
desktop. It uses unchanged `@tauri-apps/api@2.11.1` from the integrity-checked
conformance build and requires `DesktopRuntime.Backend == "wayland"`.
An X11/XWayland runtime cannot pass. No solution registration is needed.

Build the native adapter as described in [development](../../docs/development.md).
Use a session with WebKitGTK 4.1, a configured Korean IME and GNOME tray/AppIndicator
support. A missing tray or working IME is an incomplete check, not a pass.
Run these commands from the repository root:

```sh
node tests/conformance/scripts/build-frontend.mjs
node --test tests/NetWiv.Wayland.Smoke/checks.test.mjs
# Managed consumer: build, then execute in the Wayland desktop.
dotnet build tests/NetWiv.Wayland.Smoke -c Release -p:PublishAot=false
GDK_BACKEND=wayland dotnet tests/NetWiv.Wayland.Smoke/bin/Release/net8.0/NetWiv.Wayland.Smoke.dll
# NativeAOT consumer: publish, then execute the published artifact there.
dotnet publish tests/NetWiv.Wayland.Smoke -c Release -r linux-x64
GDK_BACKEND=wayland tests/NetWiv.Wayland.Smoke/bin/Release/net8.0/linux-x64/publish/NetWiv.Wayland.Smoke
```

Complete the visible steps separately for managed and NativeAOT execution:

1. Select **Probe → Native menu action** in the window menu. Open the native
   **NW** tray menu and select **Native tray action**. Each counter must increase.
   Activate the probe window and press **Ctrl+Shift+K**; its counter must increase.
   This is a focused WebView keyboard shortcut, with no global shortcut plugin.
2. Use the Korean IME to type exactly **한글 입력 확인 123** in the text field.
   Confirm composition, caret and committed text visually. The automatic check
   requires trusted composition start/end and input events plus the exact NFC
   sample. Paste and drop are blocked; no clipboard API is called. Clear/retry
   resets the composition assertions. Use only this synthetic text.
3. Drag a window edge to resize. Maximize using the titlebar or system menu,
   click the maximum-state check, restore, and click the restored-state check.
   Switch away and back to exercise native focus loss/return. Move the window
   by its native titlebar and by the dashed guest drag area.
4. Inspect the text, three color tiles and controls before and after those
   operations. Check each visual confirmation only after observing its result.
   Click Finish to remove the tray and close the probe-owned resources. Confirm
   the tray icon and window disappear after exit.

Automatic assertions cover backend identity, native menu/tray action callbacks,
trusted focused key/composition/input events, changed-size native resize events,
native focus transitions, sampled maximize/restore state and tray deregistration
after resource cleanup. The drag API return only records a completed call:
actual movement, titlebar behavior, rendering, IME presentation and menu/tray
visibility remain explicit manual confirmations. A resize event does not by
itself establish that an edge drag worked.

`READY` means guest/native setup and backend verification; it is not a visual
pass. Finish is enabled only after all automatic conditions and manual
confirmations. Abort, closing before completion and runtime/cleanup failures
return nonzero. An in-page deadline requests cleanup after 590 seconds; a
ten-minute process watchdog exits 124 if the guest or native loop is blocked.
Normal cleanup restores any previous window menu, releases listeners and closes
owned resources; process termination releases remaining native resources.

This does not establish global shortcuts, absolute window positioning, global
input injection, every IME, tray mouse event semantics, multi-monitor placement,
fractional scaling, every compositor, X11 behavior or complete Tauri parity.
GNOME shell tray support and IME configuration are prerequisites; this probe
does not install extensions or change desktop settings. Record only the checks
actually executed and their limits in the PR. It saves no reports, logs,
screenshots, environment inventory or user input, and uploads nothing.
