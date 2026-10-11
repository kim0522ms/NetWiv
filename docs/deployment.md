# Application deployment

NetWiv consumer applications can use the regular .NET runtime or NativeAOT.
The framework remains AOT-compatible; using its libraries does not require an
application or its dependencies to be AOT-compatible.

## Choose a publishing mode

Run these commands from the application root after restoring its local tools:

```sh
dotnet tool restore
dotnet tool run netwiv -- build --no-bundle                       # self-contained .NET (default)
dotnet tool run netwiv -- build --framework-dependent --no-bundle # installed .NET runtime
dotnet tool run netwiv -- build --aot --no-bundle                 # NativeAOT
```

| Mode | Runtime on the destination | Consumer dependency requirements |
| --- | --- | --- |
| Default, self-contained .NET | Included in the publish directory | Compatible with the application's .NET version and target OS/architecture |
| `--framework-dependent` | Install a compatible .NET runtime; generated apps allow major-version roll-forward | Same as regular .NET |
| `--aot` | Compiled into the application | Every dependency must support NativeAOT and trimming; native build tools required |

Managed CLI publishing explicitly disables AOT, trimming and single-file
publishing and retains an apphost executable. Reflection and dynamic loading
remain available. `--aot` and `--framework-dependent` cannot be combined.
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

## Bundles and output

All CLI modes use `target/<requested-target>/release/` (or `debug/`). Each build
replaces that generated publish directory, including previous bundles, to avoid
mixing runtime files from earlier deployment modes. Keep deliverables elsewhere
if you need to preserve them. Distribute the **whole publish directory**, not
just its executable. Framework-dependent installers do not install .NET for you.

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
Package-feed validation executes the generated React GUI/IPC app via the managed
CLI publish path in isolated .NET 8/9/10 environments, plus a NativeAOT consumer.
These checks do not establish full installer acceptance or vendor-SDK support.
