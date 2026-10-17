# macOS development and distribution

NetWiv's Wry backend supports WKWebView/AppKit on macOS. The managed SDK,
C ABI and unchanged `@tauri-apps/api` guest remain shared with Windows/Linux.
The deployment baseline is macOS 12.0. Consumer builds require a .NET SDK
selected by `global.json` (8 or later). NativeAOT builds additionally need Xcode
Command Line Tools; framework native builds also need Rust 1.95.0. Frontend
builds require the toolchain selected by the template.
WKWebView is provided by macOS.

| CPU | CLI target | .NET RID | Native asset |
| --- | --- | --- | --- |
| Apple Silicon | `aarch64-apple-darwin` | `osx-arm64` | `libnetwiv_native.dylib` |
| Intel | `x86_64-apple-darwin` | `osx-x64` | `libnetwiv_native.dylib` |

The CLI also accepts either RID. Omitting `--target` selects the current process
architecture. Build on the matching Mac architecture; universal binaries are not
implemented. Apple Silicon has local execution evidence; Intel execution and
minimum-OS acceptance still require their own machines.

## Framework source build

From the repository root:

```sh
xcode-select --install # If Command Line Tools are not installed
rustup toolchain install 1.95.0 --profile minimal
node scripts/prepare-native-dependencies.mjs osx-arm64 artifacts/native-source/macos
export SODIUM_DIST_DIR="$PWD/artifacts/native-source/macos"
bash scripts/build-native.sh
dotnet build NetWiv.sln -c Release
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64
tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/osx-arm64/publish/NetWiv.Desktop.Smoke
bash scripts/test-native-startup.sh "$PWD/tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/osx-arm64/publish"
```

Use `osx-x64` on Intel. The native build script selects the host architecture,
uses the pinned Cargo lock and writes `artifacts/native/<RID>`. Its default
`MACOSX_DEPLOYMENT_TARGET` is `12.0`. It sets the dylib install name to
`@rpath/libnetwiv_native.dylib` and applies an ad-hoc signature.
Stronghold uses the same signature-verified libsodium source archive as Linux;
see the [native guide](../native/netwiv-native/README.md) and dependency lock.

NuGet packing requires native assets for **all four** supported RIDs. Build them
on their matching hosts and collect them under `artifacts/native` before
`scripts/pack-integration.ps1`. Packing fails when an asset is missing; it never
substitutes another architecture. Ordinary application consumers need the matching
NuGet package, not Cargo or a Homebrew runtime library.

The complete CLI suite also exercises `netwiv signer`. Prepare the pinned 0.12
source build for the current Mac architecture (the upstream macOS binary archive
is arm64-only):

```sh
brew install cmake pkgconf libsodium
node scripts/prepare-test-tools.mjs
export NETWIV_MINISIGN_PATH="$PWD/artifacts/tools/minisign-0.12/build/minisign"
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build
```

The source checksum is recorded in `packages/cli/native-tools.json`. This helper
prepares a local test tool; it does not package npm sidecars. `codesign` for app
bundles is a separate tool.

## Application workflow

