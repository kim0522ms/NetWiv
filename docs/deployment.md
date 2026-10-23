# Application deployment

NetWiv consumer applications can use the regular .NET runtime or NativeAOT.
The framework remains AOT-compatible; using its libraries does not require an
application or its dependencies to be AOT-compatible.

The optional [Blazor Hybrid package](blazor-hybrid.md) defaults to managed .NET on
Windows x64/WebView2. Experimental NativeAOT Hybrid applications use
`AddNetWivBlazorHybridAot` with explicit component, JSON metadata and interop
registrations and the `NetWivBlazorExperimentalAot=true` opt-in. The shared UI can also run as Blazor WebAssembly with a NativeAOT
desktop backend; that mode excludes the Hybrid package reference.

## Choose a publishing mode

Run these commands from the application root after restoring its local tools:

```sh
dotnet tool restore
dotnet tool run netwiv -- build --no-bundle                       # self-contained .NET (default)
dotnet tool run netwiv -- build --framework-dependent --no-bundle # installed .NET runtime
dotnet tool run netwiv -- build --aot --no-bundle                 # NativeAOT
```

For the `blazor-hybrid` starter, Debug and Release default to managed Hybrid.
WebAssembly and experimental NativeAOT Hybrid are explicit choices:

```sh
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode Hybrid --experimental-blazor-aot
dotnet tool run netwiv -- build --no-bundle --aot --blazor-mode WebAssembly
```

The execution mode and `--aot` deployment choice are independent;
an explicit CLI mode overrides the project default and forwarded MSBuild mode
arguments. See the [Hybrid registration requirements and supported behavior](blazor-hybrid.md)
before adding components or third-party Razor libraries.

| Mode | Runtime on the destination | Consumer dependency requirements |
| --- | --- | --- |
| Default, self-contained .NET | Included in the publish directory | Compatible with the application's .NET version and target OS/architecture |
| `--framework-dependent` | Install a compatible .NET runtime; generated apps allow major-version roll-forward | Same as regular .NET |
| `--aot` | Compiled into the application | Every dependency must support NativeAOT and trimming; native build tools required |

Managed CLI publishing explicitly disables AOT, trimming and single-file
publishing and retains an apphost executable. Reflection and dynamic loading
remain available. NativeAOT publishing explicitly enables its required trimming,
even when the project or forwarded arguments set `PublishTrimmed=false`.
`--aot` and `--framework-dependent` cannot be combined.
The selected CLI mode takes precedence over project settings and forwarded
MSBuild arguments for these deployment properties. Direct `dotnet publish`
continues to use the properties you specify yourself.

For example, a direct managed publish on Windows is:

```sh
dotnet publish src-netwiv -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishSingleFile=false -p:UseAppHost=true -o publish/win-x64
```

Use `--self-contained false` for a framework-dependent publish, or
`--self-contained true -p:PublishAot=true` for NativeAOT. The generated frontend
project builds and embeds the frontend during publication. Existing applications
must retain their frontend build targets or run their configured build hook.

Native WebView libraries and OS prerequisites are required in every mode.
Managed publishing does not make a .NET Framework-only or architecture-specific
vendor SDK compatible with modern .NET. C++/CLI remains unsupported by NativeAOT;
a managed deployment still requires a vendor DLL that supports the chosen .NET
runtime and Windows architecture. No WMX or other equipment SDK compatibility is
claimed by these tests.

## Windows browser profiles

On Windows, a webview with no `dataDirectory` (or `null`) stores its WebView2
profile in the application's local data directory, `%LOCALAPPDATA%/<identifier>`.
Give each application a unique configured `identifier` or `AppMetadata.Identifier`.
That identity keeps browser state across managed DLL, apphost and NativeAOT launches
and across deployment locations. Applications using the reserved default identifier
`com.netwiv.app` instead use an isolated `webview/<hash>` subdirectory. The hash is
derived locally from the entry assembly's simple name and the canonical application
directory, with Windows casing normalized; it is not reported or uploaded. Such an
unconfigured application's state follows its application directory, so moving it
starts a separate profile. Linux and macOS defaults are unchanged.
These defaults apply to applications built through `Builder`. An integration that
calls `DesktopRuntime.Run` with its own `RuntimeContext` must supply an app-specific
`AppLocalDataDirectory` or an explicit webview `dataDirectory`; it owns that
lower-level identity choice.

