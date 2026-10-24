# Framework development

This guide is for building NetWiv itself. Application developers should start
with the [package quickstart](integration-quickstart.md).
Run the commands below from the repository root.

## Prerequisites

Use .NET SDK 8.0.406 or later (including 9 and 10) and the pinned Rust toolchain for native adapter builds.
Packing the complete feed requires SDK 10 or later for the three Blazor Hybrid targets. Windows
requires WebView2, the C++ build tools and Windows SDK. The Linux baseline is
Ubuntu 24.04 with GTK3, WebKitGTK 4.1 and NativeAOT/native linker dependencies.
See [Linux X11 and Wayland](linux.md) for session requirements and feature limits.
macOS requires Xcode Command Line Tools and uses the system WKWebView; see the
[macOS guide](macos.md) for arm64 build and signing instructions.
Frontend tooling must satisfy the selected template's `package.json` engines.

The [native build guide](../native/netwiv-native/README.md) describes dependency
artifacts and the signature-verified libsodium source required by Stronghold.
Ordinary app consumers use prebuilt native NuGet assets and do not need Cargo.

## Build and test

Prepare the pinned libsodium inputs and build the native adapter first, following
the [native build guide](../native/netwiv-native/README.md). The solution build
copies the native DLL/SO/dylib from `artifacts/native/<rid>` and requires it to exist.
Use `scripts/build-native.ps1` on Windows or `bash scripts/build-native.sh` on
Linux/macOS. Build each platform on its matching host. On WSL, use a Linux filesystem and
separate Windows/Linux outputs to avoid mixed restore metadata and intermediates.

```sh
dotnet build NetWiv.sln -c Release
dotnet run --project tests/NetWiv.Core.Tests -c Release --no-build
node scripts/prepare-test-tools.mjs
# On macOS, also export the NETWIV_MINISIGN_PATH printed by this helper.
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build
dotnet test tests/NetWiv.Generators.Tests -c Release --no-build
dotnet test tests/NetWiv.Api.Tests -c Release --no-build
pwsh -File scripts/test-package-smoke.ps1
node --test tests/conformance/scripts/compare-results.test.mjs
```

The PowerShell 7 package-consumer project check runs without packing or restoring
packages. It verifies unique package references and keeps the external test plugin
as a separate consumer assembly for .NET 8, 9 and 10. CI runs this check before
release packaging; the release workflow additionally publishes and executes the
consumers against the built package feed.
External embedded fixtures are copied into the isolated consumer, preserving
their bytes and logical names.

The instance-command probe delivers simulated native callbacks without the test
runner's synchronization context and restores the caller's context afterward.
A regression holds caller continuations to verify that IPC replies and scope
disposal still complete; the existing timeouts and parallel checks remain enabled.

The test-tool preparation downloads checksum-pinned official minisign 0.12
binaries for the CLI signer interoperability test on Windows/Linux x64. On macOS
arm64 it builds checksum-pinned 0.12 source using CMake, pkg-config and libsodium
(`brew install cmake pkgconf libsodium`). The official macOS binary is arm64-only.
Export the printed `NETWIV_MINISIGN_PATH` before running the CLI suite locally;
the helper writes it to `GITHUB_ENV` automatically in CI.

Publish the desktop smoke host after preparing the native binaries:

```sh
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r win-x64
# On Linux, use -r linux-x64; on macOS, use -r osx-arm64.
```

On macOS, keep `App.Run()` on the process's original main thread and use a
logged-in desktop session with an awake display. A sleeping display may be online
while CoreGraphics reports no active monitors, causing the desktop smoke's
`native monitor geometry` assertion to fail. For remote validation, temporarily
keep the display awake for the executable's lifetime:

```sh
caffeinate -d -u -t 120 /path/to/publish/NetWiv.Desktop.Smoke
```

This does not change permanent power settings. Run the focused packaging checks with
`dotnet run --project tests/NetWiv.Cli.Tests -c Release -- --filter macOS`;
they invoke real `cc`, `codesign`, `plutil` and `hdiutil`. The full CLI signer suite
also needs the pinned minisign 0.12 executable; see [macOS setup](macos.md).