Use the existing `NetWiv.Platform.Desktop` runtime with a synchronous `Main` entry
point. AppKit must run on the process's original main thread. Do not start
`app.Run()` from `Task.Run` or after an asynchronous `Main` continuation.
The native adapter rejects an off-main-thread start with a diagnostic. Install
`NetWiv.Cli` as a .NET tool from a matching feed using the
[zsh quickstart](integration-quickstart.md#create-the-project). A source clone
contains no prebuilt consumer feed; npm launcher lookup alone does not supply
macOS binaries.

```sh
netwiv dev
netwiv build --target aarch64-apple-darwin --bundles app
netwiv bundle --target aarch64-apple-darwin --bundles dmg --no-sign
```

`netwiv build` defaults to self-contained managed .NET. Use `--aot` for NativeAOT
or `--framework-dependent` for an installed runtime; see [deployment](deployment.md).
`netwiv bundle` uses the existing
publish output for the **same target spelling**. Both `.app` and `.dmg` are
selected by `bundle.targets: "all"`. The defaults use the current Mac architecture.
With `src-netwiv/netwiv.conf.json`, output is under the application root's
`target/<requested-target>/release/bundle/macos/<safe-product-name>.app` and
`bundle/dmg/<safe-product-name>_<version>_<architecture>.dmg`. The CLI prints the
exact paths. Launch the `.app` through Finder or `open "/path/to/App.app"`;
run `Contents/MacOS/<main-binary>` directly when you need terminal diagnostics.

Platform overlays are `netwiv.macos.conf.json` (or the accepted Tauri/json5/TOML
equivalent). Capability/permission platform values retain TAURI®'s `"macOS"`
spelling. Hook variables use `TAURI_ENV_PLATFORM=darwin`,
`TAURI_ENV_FAMILY=unix` and `TAURI_ENV_ARCH=aarch64` or `x86_64`.

## Bundles and signing

The app layout contains native executables and dylibs in `Contents/MacOS`,
resources in `Contents/Resources`, and generated metadata in `Contents/Info.plist`.
`PathApi.ResourceDirectoryAsync` resolves the latter in an app bundle and the
executable directory during unbundled development. `bundle.resources` mappings,
category and a configured `.icns` icon are included. Supply an `.icns` icon;
the existing icon command does not yet generate that format.

Supported `bundle.macOS` keys are `minimumSystemVersion`, `bundleVersion`,
`signingIdentity`, `entitlements`, `infoPlist` and `appStore`. Other non-null macOS bundle options fail
explicitly. Minimum versions below `12.0` are rejected. For example:

```json
{
  "bundle": {
    "targets": ["app", "dmg"],
    "macOS": {
      "minimumSystemVersion": "12.0",
      "signingIdentity": "Developer ID Application: Your Name (TEAMID)",
      "entitlements": "entitlements.plist"
    }
  }
}
```

`APPLE_SIGNING_IDENTITY` is used when no identity is configured. Without an
identity, or with `--no-sign`, an ad-hoc signature is applied: NativeAOT's linker
signature must be renewed after patching bundle metadata on Apple Silicon.
An ad-hoc signature permits local execution; it does not establish Developer ID
trust or Gatekeeper acceptance. Libraries, executable and bundle are signed in
that order; configured identity signing enables hardened runtime and timestamping.
Managed apps also require entitlements appropriate for the .NET JIT when using
Developer ID hardened-runtime signing; only ad-hoc managed bundle execution is
covered by the automated tests. Entitlements are applied to the executable and
bundle. Signing errors fail the
command. Notarization, custom frameworks,
DMG layout customization and trusted distribution on a clean Mac remain outside
this implementation. DMGs contain the app and an `/Applications` link.

## Mac App Store packages

The explicit `app-store` bundle target creates a sandboxed NativeAOT `.app` and
a `productbuild` `.pkg`. It is a NetWiv extension, not a Tauri configuration
compatibility claim. It is never selected by `bundle.targets: "all"`, which
continues to mean `app,dmg`. Store output lives separately under
`target/<requested-target>/release/bundle/app-store/`; producing it does not
overwrite the direct-distribution app. Build on macOS with Xcode Command Line
Tools. Both existing macOS RIDs are accepted; local runtime verification covers
arm64, not Intel or the minimum supported OS.

### Configure and sign

Create an XML entitlements file relative to the main configuration directory:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
  <key>com.apple.security.app-sandbox</key><true/>
  <key>com.apple.security.network.client</key><true/>
</dict></plist>
```

Both entries are required by the Store target. In the tested WKWebView setup,
omitting network client access prevented local guest IPC from completing.
Additional sandbox permissions remain explicit application choices. NetWiv
does not grant broad file access, server networking or JIT permissions. Ordinary
NetWiv capabilities still control IPC and do not replace macOS entitlements.

Set `bundle.targets` to `["app-store"]` and configure:

```json
{
  "bundle": {
    "active": true,
    "targets": ["app-store"],
    "category": "Utility",
    "macOS": {
      "entitlements": "Entitlements.plist",
      "infoPlist": "Info.plist",
      "appStore": {
        "signingIdentity": "Apple Distribution: Example Organization (TEAMID)",
        "installerSigningIdentity": "3rd Party Mac Developer Installer: Example Organization (TEAMID)",
        "provisioningProfile": "distribution.provisionprofile"
      }
    }
  }
}
```

The app identity may also be `3rd Party Mac Developer Application: ...`.
Use a **Mac Installer Distribution** certificate for the package; its Keychain
identity starts with `3rd Party Mac Developer Installer:`. Keep the corresponding
private keys in Keychain. Developer ID identities and the ordinary
`APPLE_SIGNING_IDENTITY` variable are not used for Store signing.

Instead of storing personal signing settings in project files, supply
`APPLE_APP_STORE_SIGNING_IDENTITY`, `APPLE_APP_STORE_INSTALLER_IDENTITY` and
`APPLE_APP_STORE_PROVISIONING_PROFILE` from your private local environment.
Explicit `appStore` settings take precedence over these variables. Do not commit
personal signing overlays, certificates, private keys or provisioning profiles.
Paths are resolved relative to the main configuration directory, including paths
in an overlay supplied with `--config`.

Obtain a **Mac App Store Connect** provisioning profile for the configured
`identifier` and signing certificate. The CLI decodes it with `security cms`,
checks expiration and an exact App ID match, rejects development/device profiles,
and copies it to `Contents/embedded.provisionprofile` before signing. The profile's
application and team identifiers are added to the signing entitlements; conflicting
values in the application's entitlements are rejected. Other capabilities are
not copied from the profile automatically. Configure app-specific capabilities
in both the Apple developer account and your entitlements as required.

```sh
netwiv build --aot --target osx-arm64 --bundles app-store
# Or package existing NativeAOT output, using the same target spelling:
netwiv bundle --target osx-arm64 --bundles app-store
```

The CLI signs native dylibs, the main executable and the bundle, verifies the app
signature with `codesign --verify --deep --strict`, checks that the signing
certificate belongs to the supplied profile, then calls `productbuild
--sign` with the installer identity and checks the package signature. Store
signing does not force the hardened-runtime/timestamp options used by the
Developer ID path. Signing failures stop packaging; there is no automatic fallback
to ad-hoc signing. Managed/framework-dependent Store payloads are rejected;
the existing direct-distribution targets continue to support them.

### Additional Info.plist entries

`bundle.macOS.infoPlist` accepts an XML plist dictionary for any macOS bundle
target. It merges strings, booleans, arrays and nested dictionaries into the
generated plist, including copyright and application-specific privacy messages:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
  <key>NSHumanReadableCopyright</key><string>Copyright Example Organization</string>
  <key>NSCameraUsageDescription</key><string>Scan a document you select.</string>
</dict></plist>
```

Include privacy keys only for features your app actually uses. Determine your
own encryption declaration (`ITSAppUsesNonExemptEncryption`); the framework does
not choose its value. Malformed dictionaries and duplicate keys fail validation.
Overrides of the executable, identifier, package type, version fields, minimum
OS and icon file are rejected; use their dedicated configuration instead.

### Local verification and limits

`--no-sign` explicitly builds an ad-hoc-signed sandbox app and a package named
`<product>_<version>_<architecture>-unsigned.pkg`. It requires the same sandbox
entitlements but does not require signing identities or a profile. A supplied
profile is still validated and embedded. This mode is for local checks, not Store
submission. The ordinary signed output omits the `-unsigned` suffix.

The [NativeAOT sandbox example](../tests/NetWiv.MacOS.Sandbox.Smoke/README.md)
checks WKWebView, unchanged upstream guest IPC, local-storage persistence across
process restarts, writes through SDK data/config/cache/log paths inside the
container, and rejection of a write outside it. Functional assertions print only
test labels; there is no environment-report collection or upload.

The CLI tests exercise plist/profile failures and signature argument separation
on every host, and real ad-hoc signing, package extraction, signature verification
and execution on macOS. The macOS CI job also builds the NativeAOT sandbox example
through the CLI, extracts its package and runs both persistence phases. No test
package or per-run report is uploaded. The example has been executed with NativeAOT on
a physical arm64 Mac. Real distribution-certificate signing remains unverified
without the matching certificates and profiles. These local checks do not prove
Apple distribution validation, App Store Connect acceptance or review approval;
none of those services is contacted by this target. Security-scoped bookmarks
and persistent access to user-selected external files are not implemented by
this packaging feature. App-specific plugins must be tested under their required
sandbox permissions.

For account, certificate and submission steps, consult
[Apple's distribution signing guide](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac/)
and [Tauri's App Store guide](https://tauri.app/distribute/app-store/).

## Runtime paths and macOS behavior

The shared Core path resolver uses these macOS roots:

| API directory | Path |
| --- | --- |
| Config, data, local data | `~/Library/Application Support` |
| App config, app data, app local data | `~/Library/Application Support/<identifier>` |
| Cache / app cache | `~/Library/Caches` / `~/Library/Caches/<identifier>` |
| App log | `~/Library/Logs/<identifier>` |
| Resource directory inside `.app` | `Contents/Resources` |

Use `PathApi` or the official path guest instead of assuming a Windows/Linux
layout. Native menus use the single AppKit menu bar; the focused window's menu
takes precedence over the app menu. App show/hide and Dock visibility affect the
application. Title bars, shadows, vibrancy and simple fullscreen use the macOS
Tao/Wry backend. See [menu behavior](menu-csharp-api.md#macos-application-menus).

## Window effects

Creation-time `windowEffects` and runtime `setEffects` select the first supported
macOS material. Each supported application replaces the previous effect view;
`clearEffects` removes it, and clearing again is harmless. Empty lists and lists
of Windows-only materials preserve the existing effect. See the
[pinned behavior difference](pinned-upstream-behavior.md#window-effects-and-frame-bridge-availability)
for the Tauri 2.11.5 distinction and migration impact.

After building the native adapter, run the focused effects probe in a logged-in
desktop session with `App.Run()` on the original process main thread:

```sh
dotnet build tests/NetWiv.Desktop.Smoke -c Release
caffeinate -d -u -t 95 dotnet tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/NetWiv.Desktop.Smoke.dll --effects-smoke
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64
caffeinate -d -u -t 95 tests/NetWiv.Desktop.Smoke/bin/Release/net8.0/osx-arm64/publish/NetWiv.Desktop.Smoke --effects-smoke
```

Use `osx-x64` on Intel. The probe reads its own AppKit content-view subviews on
the UI thread and asserts one effect after replacement, none after clearing,
and the last requested native material/state. It exercises creation with and
without an implicit WKWebView, explicit child creation, radius arguments,
attached-effect close/destroy and label reuse over twelve cycles. It has a
90-second watchdog and does not retain native pointers across dispatches.
It does not establish pixel appearance, corner rendering, every material,
minimum-OS support or paired upstream conformance.
Focused managed and NativeAOT executions on physical macOS arm64 with .NET 8
passed these assertions. The unmodified adapter failed the same probe after
its first effect change with two native effect views instead of one.

The earlier intermittent `NSViewUpdateVibrancyForSubtree` SIGTRAP remains
unresolved. Preventing view accumulation addresses a demonstrated replacement
defect; passing bounded lifecycle runs does not identify or eliminate the
earlier crash. See [SDK runtime limits](dotnet-compatibility.md).

## Validation and limits

The [GitHub Actions workflow](../.github/workflows/ci.yml) includes separate
`macos-15` (arm64) and `macos-15-intel` (x64) jobs. Each builds its native adapter,
runs managed/Rust/JavaScript tests, executes the Core suite with NativeAOT,
checks native startup failures, and runs real WKWebView and isolation NativeAOT
smokes. The CLI suite includes real app/DMG packaging and signing-failure tests.
Validation results remain in ordinary CI console output; separate validation
reports and diagnostic archives are not collected or uploaded. See the
[CI guide](development.md#continuous-integration) for scope and prerequisites.
A configured job is not a passing hosted run; Intel execution remains unverified
until its job succeeds. Neither macOS 15 job establishes macOS 12 acceptance.

The NativeAOT desktop smoke exercises real WKWebView guest invoke/Channel, scoped
assets, multiple windows, native menu/tray/image operations, reparenting, async
close, document invalidation, remote-origin denial, macOS title bars/effects,
Dock/app visibility and log paths. The isolation smoke additionally exercises
encrypted iframe IPC and forged/opaque-frame rejection. The macOS CLI tests use
real `cc`, `codesign`, `plutil` and `hdiutil`, including invalid signing identity,
missing entitlements and missing bundle-marker failures.

The ad-hoc-signed app also passed actual C# invoke-button input in secondary and
child WebViews and a native menu selection/checked-state check. These checks
establish integration evidence, not complete Tauri conformance.
Broader keyboard/mouse interaction, all menu roles, multiple monitors, Developer ID certificates,
package-only clean-machine installation and Intel execution need separate acceptance.
Expansion of distribution notarization is excluded from project work scope, not
a deferred acceptance task. Existing ad-hoc/identity signing does not establish
notarized distribution. Website data-store enumeration/removal is implemented through
Wry's asynchronous WKWebsiteDataStore APIs on macOS 14 and later. Older systems
receive an explicit unsupported error before either unavailable selector is called.
The creation-time `dataStoreIdentifier` follows the pinned Wry behavior: macOS 14+
uses the identified persistent store, older systems use the default store, and
incognito WebViews use a nonpersistent store. Close every WebView using an identifier
before removing its store; an Apple removal failure is returned as a command error.
WebKit may release a closed WebView's store asynchronously, so a caller can still
receive an in-use error immediately after closing it.
Guest removal failures use the pinned Tauri string
`runtime error: failed to remove data store`. The C# SDK retains the more specific
`data store is currently opened` message for the same native failure. Availability,
validation and cancellation failures are not rewritten.
Windows/Linux retain the upstream empty-list/no-op app API behavior.
The paired guest app fixture `app.website-data-store-sdk-lifecycle` checks two
random test-owned identifiers: exact enumeration bytes, isolation, persistence
after closing, rejection while active, incognito precedence, deletion and empty
recreation. Its test-only command creates windows through each host's SDK; it
does not establish public JavaScript creation-option parity. Cleanup never
deletes ambient enumerated stores. macOS below 14 skips the pinned upstream's
unavailable identifier selectors; the NetWiv SDK smoke separately asserts its
availability errors. The paired managed/NativeAOT fixture passed on physical
macOS arm64. NetWiv's separate unchanged-guest `dataStoreIdentifier` option
regression also passed managed and NativeAOT execution. The pinned public creation option ignores the identifier; see
[the observed difference](pinned-upstream-behavior.md#website-data-store-creation-and-removal).
Intel and minimum-OS execution remain unverified.
Per-window `skipTaskbar=true` is rejected on
macOS; use `AppApi.SetDockVisibilityAsync` for the application-wide Dock entry.
The Autostart and DeepLink OS-registration plugins remain Windows/Linux-only.
Their macOS failure paths are tested and are not reported as supported features.
Opener URL/path opening and Shell `open` use `/usr/bin/open` on macOS.
Opener reveal resolves each requested path through macOS `realpath`, including
symbolic links in parent directories, then submits all canonical paths in one
`open -R` request. Missing items, broken/cyclic links and invalid paths reject the
whole request before Finder is launched.
The `external opening` Core tests check launcher arguments and rejection paths
without opening desktop applications. Local arm64 smoke checks also confirmed
browser requests from Opener URL/`.webloc` opening and Shell URL opening;
Finder selection itself still needs GUI acceptance.
Explicit application selection remains unsupported. The OS plugin now reports
macOS product versions and process architecture; consult the
[plugin limits](official-plugins.md#macos-plugin-limits) before enabling them.

Custom-protocol responses are queued to the native UI loop. A response submitted
from a managed worker must not synchronously wait for AppKit while holding the
managed runtime lifetime lock; the integration smoke covers this deadlock path.

### Reproduce the focused checks

After the framework source build, run from the repository root:

```sh
dotnet run --project tests/NetWiv.Core.Tests -c Release --no-build
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build -- --filter macOS
dotnet test tests/NetWiv.Api.Tests -c Release --no-build
dotnet test tests/NetWiv.Generators.Tests -c Release --no-build
cargo +1.95.0 test --locked --manifest-path native/netwiv-native/Cargo.toml
```

The focused CLI checks create temporary real Mach-O executables, stage resources,
create app/DMG artifacts, verify signatures/plists/images and exercise signing
failures. The Rust tests check off-main-thread rejection without creating AppKit
objects on a worker. Run the published desktop smoke with `--no-window-smoke` for
windowless app/menu/tray startup and teardown, or set
`NETWIV_SMOKE_HOLD_MS=120000` before ordinary execution for GUI inspection.
See [isolation smoke](isolation.md#macos-source-linked-smoke) and
[differential conformance](../tests/conformance/README.md#macos-runs) for their
separate workflows.

The `tests/frontend-bindings/run.mjs`, `resources.mjs` and `native-version.mjs`
harnesses still select Windows/Linux RIDs. They must be extended before being
used for macOS package-consumer validation. Do not treat a source-linked smoke
result as package-only or release acceptance.

### Website data-store check

The ordinary desktop smoke includes `MacOsDataStoreProbe` on macOS. The same
scenario runs independently with `--data-store-smoke`. On macOS 14+
it creates a fresh random 16-byte identifier, uses unchanged
`@tauri-apps/api@2.11.1` guest code to enumerate it, checks that removal while its
WebView is open fails without invalidating that WebView, closes and reopens it to
check local-storage persistence, then removes it from another WebView and checks
that recreation has no previous data. SDK enumeration checks the same identifier
bytes, and SDK cleanup deletes only the test-owned store. On macOS 12/13 the smoke
checks explicit version errors for enumeration and removal. Native unit tests
cover malformed identifiers, off-main-thread rejection and discarded completion
after shutdown. Completions use the native event queue, do not retain WebViews or
contexts, and cannot call managed code after runtime shutdown.

After rebuilding the native adapter, run both managed and NativeAOT consumers:

```sh
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64 -p:PublishAot=false -o artifacts/data-store-smoke/managed
artifacts/data-store-smoke/managed/NetWiv.Desktop.Smoke --data-store-smoke
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r osx-arm64 -o artifacts/data-store-smoke/aot
artifacts/data-store-smoke/aot/NetWiv.Desktop.Smoke --data-store-smoke
```

Cleanup retries only the known in-use error for up to five seconds after
test-owned WebViews are destroyed. Keep the display awake during the ordinary
monitor-geometry smoke; a sleeping display can report no active monitors.
For the pinned Rust toolchain's macOS 27
[Mach-O strip alignment issue](https://github.com/rust-lang/rust/issues/157750),
pass `--config 'profile.release.build-override.strip="none"'` to Cargo for
the affected native build. This disables stripping of build dependencies without
changing the committed release profile.
Intel and macOS 12/13 execution remain unverified.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
