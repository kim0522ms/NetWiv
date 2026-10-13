# Desktop readiness validation — 2026-10-13

This record covers the coordinated package, IPC, referenced-binding, Hosting,
OS-information and website-data-store changes. It is not a full Tauri parity
claim or public release acceptance.

## Source and artifact identity

The common local feed is `0.1.0-readiness.1`, containing 30 packages built from
integration revision `92e1cb3362a1bccb9bf5bc6b4f1feae36c072300`, based on
`7183b53628c2d714c98a0671025bdbfca0667812`. Subsequent commits correct test helpers,
the Hosting sample and documentation; they do not change these SDK/native package
contents. Consumers use fresh caches and the same package hashes.
The real feed also passed `scripts/release-packages.py bundle` locally; the ZIP
SHA-256 is `7a5ea67c92db5bec7278bc0a2b64300b636da7503e5963e33df47770bcb1b47c`.
No release or version tag was published by this validation.

| Package | SHA-256 |
| --- | --- |
| Core | `4fcbcb980ebd54e8c362871725b3525f30d4139999d0c9e71db7e70d97ba7e58` |
| Hosting | `3c85d8d969ce6f402b7da381347b0659abe3e7bddf3bb14fe28c2032a336fd9a` |
| Generators | `4f83c3c503ab38fa98963cc9a3efcfa4724c9e82427f3a0fc7bcc796e5f186f1` |
| Build | `312e39150edd1812151af2989dbfd1662688b1cda31cd774b610e8b641d31b14` |
| Platform.Desktop | `14da55758969171ec40ee3d378162c3fdc26220f6f8550104d681b7ddf7996ca` |
| Cli | `5360634811d29df62c9622f1b74a6b668fd046ba6a382ebe831de1532b9c7ea2` |

All names above have the `NetWiv.` prefix and `.0.1.0-readiness.1.nupkg` suffix.
The native code is baseline plus `ab6a39b`; Windows/Linux were rebuilt in the
integration checkout. macOS x64 was cross-compiled, not executed on Intel.

## Hosts and results

| Host | Environment and observed result |
| --- | --- |
| Physical Windows x64 | Windows build 26200, WebView2 154.0.4258.62. Framework SDK 10.0.302; isolated consumer SDK 8.0.406/runtime 8.0.13. Core managed and NativeAOT each passed 221 checks. API 33, generator 176, CLI 107 and native Rust 50 tests passed. Full NativeAOT desktop smoke passed. |
| Physical macOS arm64 | macOS 27.0 beta, WebKit 22625.1.24.11.2, Xcode 27, SDK 8.0.406/runtime 8.0.13, Rust 1.95.0. OS-information managed/AOT checks passed. Native Rust 52 tests and website-data-store guest/SDK managed/AOT checks passed. Full managed/AOT desktop smoke passed with the display awake. |
| WSL2 Linux x64 | Ubuntu 24.04, GTK 3.24.41, WebKitGTK 2.52.6, SDK 10.0.302. Integrated Core NativeAOT and desktop NativeAOT smoke passed under Xvfb/X11 with an XFCE window manager. This is virtual/headless evidence, not physical Linux or Wayland acceptance. |

Windows IPC differential verification compared the unchanged official guest in
the Rust reference host and the integrated Wry NativeAOT host at `f3e4186`.
`channel.ordered`, `webview.reparent-capability` and
`webview.reparent-inflight-channel` matched: the admitted call and channel
survived reparenting/old-parent destruction, and new calls respected the new
parent's ACL. This is **3 focused cases out of 62**, not a full-suite pass.
Separate managed/AOT Core tests cover six cancellation/reload/close/transport
combinations, late resource rejection and awaited scoped disposal.

The Windows package-binding harness passed 15 checks with the common feed,
including strict TypeScript, real managed/AOT WebView IPC from a referenced
NuGet producer, producer JSON policies, denied/unregistered routes and export
invalidation after package removal. The consumer has no framework source
references and places a failing Cargo shim first on PATH.

The release package-consumer gate also passed on physical Windows and Apple
Silicon with isolated .NET 8, and under WSL2/Xvfb with isolated .NET 10. Windows
and Mac each executed framework-dependent and default self-contained React
GUI/IPC; Mac additionally executed both `.app` bundles. Linux executed the
framework-dependent .NET 10 React GUI/IPC path. All three published and executed
the package-only NativeAOT windowless startup and Hosting binding probe. Each
run verified the same 30 package hashes. This gate's AOT check is startup, not
the full desktop scenario; the separate full smoke and Hosting runs above
provide their own narrower behavioral evidence.