Run the published executable in a desktop session. Set
`NETWIV_SMOKE_HOLD_MS=120000` to keep its UI open for inspection. Publishing
alone is not a GUI test. See [conformance](../tests/conformance/README.md),
[isolation](isolation.md) and [consumer verification](frontend-generator.md#verification)
for separate validation paths.

Run `NetWiv.Desktop.Smoke --fs-guest-smoke` from both managed and NativeAOT
outputs to check the shipped frontend file API against the real WebView bridge.
It covers UTF-8/UTF-16 text and lines, empty files, omitted write options,
binary preservation, append/exclusive creation, generated command error messages
and typed backend events with exact large-integer payloads.
The fixture uses synthetic files and removes its owned temporary directory.

On Windows, run the managed and NativeAOT desktop smoke executables with
`--notification-smoke` to exercise a windowless native notification request and
await the pinned backend's `show()` result. The Windows command reports backend
errors asynchronously without joining its worker on the UI thread. A successful
result means the OS call was accepted; it does not establish banner visibility,
installed app identity registration or notification-center delivery. Notifications
already admitted to the native runtime retain their result after caller cancellation,
like other UI mutations; document retirement suppresses stale guest replies.

On Linux, select the display backend explicitly. For a logged-in Wayland
desktop, run both the managed and published NativeAOT smoke hosts:

```sh
dotnet build tests/NetWiv.Desktop.Smoke -c Release -p:PublishAot=false
bash scripts/run-linux-smoke-desktop.sh \
  "$PWD/tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/NetWiv.Desktop.Smoke" wayland
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r linux-x64
bash scripts/run-linux-smoke-desktop.sh \
  "$PWD/tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/linux-x64/publish/NetWiv.Desktop.Smoke" wayland
```

The Wayland path reuses the existing `WAYLAND_DISPLAY` socket and reachable
`DBUS_SESSION_BUS_ADDRESS`; a relative socket name also requires
`XDG_RUNTIME_DIR`. It needs `gdbus`, `timeout` and the desktop's own tray host.
It does not create a nested compositor, Xfce desktop or private D-Bus session,
and does not inject renderer workarounds. It preserves `DISPLAY`, allowing
optional plugins with independent X11 connections to use XWayland.
Such plugin calls do not establish native Wayland support for those features.

The wrapper sets `GDK_BACKEND` and `NETWIV_EXPECT_BACKEND` to the requested
backend. The smoke asserts the actual `DesktopRuntime.Backend` at setup;
an X11 runtime cannot pass a Wayland expectation. Direct smoke launches can
also set `NETWIV_EXPECT_BACKEND`; invalid values fail before runtime startup.
Use `x11` instead of `wayland` for the existing nested Xephyr/Xfce path with
its separate D-Bus session and tray host.

Automated smoke checks do not establish native input, rendering or menu/tray
interaction acceptance. Run the separate [Wayland interaction probe](../tests/NetWiv.Wayland.Smoke/README.md)
in both managed and NativeAOT modes for its manual checks. See
[Wayland validation limits](implementation-status.md#wayland-validation) before
claiming coverage.

Linux dialog changes have a separate [dialog probe](../tests/NetWiv.Dialog.Smoke/README.md).
Run its managed and NativeAOT automatic modes on both `wayland` and `x11` for
pending-dialog responsiveness, caller cancellation and lifecycle cleanup.
Then use interactive mode for file/folder selection, save, custom message
buttons, user cancellation and session scope grants. Build the native adapter
first: the managed runtime and native library must both use ABI 3.

## Frontend SDK maintenance

The `@netwiv/api` source lives in `packages/api`. Run its package tests and the
isolated tarball/type/template-parity check from the repository root:

```sh
npm ci --prefix packages/api --no-audit --no-fund
npm test --prefix packages/api
node tests/frontend-api/run.mjs
```

When changing shipped SDK files, rebuild the package and regenerate the template
snapshot with `node scripts/generate-frontend-template-overlays.mjs`. Review the
resulting snapshot together with the source; do not hand-edit generated copies.
Pinned upstream comparison tests remain internal references, separate from the
public core/plugin facade tests. They do not support direct upstream imports.

## Continuous integration

The [Build and test workflow](../.github/workflows/ci.yml) runs on pushes to
`main`, pull requests and manual dispatch. Pushes limited to `docs/`, root
`README.md` and `AGENTS.md` skip automatic runs; manual dispatch always remains
available. Pull requests always run change classification and the `CI required`
check. PRs limited to those same paths skip the build jobs. Other paths, including
READMEs under templates or source directories, require the desktop matrix, CLI
groups, Windows installer job, frontend API job and .NET 9/10 compatibility jobs
to succeed.
Classification failures, cancelled builds and unexpected skips fail the required
check.

The `main` branch has two rulesets. History protection blocks force pushes and
deletion without any bypass actors. A separate ruleset requires a pull request
with resolved review conversations and `CI required` from GitHub Actions against
an up-to-date base branch; repository administrators can bypass these PR and CI
requirements. Review approval is optional so a single maintainer can merge their
own PRs.

Windows Server 2025 x64, Ubuntu 24.04 x64 and macOS 15 Apple Silicon (`macos-15`)
retain independent native and NativeAOT
execution. CI separates work that does not depend on the native build:

| Job | Required coverage |
| --- | --- |
| Desktop, three RIDs | Rust build/tests, managed and AOT Core tests, native startup diagnostics and real WebView smoke; platform-specific dialog, isolation and sandbox scenarios |
| CLI contracts, three RIDs | Configuration, hooks, templates, permissions, signing/packaging contracts, API and platform script tests |
| CLI lifecycle, three RIDs | Real consumer publishing in managed/framework-dependent/AOT modes, mode-switch cleanup and managed/AOT development restarts |
| Windows installers | Actual NSIS/MSI installation, upgrade and removal in both installation scopes on a dedicated runner |
| Frontend | SDK runtime tests, isolated packed consumer types/exports and template builds |
| SDK 9/10, Linux | Managed Core/CLI/API/generator coverage and executed NativeAOT Core tests |

The CLI groups partition the complete test registry: every test belongs to
exactly one group. `scripts/check-cli-groups.mjs` checks that their union equals
the full registry, that they do not overlap and that unknown groups fail.
Each group has its own checkout, environment and build outputs. Do not execute
multiple groups concurrently in the same checkout: tests change process-wide
settings and build referenced projects.

Linux .NET 8 covers the original five starter templates and all package-manager choices.
Other OS/SDK jobs generate the React base template, the Angular overlay and Blazor,
and still check every manager and the full discovery list. Unselected
template combinations are not claimed as passes on those hosts. The full
generator suite runs on Windows, Linux and macOS ARM, plus SDK 9/10.
The optional `blazor-hybrid` starter has a separate CLI generation regression.
All three supported desktop jobs run the `NetWiv.BlazorHybrid.Tests` managed
transport and lifecycle harness. They publish and execute
`NetWiv.BlazorHybrid.Aot.Tests` in the ordinary Microsoft managed host
(`--managed-host --native-webview`) and experimental NativeAOT host (protocol
checks and `--native-webview`). Linux CI uses a nested X11 desktop; it does not
establish physical Wayland acceptance. These checks do not establish IDE or
arbitrary Razor-library acceptance. See the [Hybrid validation boundary](blazor-hybrid.md#validation-boundary).
The SDK 9/10 jobs also run matching Hybrid managed tests and NativeAOT protocol
tests without rebuilding Rust. Release package consumers run matching Hybrid
native WebView tests on each job's target framework.
API tests run on all three supported desktop RIDs. macOS x64 is excluded from
both ordinary CI and tag releases; it is not a supported distribution target.

The complete solution is compiled on Linux. Other desktop jobs build their
required project graphs rather than unrelated samples and test projects.
Platform-independent JavaScript harness, inventory and license tests run once
in the Linux contracts job. Core and CLI are executable test harnesses and must
be run with `dotnet run`; `dotnet test` alone does not execute them.

The generator suite runs instance-command runtime scenarios in a non-parallel
xUnit collection, separately from CPU-heavy Roslyn compilations. IPC dispatch
and asynchronous scope disposal need ThreadPool workers. Keeping them separate
avoids competition with compiler workloads for those workers during the existing
10-second callback deadline. Compiler collections remain parallel, and the
runtime scenarios retain their explicit
concurrent-invocation checks. The same scenarios also run in the managed and
NativeAOT Core harnesses.

Each desktop job publishes and executes the Core suite with NativeAOT, publishes the
NativeAOT desktop smoke host, checks incompatible native-library startup diagnostics
and runs the automated WebView smoke scenario. Linux uses Xvfb with a nested
X11/Xfce desktop and tray host. The macOS desktop job additionally runs windowless
startup and NativeAOT isolation IPC checks. The macOS contracts group retains
a separate five-minute app/DMG step, excluded from its remaining tests. It
creates and executes an ad-hoc-signed Mach-O app fixture, validates its plist/resources, builds and verifies a DMG, rebuilds the app
with the DMG's `/Applications` shortcut present, and checks signing failures
using real macOS tools. These cases run once on macOS arm64. CLI logs include
each test's start and elapsed time to make stalled cases identifiable.

The desktop guest smoke handles initial module-load failures through the
preinstalled bridge. Its watchdog names each guest still waiting and its last
functional startup stage; these fixed labels are assertion state, not retained
reports. The full scenario and its 180-second deadline remain unchanged.

The macOS desktop smoke step sets `__XPC_MTL_SHADER_CACHE_SIZE=0` for its
WebContent subprocesses. On macOS 15, Metal's shared shader disk cache can block
a WebContent rendering thread in `flock`, preventing even the initial document
request from reaching the app. This CI-only workaround bypasses that cache;
GPU rendering, concurrent WebViews and all functional assertions remain enabled.
It does not change the framework or consumer defaults, and is not a public
NetWiv configuration option. Revalidate the workaround when changing the runner
image or WebKit version.

Core and CLI harnesses reuse their preceding builds with `--no-build`. NativeAOT
publishes remain separate because they compile different executable targets;
managed and NativeAOT execution both provide required coverage. Native caches
remain separated by runner image and RID. All managed validation
jobs share the setup action and NuGet caches keyed by OS, architecture, exact SDK
and project inputs, with restore prefixes for dependency reuse. Normal
build/restore commands still validate their inputs.
CI uses command exit codes and test assertions to determine success. It does not
collect or upload separate validation reports or diagnostic archives.

For a local comparison, use the same SDK version, native binaries, execution
mode and dependency-cache state before and after the change. Start with clean
project build outputs in isolated checkouts. Run each required command and check
its exit status; measure elapsed time in the shell. CLI group commands are:

```sh
dotnet build tests/NetWiv.Cli.Tests -c Release
node scripts/check-cli-groups.mjs
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build -- --group contracts
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build -- --group lifecycle
```

By default local template tests cover the full matrix. Set
`NETWIV_TEST_TEMPLATE_MATRIX=representative` only when reproducing that CI scope;
unknown values fail. Use separate checkouts to measure concurrent groups.
Report the measured command scope, cold/warm cache distinction and excluded
steps. A local parallel replay is not GitHub runner throughput or queue time.
The default Windows installer acceptance mode and `-AllowLocalMachine` temporarily
change runtime registration; use a dedicated validation machine for those modes.
The bounded, non-elevated `-CurrentUserOnly` mode leaves runtime registration
unchanged; see [its prerequisites and limits](windows-installers.md).
No benchmark reports,
environment inventories or measurement helpers belong in the repository or CI.

The existing desktop-only ignored Rust test and optional external MIME corpus
remain explicitly skipped in their suite output. Passing CI does not establish
Wayland, manual input, multi-monitor, signed installer or complete TAURI®
conformance acceptance. The macOS 15 jobs do not establish the macOS 12 minimum
OS baseline, Developer ID signing or notarization. Adding jobs is not evidence
of a successful hosted run; inspect the workflow results for the exact revision.
NuGet publication is separate from this workflow. Version tags can reuse this
CI to prepare a [draft Release package bundle](release-packages.md) without
publishing to NuGet.org.

## Create a local package feed

Build Windows, Linux and macOS arm64 native artifacts first. In PowerShell 7, select a
new package version and an unused output directory:

```powershell
./scripts/pack-integration.ps1 -Version 0.1.0-local.1
# Output feed: artifacts/integration/0.1.0-local.1/packages
```

This packages Core, Hosting, optional BlazorHybrid, Generators, Build, Platform.Desktop, CLI and the managed plugins.
It refuses an existing output directory and does
not publish to a remote feed. Use a new version for changed contents to avoid
NuGet cache collisions. Supply the resulting `packages` directory and version
to application developers; a fresh clone does not contain a prebuilt feed.

The direct `dotnet new` React template package is a separate delivery path.
The integration script's CLI includes `netwiv new` with all six starter choices.

## Release validation

Validate package-only consumers against the exact packages intended for release,
including NativeAOT execution, frontend IPC and installer lifecycle on each
supported platform. The Windows package-consumer check runs each managed
deployment from a separate copy, keeping WebView2 profile locks out of the
build output that the next deployment mode cleans. State the checks performed,
results, skips and missing dependencies in the PR; do not add evidence collection
or report storage to code or CI/CD.
See [support and validation](implementation-status.md) for outstanding coverage.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.

See [SDK compatibility](dotnet-compatibility.md) for the .NET 8 baseline and the separate 9/10 consumer tests.
