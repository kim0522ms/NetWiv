# Windows lifecycle investigation

Recorded 2026-10-12, based on `main` commit
`71d3e1d` (PR #15 merged). This report accompanies the source changes in
`fix/windows-webview-lifecycle`; artifact hashes identify the tested builds.

## Confirmed defects

### Initial focus incorrectly aborts WebView creation

Unmodified main failed creating the hidden `temporary` child with `0x80070057`.
An isolated program using the same WRY 0.55.1 and TAO 0.35.3 dependencies then
identified a deterministic case: a minimized parent with default initial focus
fails inside `ICoreWebView2Controller.MoveFocus`; disabling initial focus succeeds.
Visible and hidden, non-minimized isolated cases succeeded. Error-only temporary
WRY instrumentation identified the failing API; it was removed before regression
validation. The initial hidden-window failure has no API-level trace, so this
report does not equate that individual occurrence with the minimized case.

The NetWiv regression also failed before the fix when creating `minimized-child`.
The adapter now creates the controller without initial focus, then attempts focus
unless explicitly disabled. Explicit focus commands still propagate errors.
This follows [WRY #1799](https://github.com/tauri-apps/wry/pull/1799) without
changing the WRY pin. The test checks explicit false/true and default focus,
minimized-state preservation, restore/focus and native registry cleanup.

### Taskbar recreation permanently deadlocks the UI thread

Two workers delivered `TaskbarCreated` to a window belonging only to the test
process. Main stopped responding; a full dump captured the UI thread waiting
inside `parking_lot::RawMutex::lock_slow`. The same stack contains both the
outer taskbar handler and its re-entry through `TaskbarList::AddTab` and
`SendMessageW`. [The stack excerpt](taskbar-stack.txt) proves the native lock
cycle, rather than inferring it from a managed thread waiting in `NativeRun`.

TAO 0.35.3 holds its window-state mutex across the taskbar COM call. TAO 0.36.0
includes the [upstream lock-release fix](https://github.com/tauri-apps/tao/pull/1264).
The dependency and source-license locks were updated together. Other desktop
changes in 0.36.0 include keyboard/IME internals and Linux window requests; the
cross-platform checks below cover the available environments. The Tauri guest
API/reference version stays unchanged.

The committed `WindowsLifecycleProbe` sends 400 targeted messages, checks each
send and verifies responsiveness afterward. With the old DLL, this exact C#
test reported a timeout and the diagnostic runner captured a dump before killing
the test process. With the fixed DLL, it passed under managed and NativeAOT.
Explorer was not restarted and messages were not broadcast to other applications.
The smoke watchdogs now terminate the test process on their existing deadlines;
posting an exit request alone cannot terminate a deadlocked UI thread.

## Device and results

- Physical Lenovo 20S60006KR, Intel i7-10510U, Windows 10 Pro x64 build 19045;
  WebView2 154.0.4258.62; .NET SDK 8.0.415; Rust 1.95.0.
- Windows GUI runs used the logged-in console session through interactive
  scheduled tasks. SSH was used for builds and log transfer.
- Physical Apple Silicon macOS 15.4 host; .NET SDK 8.0.406; Rust 1.95.0.
- Linux checks used Ubuntu 22.04 x64 in WSL2, Xvfb and D-Bus: virtual/headless
  native evidence, not physical Linux GUI or NativeAOT acceptance.

| Final check | Result |
| --- | --- |
| Windows full desktop, managed | 6/6 passed |
| Windows full desktop, NativeAOT | 6/6 passed |
| Windows managed coverage run | Passed; [helper coverage](coverage.txt) |
| Windows windowless, managed / NativeAOT | Both passed |
| Windows native unit tests | 50 passed; GUI-only test initially ignored |
| Windows native menu/accelerator test, interactive | 1 passed |
| macOS native unit tests | 49 passed |
| macOS full desktop, managed / NativeAOT | Both passed |
| Linux native unit tests, WSL2 | 48 passed; GUI-only test initially ignored |
| Linux menu test, Xvfb/X11/D-Bus | 1 passed |
| Native license provenance and dependency preparation tests | 16 passed |

The Xvfb menu run explicitly used `GDK_BACKEND=x11`; leaving WSL's Wayland
backend selected stalled during GTK initialization and that test process was
terminated. This was a headless test setup correction, not a source change.

[Windows before/after logs](windows-results.txt) and
[other platform logs](other-platforms.txt) record the actual executions.
Earlier focus-only builds passed 30 desktop runs; those are separate from the
12 final runs with the TAO update. Unmodified main also passed 43 ordinary
repetitions before the targeted taskbar reproduction. Repetition alone was not
used to establish the cause or claim a fix.

Native library SHA-256:

- Initial main Windows DLL: `b5f00e658d677391a0e3090f3611b639a5a6b1e0f3ac51f63db37611f4f72f5f`
- Final Windows DLL: `b323d8b5b31f036d0d5d9c1c2462239a770e7fb2a7eac581ebd8e70ec4d008fe`
- Final macOS arm64 library: `73620e0a7a62040248aecaa59582b75bc59fc4ba538d0b62bf34d82ee20d38d2`

## Reproduction

Build native assets with `scripts/build-native.ps1`, then publish and run
`tests/NetWiv.Desktop.Smoke` in a real Windows desktop session:

```powershell
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r win-x64 -p:PublishAot=false -o artifacts/check/managed
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r win-x64 -p:PublishAot=true -o artifacts/check/aot
$env:NETWIV_SMOKE_HOLD_MS = '0'
& artifacts/check/managed/NetWiv.Desktop.Smoke.exe
& artifacts/check/aot/NetWiv.Desktop.Smoke.exe
```

For the failure-before comparison, use the new smoke executable with a matching
ABI-2 native DLL built from the baseline commit. The taskbar regression runs
before the minimized-parent regression and can deadlock the old library. Capture
a dump and terminate that test-owned process on a stall; do not wait for CI to
expire. To isolate the focus regression, run it separately or omit the taskbar
probe in a temporary diagnostic copy.

## Evidence boundaries

Two defects now have controlled failure-before/pass-after evidence. Earlier
intermittent stalls were not captured with native stacks, so they cannot all be
attributed retrospectively to the taskbar defect. Sleep/resume and real Explorer
restart were not exercised; the test reproduces their taskbar-message path.
This is not a claim that all possible WebView2 freezes are eliminated. Minimum
Windows versions, macOS Intel and Linux physical-device/NativeAOT runs were not
part of this investigation.
