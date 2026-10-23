# Desktop plugin smoke

This interactive consumer exercises native clipboard text, file dialog and
notification command paths with generated synthetic content. Build the native
adapter and the pinned frontend overlay first, as described in
[development](../../docs/development.md). This probe writes no reports and
prints no clipboard contents, fixture paths or native error details.

With no arguments, the probe runs all three manual checks. Pass
`--dialogs-notifications` to run only the native picker and notification checks;
the page explicitly labels clipboard as **NOT REQUESTED** and disables its
controls. Pass `--isolated-clipboard` only for the guarded virtual-X11 test
described below.

Build the managed host:

```sh
dotnet build tests/NetWiv.PluginDesktop.Smoke -c Release -p:PublishAot=false
```

Run the produced host in a logged-in desktop session. The Linux wrapper asserts
the requested backend; use both backends when they are available:

```sh
# Windows: run the produced .exe directly.
# macOS arm64: run the produced executable directly.
# Linux X11:
bash scripts/run-linux-smoke-desktop.sh "$PWD/tests/NetWiv.PluginDesktop.Smoke/bin/Release/net8.0/NetWiv.PluginDesktop.Smoke" x11
# Linux Wayland:
bash scripts/run-linux-smoke-desktop.sh "$PWD/tests/NetWiv.PluginDesktop.Smoke/bin/Release/net8.0/NetWiv.PluginDesktop.Smoke" wayland
```

For NativeAOT, publish for the host RID, then run the published executable by
the same platform-specific method:

```sh
# Windows x64
dotnet publish tests/NetWiv.PluginDesktop.Smoke -c Release -r win-x64
# Linux x64
dotnet publish tests/NetWiv.PluginDesktop.Smoke -c Release -r linux-x64
# macOS arm64
dotnet publish tests/NetWiv.PluginDesktop.Smoke -c Release -r osx-arm64
```

On Windows and macOS arm64, run the resulting executable from
`bin/Release/net8.0/<rid>/publish/`. On Linux, pass that executable path to
`scripts/run-linux-smoke-desktop.sh` with `x11` or `wayland`.

Before changing the clipboard, place disposable text there and check the
confirmation box. The probe reads the original text, writes and verifies a
synthetic marker, restores the original text, and verifies restoration. Other
clipboard formats are not preserved; do not use this check when the clipboard
contains data that must be kept in another format. A failed initial text read
skips mutation but does not count as a completed clipboard check.

Select the generated `selection.txt` in the first native picker, then cancel the
second. The probe checks the selection internally without displaying or logging
its path. The notification action waits for the desktop plugin command response;
it does not establish that the OS displayed a notification. Confirm visibility
in the separate checkbox only after observing it. Click **Finish checks**, then
close the app window; its temporary synthetic dialog fixture is removed on
normal shutdown. Closing early or failing restoration exits with a failure
status.

These are manual consumer checks, not automated OS acceptance. Record results
only through the project's approved review process; this probe does not retain
per-run evidence. Repeat on each target OS/runtime being evaluated. Wayland
execution also requires the existing Linux wrapper prerequisites documented in
[development](../../docs/development.md).

## Isolated virtual-X11 clipboard check

This Linux-only mode writes a fixed synthetic text value, reads it back, then
clears it. It requires both `NETWIV_TEST_ISOLATED_CLIPBOARD=1` and the explicit
X11 backend settings; it refuses to run otherwise. Run it only on a newly
created, dedicated Xvfb display so it cannot touch a user's desktop clipboard.
Do not run this command from a real desktop session or reuse a display that
belongs to another process. This verifies the native X11 clipboard path in a
headless virtual display; it is not physical Linux clipboard acceptance.

```sh
xvfb-run -a dbus-run-session -- env --unset WAYLAND_DISPLAY \
  GDK_BACKEND=x11 NETWIV_EXPECT_BACKEND=x11 NETWIV_TEST_ISOLATED_CLIPBOARD=1 \
  tests/NetWiv.PluginDesktop.Smoke/bin/Release/net8.0/NetWiv.PluginDesktop.Smoke \
  --isolated-clipboard
```

Use the published executable in place of the managed host when checking a
Linux NativeAOT publish. The mode prints one fixed PASS or FAIL line and exits
with the corresponding status; it does not retain clipboard data or reports.
