# .NET SDK and runtime compatibility

The `backport/dotnet8` branch lowers the managed package and default desktop
application target to `net8.0`. The same packages can be referenced by `net8.0`,
`net9.0` and `net10.0` applications. This does not change the previously published
`0.1.0-beta.1` bundle, which still contains .NET 10-only packages.

## Building and consuming

- Install .NET SDK 8.0.406 or later, including SDK 9 or 10. `global.json` selects
  the newest installed compatible stable SDK. CI pins each SDK in a separate
  installation so child CLI processes cannot accidentally use a newer SDK.
- Open/build `NetWiv.sln`. Generated applications also use `.sln`; SDK 8 cannot
  read `.slnx` files. C# 12 is the source-language baseline.
- Libraries and the CLI target `net8.0`; the source generator remains
  `netstandard2.0`. No separate package set is needed for each .NET version.
- The CLI and generated managed desktop apps use `RollForward=Major`. They use
  .NET 8 when installed and can run on .NET 9/10 when .NET 8 is absent. Existing
  custom apps control their own target and runtime roll-forward policy.
- A generated desktop backend defaults to .NET 8. To publish with the .NET 9 or
  10 NativeAOT compiler, change its `TargetFramework` to `net9.0` or `net10.0`.
  The optional Blazor frontend uses the selected SDK's template target.
- NativeAOT applications still need no separately installed .NET runtime on an
  end user's machine. OS requirements and the native WebView adapter are
  unchanged; macOS still requires 12 or later.

`System.Text.Json` 10.0.12 supplies the JSON contracts already used by NetWiv,
including nullability metadata and directional ignores, on all three runtimes.
Its package version does not require the .NET 10 runtime; the package includes
[a .NET 8 target](https://www.nuget.org/packages/System.Text.Json/10.0.12).
MSBuild tasks compile against private .NET 8-compatible MSBuild reference
packages and use the build host's MSBuild assemblies at execution time.
`NetWiv.Hosting` enables the configuration binding source generator for consumers;
SDK 8/9 need this explicitly for NativeAOT-safe `Services.Configure<T>` calls.

## Validation

The existing desktop CI uses SDK 8 for Windows x64, Linux x64, macOS arm64 and
macOS x64. Separate Linux jobs exercise SDK/runtime 9 and 10 with the same
`net8.0` source libraries, managed tests and NativeAOT Core tests; they do not
rebuild Rust. The `CI required` check includes these jobs.

Release packaging builds one feed and reuses it in six separate consumer
jobs: Linux x64 with SDK/runtime 8, 9 and 10, plus Windows x64 and macOS arm64/x64
with SDK/runtime 8. The baseline covers all packaged native RIDs; keeping newer
SDK/runtime coverage on Linux bounds the CI cost. Each job uses a private
`DOTNET_ROOT`, removes
any extra LTS runtime installed by `setup-dotnet`, and verifies that no other
runtime major is visible. A mixed installation or an ambient runtime-policy
override fails the check.

Each job installs and runs the packaged CLI, publishes the generated React
solution via `netwiv build --framework-dependent`, and executes its `net8.0`
apphost through the WebView/IPC smoke flow.
A test probe inside the generated app asserts its actual runtime major. macOS
also bundles and executes the managed `.app` through the same GUI/IPC scenario.
Each job then
publishes/runs a package-only NativeAOT desktop consumer targeting `net8.0`,
`net9.0` or `net10.0`, including configuration binding from the packaged Hosting
dependency with warnings as errors. The draft Release job requires all six
consumer jobs to pass. No package is published to NuGet.org.

Consumer evidence artifacts retain execution logs, `dotnet --info`, the test
revision, OS/architecture, exact feed package SHA-256 hashes and the bundle's
`SOURCE.txt` when present. These are hosted validation jobs; they do not replace
physical-device desktop evidence. Windows/macOS .NET 9/10 package consumers and
macOS minimum-OS acceptance remain outside this bounded release matrix.

To select a newer test runtime while keeping library/package targets at .NET 8:

```sh
# First select the matching SDK; verify with dotnet --version.
NetWivTestFramework=net9.0 dotnet build NetWiv.sln -c Release
NetWivTestFramework=net9.0 dotnet run --project tests/NetWiv.Core.Tests -c Release --no-build
NetWivTestFramework=net9.0 dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build
NetWivTestFramework=net9.0 dotnet test tests/NetWiv.Generators.Tests -c Release --no-build
NetWivTestFramework=net9.0 dotnet test tests/NetWiv.Api.Tests -c Release --no-build
```

Use `net10.0` for SDK 10, or omit the variable for the baseline. Test fixture
applications follow the test runtime so nested `dotnet run/watch/publish`
commands exercise that version too.

Local validation on macOS 15.4 / Apple Silicon (2026-10-11):

| Check | SDK 8.0.406 / net8.0 | SDK 9.0.200 / net9.0 | SDK 10.0.301 / net10.0 |
| --- | --- | --- | --- |
| Full solution build | Pass, no warnings | Pass, no warnings | Pass, no warnings |
| Core managed tests | 203 passed, 2 skipped | 203 passed, 2 skipped | 203 passed, 2 skipped |
| API / generator tests | 32 / 118 passed | 32 / 118 passed | 32 / 118 passed |
| CLI tests | 99 passed, 2 skipped | 99 passed, 2 skipped | 99 passed, 2 skipped |
| NativeAOT Core | 203 passed, 2 skipped | 203 passed, 2 skipped | 203 passed, 2 skipped |
| NativeAOT desktop | Windowless + 3 GUI passes | Windowless + 3 GUI passes | Windowless + 3 GUI passes |
| Package-only CLI / generated managed GUI + IPC / NativeAOT + binding | Passed, only runtime 8 installed | Passed, only runtime 9 installed | Passed, only runtime 10 installed |

The native adapter binaries were reused; Rust was not changed. Core skips cover
Windows-specific behavior and the optional external inference corpus. CLI skips
cover Windows-specific checks. The modified `DevCommand`/`NewCommand` code had
94.1% line / 78.09% branch coverage; every changed executable line was hit.
The development regression checks cover empty feature overrides, configured
feature lists, invalid feature rejection, configuration reload and child cleanup.

A previous feasibility run had one AppKit `NSViewUpdateVibrancyForSubtree`
SIGTRAP on .NET 8. It did not reproduce in these three consecutive GUI runs per
version; no root cause or native crash fix is claimed. The original backport
commit `c3dcb24` subsequently passed [all four desktop CI jobs and SDK 9/10
checks](https://github.com/kim0522ms/NetWiv/actions/runs/38138372341).
Minimum-OS acceptance still needs separate hardware evidence. The isolated
Release consumer gate added after review was exercised locally on macOS for all
three majors; its Linux release jobs still require a tagged workflow run.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
NetWiv is an independent project and is not affiliated with, endorsed by, or
officially approved by The Tauri Programme within the Commons Conservancy.
