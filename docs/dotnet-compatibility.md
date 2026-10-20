# .NET SDK and runtime compatibility

Current managed packages and default desktop applications target `net8.0`.
The same packages can be referenced by `net8.0`, `net9.0` and `net10.0`
applications. This is part of `main`, not a separate backport package line.
Historical `0.1.0-beta.1` packages still require .NET 10; use a current matching
package set for .NET 8/9 applications.

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
  The `blazor` WebAssembly starter uses the selected SDK's template target.
  The separate `blazor-hybrid` starter targets .NET 8 and pins its Microsoft
  component packages to 8.0.32. `NetWiv.BlazorHybrid` acceptance currently covers
  Windows x64/.NET 8; the general SDK 9/10 checks below do not establish Hybrid
  acceptance. See [Hybrid dependency and AOT limits](blazor-hybrid.md).
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

The existing desktop CI uses SDK 8 for Windows x64, Linux x64 and macOS arm64. Separate Linux jobs exercise SDK/runtime 9 and 10 with the same
`net8.0` source libraries, managed tests and NativeAOT Core tests; they do not
rebuild Rust. The `CI required` check includes these jobs.

Release packaging builds one feed and reuses it in five separate consumer
jobs: Linux x64 with SDK/runtime 8, 9 and 10, plus Windows x64 and macOS arm64
with SDK/runtime 8. The baseline covers all packaged native RIDs; keeping newer
SDK/runtime coverage on Linux bounds the CI cost. Each job uses a private
`DOTNET_ROOT`, removes
any extra LTS runtime installed by `setup-dotnet`, and verifies that no other
runtime major is visible. A mixed installation or an ambient runtime-policy
override fails the check.

Each job installs and runs the packaged CLI, publishes the generated React
solution via `netwiv build --framework-dependent`, and executes its `net8.0`
apphost through the WebView/IPC smoke flow.
The .NET 8 baseline jobs then publish and execute the default self-contained
managed output. Its runtime configuration must include the bundled framework;
framework-dependent output must request an installed framework. Both modes
assert the actual runtime major and complete the GUI/IPC scenario.
A test probe inside the generated app asserts its actual runtime major. macOS
also bundles and executes the managed `.app` in both deployment modes through
the same GUI/IPC scenario.
Each job then
publishes/runs a package-only NativeAOT desktop consumer targeting `net8.0`,
`net9.0` or `net10.0`, including configuration binding from the packaged Hosting
dependency with warnings as errors. The draft Release job requires all six
consumer jobs to pass. No package is published to NuGet.org.

These hosted validation jobs check process exit codes and behavioral assertions.
Windows/macOS .NET 9/10 package consumers and
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

The package gate's NativeAOT check is windowless startup. WSL/Xvfb runs are
virtual/headless checks and do not establish physical desktop acceptance.
Minimum-OS acceptance and Windows/macOS .NET 9/10 package consumers remain
outside the release matrix.

An intermittent AppKit `NSViewUpdateVibrancyForSubtree` SIGTRAP was observed
in an earlier .NET 8 feasibility run. Later successful runs do not establish
its root cause or a native crash fix. The separate macOS effect-view
accumulation defect is addressed by replacing the prior view before applying
a new supported effect. The [focused lifecycle probe](macos.md#window-effects)
checks native view count and material/state through close/recreation; that
scope must not be reported as resolution of the earlier SIGTRAP.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
NetWiv is an independent project and is not affiliated with, endorsed by, or
officially approved by The Tauri Programme within the Commons Conservancy.