The final Hosting sample (`ef8b951`; integrated run `131495a`) passed its full
`--run` helper on Windows and Apple Silicon: managed/AOT publication and actual
frontend IPC, unique sequential/concurrent scopes, overlapping awaited calls,
async disposal before reply, explicit deny precedence and lifecycle cleanup.
The root reviewer also used the Windows NativeAOT window interactively: entering
`검증` returned `Hello from the Generic Host, 검증` with a shared scoped dependency;
status returned `ready` from a separate receiver; reset displayed the ACL denial;
closing the window exited normally. This interaction is additional to the
automated smoke, not a substitute for its concurrency/disposal assertions.

## Reproduction and retained evidence

From the integration checkout, after collecting all four native RID assets:

```powershell
./scripts/pack-integration.ps1 -Version 0.1.0-readiness.1
./scripts/test-package-feed.ps1 -Feed <feed> -Version 0.1.0-readiness.1 -TargetFramework net8.0 -EvidenceDirectory artifacts/package-consumer
npm ci --prefix tests/frontend-bindings
node tests/frontend-bindings/run.mjs <feed> 0.1.0-readiness.1 0.1.0-readiness.1 --new
node samples/NetWiv.HostingApp/verify.mjs <feed> 0.1.0-readiness.1 win-x64 --run
```

The package gate needs a private installation containing only the selected SDK
and runtime major, with `DOTNET_ROOT` and PATH set accordingly. Use `osx-arm64`
for the Hosting helper on Apple Silicon. The frontend-binding harness currently
supports Windows/Linux x64 only. macOS full desktop execution uses the temporary
[awake-display procedure](macos-monitor-sleep/README.md).

Ignored `artifacts/` retain build logs, TRX, package hashes, provenance, structured
consumer reports and focused conformance JSONL. On Windows the integrated logs
use `integrated-*`; the binding report uses `frontend-bindings-win32-*` and the
Hosting report uses `samples/NetWiv.HostingApp/artifacts/hosting-win-x64-*`.
The [Mac display record](macos-monitor-sleep/README.md) includes compact original
full-smoke logs and executable hashes. Transferred Mac archives also retain
their source revision alongside reports, because an archive has no `.git`.

These retained directories are private local diagnostics, not upload bundles.
Earlier package transcripts, environment dumps and consumer reports can contain
account names, machine details and absolute paths, including on failure. Do not
attach those historical files or archive the entire run directory. Regenerate
the public evidence using the current helpers and review the exact files to be
shared; keep raw logs, copied consumers, caches and private diagnostics local.

## Limits and investigated failures

- Intel Mac, macOS 12/13 execution, Rosetta, physical Linux, Wayland, multiple
  displays, Developer ID signing/notarization and complete Tauri conformance
  remain outside this run. The release workflow schedules all four desktop RIDs
  on .NET 8 and Linux on .NET 9/10; local runs do not claim those CI outcomes.
- The optional pinned MIME corpus was unavailable and explicitly skipped. The
  Windows CLI symlink fixture is Unix-only; native unit tests skip the
  interactive UI-thread case, which the desktop smoke covers separately.
  Shared Node tests passed 35 with five platform-dependent skips; release-bundle
  tests passed five. Package project checks passed for .NET 8/9/10, including
  the added app/image resources; the synthetic failure-evidence check passed.
- macOS 27 exposed an upstream Mach-O strip issue. Native builds used the
  scoped `CARGO_PROFILE_RELEASE_STRIP=none` override, retaining pinned Rust
  1.95.0. An asleep display produced zero active CoreGraphics displays;
  temporary `caffeinate` resolved the full-smoke failure without changing code
  or relaxing assertions. See [macOS details](../macos.md).
- Windows AOT publication emitted the existing EventLog/Threading.AccessControl
  informational diagnostic; tested logging/IPC paths passed. Mac AOT emitted
  the ignored `-ld_classic` linker warning. Neither establishes those unexecuted
  logging or older-linker paths as covered.
- An initial root conformance publish accidentally selected the legacy Windows
  runtime and failed during window teardown. The recorded comparison explicitly
  republishes with `-p:NetWivRuntime=wry`, the production backend under review;
  legacy-backend acceptance is not claimed.

The root reviewer inspected the complete production changes, their callers,
tests and documentation; resolved integration failures; read execution evidence;
and performed the Windows UI interaction above. Six GPT-6.1 Sol high agents
implemented the scoped work, with a seventh diagnosing the Mac display condition.
