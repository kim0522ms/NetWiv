# Hosting package consumer

A small package-only desktop app combines the .NET Generic Host, options,
logging, constructor injection, scoped instance commands, a feature plugin and
generated frontend bindings. The equipment status is synthetic application I/O;
this sample does not connect to hardware or claim vendor SDK compatibility.

The app targets .NET 8 and can be built with .NET SDK 8/9/10. Node must satisfy
`package.json` (20.19+ or 22.12+). Use a logged-in desktop with WebView2 on Windows
x64, GTK3/WebKitGTK 4.1 on Ubuntu 24.04 x64, or WKWebView on macOS 12+ arm64/x64.
NativeAOT additionally needs the target platform's native compiler/linker:
Visual Studio C++ build tools/Windows SDK, Linux clang/zlib development files,
or Xcode Command Line Tools. Ordinary consumers do not need Cargo.

## Restore and build

Use the actual matching version of `NetWiv.Hosting`, `NetWiv.Platform.Desktop`,
`NetWiv.Build` and `NetWiv.Generators` in your package feed. A maintainer can
create the feed with [pack-integration.ps1](../../scripts/pack-integration.ps1).
Do not substitute a version whose packages are unavailable. The sample does not
hardcode a release version, inherit framework build properties, or reference
framework source projects; it intentionally lives outside `NetWiv.sln`.

Copy this directory anywhere, then from the copied sample root, set your package
version and feed. In PowerShell:

```powershell
$env:NETWIV_PACKAGE_VERSION = Read-Host 'Actual NetWiv package version'
$feed = Read-Host 'Absolute package feed directory'
# Configure sources for all subsequent builds, including the inner binding build.
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content NuGet.Config
dotnet nuget add source $feed --name netwiv --configfile NuGet.Config
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org --configfile NuGet.Config
dotnet restore src-netwiv -r win-x64
npm ci
npm run build
dotnet build src-netwiv -c Release -r win-x64 --no-restore
dotnet run --project src-netwiv -c Release -r win-x64 --no-build
```

On Linux/macOS use `export NETWIV_PACKAGE_VERSION=...`, and replace `win-x64`
with `linux-x64`, `osx-arm64` or `osx-x64` for your actual host. The same dotnet/npm
commands apply; create the empty config with
`printf '%s\n' '<configuration><packageSources><clear /></packageSources></configuration>' > NuGet.Config`.
`npm run build` first builds a managed assembly with
`NetWivBindingsOnly=true` into separate binding intermediates, exports wrappers,
and bundles the frontend with the unchanged `@tauri-apps/api@2.11.1`. Rebuild it
after command signatures or frontend code change. A normal desktop build fails
with an actionable message if the frontend has not been produced.

## Publish and run

After building the frontend, publish on the target host. For Windows x64:

```powershell
# Self-contained managed .NET; also works with dependencies that are not AOT-safe.
dotnet publish src-netwiv -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishSingleFile=false -p:UseAppHost=true -o publish/managed
./publish/managed/HostingApp.exe

# Optional NativeAOT. All application dependencies must support trimming/AOT.
dotnet publish src-netwiv -c Release -r win-x64 --self-contained true -p:PublishAot=true -o publish/aot
./publish/aot/HostingApp.exe
```

For framework-dependent publishing, use `--self-contained false` with the managed
properties and install the compatible .NET runtime on the destination. Keep
separate publish directories for each mode and distribute the complete directory,
including `appsettings.json` and native assets. On Linux/macOS the executable is
`HostingApp` without `.exe`. See [deployment](../../docs/deployment.md).

## Ownership and permissions

`GreetingCommands` receives an invocation workspace and `IOptions<GreetingOptions>`
through its constructor. Its method-injected workspace comes from the same
scope. `EquipmentPlugin` is also a scoped DI receiver; every accepted invocation
gets a fresh receiver and workspace. Its async receiver disposal and dependency
disposal complete before the guest receives a result.

`EquipmentPlugin.CreateLifecycle(audit)` explicitly creates the object installed
with `builder.Desktop.Plugin(...)`. It registers selected commands and permission
definitions once. This object is never registered in DI. Its `Initialize` does
not run on the DI receivers: receivers use constructor dependencies and report
`lifecycleInitialized: false`. The lifecycle object is disposed with the app;
the scoped receivers use `DisposeAsync` at invocation completion. The singleton
audit only retains IDs/counters, never scoped services, and is caller-owned.

The app manifest grants `greet` only to the local `main` window. The plugin
registers `get_status` and `reset` explicitly. The capability includes both
`equipment:allow-reset` and `equipment:deny-reset`; reset is denied because denies
win. `unselected` is generated into the frontend bindings and explicitly allowed
by a capability, but deliberately not registered. After authorization the desktop
runtime's native plugin fallback rejects its wrapper with `not implemented`;
the generated C# command body never runs. Binding generation never
registers commands or grants permission. All declarations are in this assembly,
so this sample does not depend on referenced-assembly binding aggregation.

## Automated verification

From the repository root, supply the real feed, matching version and host RID:

```powershell
node samples/NetWiv.HostingApp/verify.mjs $feed $env:NETWIV_PACKAGE_VERSION win-x64 --build-only
node samples/NetWiv.HostingApp/verify.mjs $feed $env:NETWIV_PACKAGE_VERSION win-x64 --run
```

The helper copies the sample to a fresh consumer directory, isolates NuGet cache,
maps `NetWiv.*` packages to the supplied feed, hashes the packages and puts a
failing Cargo shim first on PATH. It restores the locked npm dependencies, tests
the smoke assertions, generates the frontend, and publishes both self-contained
managed and NativeAOT apps. `--build-only` is the default and never launches a GUI.
`--run` additionally executes both resulting apps in your desktop session with
`--smoke-test`. Both modes are explicit in `report.json`; publishing alone is
never recorded as execution. Logs and package hashes are under
`artifacts/hosting-<rid>-<timestamp>/`, or `NETWIV_TEST_ARTIFACTS` if set.

The embedded frontend calls generated wrappers through the real WebView IPC to
check configuration/constructor injection, shared constructor/parameter scope,
unique sequential and concurrent identities, actual overlap during await,
async disposal before reply, deny-over-allow, no receiver activation on denial,
and rejection of a generated but unselected command. It reports completion
through a smoke-only authorized command; the host closes through
`IHostApplicationLifetime.StopApplication`, then checks lifecycle disposal.
A missing frontend report fails the run or times out. Smoke-only audit/finish
commands are not registered in an ordinary run.

`npm test` tests that these assertions reject broken observations using synthetic
fixtures; it does not establish DI, native execution or real IPC. `--run` provides
those automated execution results on the host that runs it. Record that host's
physical/VM/headless status and OS/WebView versions separately. These checks do
not establish manual GUI acceptance, hardware I/O or full Tauri compatibility.
For Linux, an existing desktop launcher can be selected with
`NETWIV_GUI_LAUNCHER`; `NETWIV_DOTNET` selects an absolute SDK executable.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
