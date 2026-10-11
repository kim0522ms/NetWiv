# Framework development

This guide is for building NetWiv itself. Application developers should start
with the [package quickstart](integration-quickstart.md).
Run the commands below from the repository root.

## Prerequisites

Use .NET 10 and the pinned Rust toolchain for native adapter builds. Windows
requires WebView2, the C++ build tools and Windows SDK. The Linux baseline is
Ubuntu 24.04 with GTK3, WebKitGTK 4.1 and NativeAOT/native linker dependencies.
macOS requires Xcode Command Line Tools and uses the system WKWebView; see the
[macOS guide](macos.md) for arm64/x64 build and signing instructions.
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
dotnet build NetWiv.slnx -c Release
dotnet run --project tests/NetWiv.Core.Tests -c Release --no-build
node scripts/prepare-test-tools.mjs
# On macOS, also export the NETWIV_MINISIGN_PATH printed by this helper.
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build
dotnet test tests/NetWiv.Generators.Tests -c Release --no-build
dotnet test tests/NetWiv.Api.Tests -c Release --no-build
node --test tests/conformance/scripts/compare-results.test.mjs
```

The test-tool preparation downloads checksum-pinned official minisign 0.12
binaries for the CLI signer interoperability test on Windows/Linux x64. On macOS
arm64/x64 it builds checksum-pinned 0.12 source using CMake, pkg-config and libsodium
(`brew install cmake pkgconf libsodium`). The official macOS binary is arm64-only.
Export the printed `NETWIV_MINISIGN_PATH` before running the CLI suite locally;
the helper writes it to `GITHUB_ENV` automatically in CI.

Publish the desktop smoke host after preparing the native binaries:

```sh
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r win-x64
# On Linux, use -r linux-x64; on macOS, use -r osx-arm64 or osx-x64.
```

On macOS, keep `App.Run()` on the process's original main thread and use a
logged-in desktop session. Run the focused packaging checks with
`dotnet run --project tests/NetWiv.Cli.Tests -c Release -- --filter macOS`;
they invoke real `cc`, `codesign`, `plutil` and `hdiutil`. The full CLI signer suite
also needs the pinned minisign 0.12 executable; see [macOS setup](macos.md).

Run the published executable in a desktop session. Set
`NETWIV_SMOKE_HOLD_MS=120000` to keep its UI open for inspection. Publishing
alone is not a GUI test. See [conformance](../tests/conformance/README.md),
[isolation](isolation.md) and [consumer verification](frontend-generator.md#verification)
for separate validation paths.

## Continuous integration

The [Build and test workflow](../.github/workflows/ci.yml) runs on pushes to
`main`, pull requests and manual dispatch. Pushes limited to `docs/`, root
`README.md` and `AGENTS.md` skip automatic runs; manual dispatch always remains
available. Pull requests always run change classification and the `CI required`
check. Documentation-only PRs skip the desktop matrix; other PRs require all
four desktop jobs to succeed. Classification failures, cancelled builds and
unexpected skips fail the required check.

The `main` branch has two rulesets. History protection blocks force pushes and
deletion without any bypass actors. A separate ruleset requires a pull request
with resolved review conversations and `CI required` from GitHub Actions against
an up-to-date base branch; repository administrators can bypass these PR and CI
requirements. Review approval is optional so a single maintainer can merge their
own PRs.

Windows Server 2025 x64, Ubuntu 24.04 x64, macOS 15 Apple Silicon (`macos-15`)
and macOS 15 Intel (`macos-15-intel`)
jobs independently build the native adapter and the .NET solution, then run
Rust unit tests, managed Core/CLI tests, generator/API tests and platform
build-tool script tests. Platform-independent JavaScript harness, inventory and
license tests run once in the existing Linux job, avoiding another runner setup.
Core and CLI are executable test harnesses and must be
run with `dotnet run`; `dotnet test` alone does not execute them.

Each job publishes and executes the Core suite with NativeAOT, publishes the
NativeAOT desktop smoke host, checks incompatible native-library startup diagnostics
and runs the automated WebView smoke scenario. Linux uses Xvfb with a nested
X11/Xfce desktop and tray host. Both macOS jobs additionally run windowless
startup and NativeAOT isolation IPC checks. A separate five-minute packaging
step in each macOS job creates and executes an ad-hoc-signed Mach-O app fixture,
validates its plist/resources, builds
and verifies a DMG, rebuilds the app with the DMG's `/Applications` shortcut
present, and checks signing failures using real macOS tools. This case is
excluded from the remaining CLI step, so it runs exactly once. CLI logs include
each test's start and elapsed time to make stalled cases identifiable.

Managed test steps reuse the solution build with `--no-build`. NativeAOT
publishes remain separate because they compile different executable targets;
managed and NativeAOT execution both provide required coverage. Native and
NuGet caches are separated by runner image and RID, with restore prefixes that
reuse available dependencies after lock/project changes; normal build/restore
commands still validate them. NuGet keys also include the pinned .NET SDK.
Logs, test reports, build binary logs and the published executables are retained
as workflow artifacts for seven days.

The existing desktop-only ignored Rust test and optional external MIME corpus
remain explicitly skipped in their suite output. Passing CI does not establish
Wayland, manual input, multi-monitor, signed installer or complete TAURI®
conformance acceptance. The macOS 15 jobs do not establish the macOS 12 minimum
OS baseline, Developer ID signing or notarization. Adding jobs is not evidence
of a successful hosted run; inspect the workflow results for the exact revision.
NuGet publication is separate from this workflow.

## Create a local package feed

Build Windows, Linux and both macOS architecture native artifacts first. In PowerShell 7, select a
new package version and an unused output directory:

```powershell
./scripts/pack-integration.ps1 -Version 0.1.0-local.1
# Output feed: artifacts/integration/0.1.0-local.1/packages
```

This packages Core, Hosting, Generators, Build, Wry, CLI and the managed plugins, and
writes package SHA-256 hashes. It refuses an existing output directory and does
not publish to a remote feed. Use a new version for changed contents to avoid
NuGet cache collisions. Supply the resulting `packages` directory and version
to application developers; a fresh clone does not contain a prebuilt feed.

The direct `dotnet new` React template package is a separate delivery path.
The integration script's CLI already includes `netwiv new` with all 18 choices.

## Release validation

Validate package-only consumers against the exact packages intended for release,
including NativeAOT execution, frontend IPC and installer lifecycle on each
supported platform. Retain source revisions, toolchain versions and artifact
hashes with the results. Report skips and missing dependencies explicitly.
See [support and validation](implementation-status.md) for outstanding coverage.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