Earlier NetWiv versions on Windows left the default profile location to WebView2, which can
place it beside the host executable. This fails when that directory is not writable,
including managed DLL launches through a protected `dotnet` installation. The new
default does not move or delete an existing profile; existing cookies and browser
storage may therefore appear empty after upgrading. To retain an existing profile,
set `dataDirectory` to its absolute path explicitly and ensure that location is
writable. A safe relative `dataDirectory` continues to resolve under
`%LOCALAPPDATA%/<webview-label>/`; explicit directories keep their existing sharing
behavior and override the new default.

The Windows regression can be run with managed and NativeAOT desktop smoke publish
directories:

```powershell
./scripts/test-windows-webview-profile.ps1 -ManagedDirectory artifacts/desktop-managed -AotDirectory artifacts/desktop-aot
```

It uses synthetic storage to check persistence across launch modes and locations,
separate app identities, unconfigured app isolation, and explicit profile paths.

## Bundles and output

All CLI modes use `target/<requested-target>/release/` (or `debug/`). Each build
replaces that generated publish directory, including previous bundles, to avoid
mixing runtime files from earlier deployment modes. Keep deliverables elsewhere
if you need to preserve them. Distribute the **whole publish directory**, not
just its executable. Framework-dependent installers do not install .NET for you.
[Windows installer settings](windows-installers.md) apply to all deployment modes,
including installation scope, directory selection and shortcut arguments.

Remove `--no-bundle` to use configured bundling, or pass `--bundles`. For example:

```sh
netwiv build --bundles app,dmg --no-sign       # self-contained managed macOS app
netwiv build --aot --bundles app,dmg --no-sign # NativeAOT macOS app
```

`netwiv bundle` packages the last published output without changing its mode.
Bundle metadata is patched in the staged Core DLL for managed applications and
in the staged executable for NativeAOT. macOS managed app bundles keep the
apphost, DLLs, runtime configuration and runtime files together under
`Contents/Resources`, launched by a small executable script in `Contents/MacOS`.
Configured bundle resources also remain under `Contents/Resources`.
Build and bundle on the target OS with the supported architecture. Existing
signing/notarization and installer-acceptance limitations still apply.

## Change from earlier releases

Earlier `netwiv build` versions always published NativeAOT. Add `--aot` to
existing automation that requires the previous behavior. Ordinary `dotnet build`
and `netwiv dev` remain managed; `netwiv dev --aot` remains available. New consumer
templates no longer opt into AOT analysis or publishing automatically. Existing
projects may remove their `PublishAot` / `IsAotCompatible` opt-ins when choosing
managed-only dependencies.

Framework source and release CI retain NativeAOT gates. CLI regression tests
publish and execute self-contained, framework-dependent and NativeAOT consumers,
check embedded frontend assets, exercise dynamic code in managed mode, reject
conflicting modes and missing frontend output, and check switching publish modes.
macOS tests also execute the managed `.app` after staging and ad-hoc signing.
Release package-feed validation executes the generated React GUI/IPC app via the
managed CLI publish path on Windows x64, Linux x64 and macOS arm64 with an
isolated .NET 8 installation. Linux additionally runs isolated .NET 9/10 jobs.
All jobs publish and execute framework-dependent consumers; the .NET 8 baseline
jobs also publish and execute the default self-contained managed consumer.
Every job publishes and executes a package-only NativeAOT consumer's windowless
startup and configuration binding; macOS also runs the generated managed `.app`
in both deployment modes.
The jobs reuse the same package bundle and fail on unsuccessful commands or
assertions. Adding these jobs does not establish a successful hosted or
physical-device run; inspect the workflow results for the tested revision.
These checks do not establish full installer acceptance or vendor-SDK support.
