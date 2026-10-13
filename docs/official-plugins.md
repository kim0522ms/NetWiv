# Using TAURI® plugins with NetWiv

The official JavaScript packages referenced here are maintained by the upstream
`tauri-apps` organization. The .NET implementations and templates described in
this guide belong to NetWiv and are not officially approved by that organization.

## Reliability and permission contracts

### Automatic log and window-state management

On Windows, Linux and macOS, `LogPlugin()` keeps an active log file of up to 5 MiB and three archived files by default. Configure these limits with `new LogPlugin(minimumLevel: 1, maxFileBytes: 1024 * 1024, retainedFiles: 2)`; `retainedFiles: 0` disables archives. Records that exceed the limit are truncated at a UTF-8 character boundary and marked with `[truncated]`. Existing constructors and JS calls remain available, but logs no longer grow without a limit. Applications that need permanent audit records should use a separate collection and retention system. This is NetWiv's disk-usage policy; it does not establish parity with every upstream log-rotation behavior.

`WindowStatePlugin()` automatically restores saved state for existing windows at startup and for windows created later. Its `Ready` Task completes after the initial restoration attempt; do not wait for it synchronously on the UI thread. Immediately before a window is destroyed, the Wry backend supplies its final normal bounds and state, including maximization. The plugin saves this state when the app is disposed. Use the `using var app = builder.Build(); app.Run();` lifecycle. Intercepting window closure or synchronously querying destroyed native windows would introduce deadlock risks and alter close behavior, so the plugin avoids those approaches.

Automatic management requires matching native libraries with this support. Explicit save and restore calls remain available. Applications that manage the lifecycle themselves can preserve that behavior with `new WindowStatePlugin(".window-state.json", StateFlags.All, automatic: false)`. Unselected flags are left unchanged. Corrupt state files are preserved and reported through diagnostics. Automatic saving is not guaranteed after forced termination, a crash, or shutdown before the initial path lookup completes. The verified native restoration scope is Windows x64 and Linux x64/X11; Wayland position restoration and macOS window-state save/relaunch restoration have not been verified.

### Persistence, permissions, and invocation lifetime

The following shared contracts apply on desktop targets where the corresponding plugin operation is available; consult the macOS limits below. They do not establish identical behavior across all versions of the official plugins.

- HTTP redirects retain `Authorization`, `Cookie`, and `Proxy-Authorization` headers only within the same origin: scheme, host, and port must match. Credentials are stripped on redirects to another origin even when its URL is allowed by the scope. Send a separate request when a destination needs its own credentials.
- Shell `execute` terminates the running process tree when the document reloads or closes, the app is disposed, or the caller cancels. It finishes output-reader cleanup before returning a failure. This does not undo completed external work or manage processes that deliberately detached themselves.
- SQL integer parameters are bound as SQLite signed 64-bit integers. Integer literals outside that range are rejected instead of being rounded to floating point. Decimal and exponent notation still use floating-point bindings. Pass and store larger identifiers as strings/TEXT. This cannot recover precision already lost in a JavaScript number.
- The `core:resources:close` permission alone cannot close a managed resource owned by another document. Ownership checking and closure are atomic. Existing close behavior for trusted backend calls and app-shared resources created with `Resources.Add` is preserved.
- Store saves and Upload downloads complete a temporary file in the destination directory before replacing the target. Transfer, serialization, or parsing failures do not first erase the existing file or in-memory value. This preserves data during those failures; it is not a power-loss durability guarantee for every filesystem.
- Upload guest calls require both file `path` and network `url` scopes in the command's capability. Deny rules take precedence, and redirect destinations are checked again. Permissions are not inherited from the fs or http plugins. Migrate an existing unscoped `upload:default` permission to explicit scopes, as shown below. Trusted backend calls retain their behavior. This intentional NetWiv difference defines boundaries against file disclosure and overwrites more precisely than a URL-only policy or unrestricted local file access. Applications with dynamic policies can validate access in a C# application command and then make a trusted backend call.

```json
{
  "identifier": "upload:default",
  "allow": [
    { "path": "$DOWNLOAD/**" },
    { "url": "https://downloads.example.com/**" }
  ]
}
```

- SQL `select` uses the SQLite authorizer to permit only queries and query functions. INSERT, DDL, ATTACH, and PRAGMA are rejected. Calls that previously wrote using only `select` permission must migrate to `execute` with an explicit `sql:allow-execute` permission. Provide a validated C# command for administrative queries such as PRAGMA results. This NetWiv policy enforces a read boundary through SQLite itself instead of inspecting only the first word of a SQL string; it is not a claim of complete upstream SQL parity. Cancellation of a running query reaches `sqlite3_interrupt`.
- Replacing or closing a WebView document, or shutting down the app, cancels the command's `CancellationToken`. Application commands must observe that token. Guest resources registered through `InvokeContext.AddResource` are tied to the originating document's lifetime. HTTP request and response streams, fs handles, and Store handles use this path. Cancellation does not roll back UI changes already accepted by the native event loop.
- `process.relaunch` requests shutdown, waits for `App.Run` to return, and finishes plugin and SingleInstance-lock cleanup before starting the new process. Cleanup failure prevents the new process from starting.

Regression coverage is in `tests/NetWiv.Core.Tests` and `tests/NetWiv.Cli.Tests`.
Process, SQLite, and local HTTP checks are separate from full OS and GUI validation.
See [support and validation](implementation-status.md) for the limits of certificate
signing and installed-application verification.

## Installation and registration

NetWiv applications use unchanged Tauri frontend packages and register the corresponding .NET plugins with `Builder`. NetWiv provides desktop implementations for `fs`, `dialog`, `os`, `process`, `store`, `opener`, `http`, `clipboard-manager`, `log`, `shell`, `window-state`, `websocket`, `upload`, `autostart`, `positioner`, `cli`, `sql`, `single-instance`, `deep-link`, `localhost`, `notification`, `global-shortcut`, and `stronghold`. Implementation and validation coverage vary by plugin. See the `EXT-*` rows in the [API status table](api-status.md) for verified scope and remaining features. The updater runtime is not implemented in v1.

Prepare an app and package feed using the [quickstart](integration-quickstart.md),
then install only the packages you need. Replace `$version` below with an actual
package version matching the app's SDK. Check the corresponding `EXT-*` row in the
[API status table](api-status.md) for supported official JS plugin features.

```powershell
$feed = 'C:\path\to\packages'
$version = 'PACKAGE_VERSION'
dotnet add src-netwiv/MyApp.csproj package NetWiv.Plugin.Fs --version $version --source $feed
dotnet add src-netwiv/MyApp.csproj package NetWiv.Plugin.Dialog --version $version --source $feed
```

Install the corresponding `@tauri-apps/plugin-fs` and `@tauri-apps/plugin-dialog`
packages in the frontend as well. JS and .NET packages have separate version
schemes. Reference `NetWiv.Platform.Desktop` and keep SDK and native package versions
aligned. Ordinary application restore and build operations do not require Cargo.

## Plugin-specific limitations

`NetWiv.Plugin.Os` reports `macos` for both `platform()` and `type()` on macOS,
`unix` for `family()`, `\n` for `eol()`, and an empty executable extension. Its
`version()` is the macOS product version from `/usr/bin/sw_vers -productVersion`,
formatted with three components (for example, `26.0.0`), rather than the Darwin
kernel version. A failed, empty or timed-out query returns `Unknown`; startup
waits at most five seconds for the query. `arch()` describes the executing app:
`aarch64` on arm64 and `x86_64` on x64, including x64 apps running under Rosetta.
The synchronous properties remain initialization values; `locale()` and
`hostname()` remain capability-controlled commands, with hostname excluded from
`os:default` and deny permissions taking precedence.

This correction was checked against the source of
[`@tauri-apps/plugin-os` 2.4.0, commit `a2364a5f216324439feedeb25b2db74e7b1eba90`](https://github.com/tauri-apps/plugins-workspace/tree/a2364a5f216324439feedeb25b2db74e7b1eba90/plugins/os),
whose lockfile selects `os_info` 3.10.0. The repository's core upstream lock does
not currently pin official plugin packages; this source comparison is separate
from the core compatibility profile. Windows and Linux retain their existing
.NET OS version and OS architecture values; full upstream version/architecture
parity on those platforms is unverified. Managed and NativeAOT tests cover the
seven initialization fields, locale/hostname permission and deny rules, and
version-query failure/timeout fixtures. Run the managed case with
`dotnet run --project tests/NetWiv.Core.Tests -c Release -- --filter "official OS"`;
publish with `dotnet publish tests/NetWiv.Core.Tests -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/os-aot`,
then run `artifacts/os-aot/NetWiv.Core.Tests --filter "official OS"`.
These tests use the fake desktop runtime; unchanged official guest execution
in WKWebView and Rosetta/Intel macOS execution require separate evidence.

`NetWiv.Plugin.Cli` exposes application launch arguments through `@tauri-apps/plugin-cli`'s `getMatches()`. It is separate from the `netwiv` CLI used to create and build projects. Application CLI configuration currently supports names, short options, positional arguments, required values, multiple values, allowed values, and subcommands. Unsupported clap features, including conditional constraints and help output, are explicitly rejected.

Register `NetWiv.Plugin.PersistedScope` after `FsPlugin` and `DialogPlugin`. It saves dialog-selected paths to `.persisted-scope.netwiv.json` in the app's data directory and restores them on restart. It intentionally does not read Tauri's `.persisted-scope` bincode format: sharing formats across different runtime scope structures could restore access incorrectly. Users migrating from Tauri must select their files again. Arbitrary backend scope mutations and deny patterns are not yet persisted. Automated restart checks have passed; actual GUI selection and restart remain unverified.

`NetWiv.Plugin.Sql` currently supports only SQLite through Windows `winsqlite3.dll` and the `sqlite3` native import on non-Windows hosts (Linux `libsqlite3.so`). Managed SQLite query/binding regressions also passed on the Apple Silicon host; macOS NativeAOT database packaging and clean-machine loading remain unverified. `sqlite:app.db` opens under the app configuration directory, and paths that escape that directory are rejected. The official JS `Database.load`, `execute`, `select`, and `close` calls are available. `sql:default` does not include `execute`; grant `sql:allow-execute` separately for writes. MySQL, PostgreSQL, migrations, and result conversion based on SQLite declared types remain incomplete. Results may vary with the system SQLite version.

Register `NetWiv.Plugin.SingleInstance` before other plugins and before the app starts. For example, `new SingleInstancePlugin((app, args, cwd) => { /* Forward to the existing window. */ })` receives the second launch's arguments and working directory in the first process. The shared desktop implementation uses a .NET named pipe keyed by the app identifier. This intentionally differs from upstream Tauri's Linux D-Bus service names and Snap/Flatpak service declarations. The plugin does not yet support applications that require upstream-compatible argument forwarding in those sandboxes. Separate-process automated checks have passed on Windows, Linux and the Apple Silicon host; a second launch with the actual GUI remains unverified.

Register `NetWiv.Plugin.DeepLink` after `SingleInstancePlugin` to forward second-launch URLs through the `deep-link://new-url` event and `getCurrent()`. Declare the schemes accepted from launch arguments in `plugins.deep-link.desktop`'s `schemes`. As in upstream Tauri, schemes registered dynamically through `register()` are not automatically recognized in launch arguments. Linux registration requires `xdg-mime` and `update-desktop-database`. Windows registration uses the current user's `Software\Classes`; unregistering leaves entries owned by other applications intact. Installer protocol declarations and actual OS URL activation remain unverified.

`NetWiv.Plugin.Localhost` has no JS guest package. Register `new LocalhostPlugin(9527)` and set the window URL to `http://localhost:9527/`. For frontend IPC, register the same origin with `Builder.DevelopmentOrigin("http://localhost:9527")`. The server has no authentication, so other processes on the device can read its assets. Origin and CSP integration in an actual WebView has not yet been fully verified.

`NetWiv.Plugin.Notification` connects the official JS `isPermissionGranted()`, `requestPermission()`, and `sendNotification()` calls to the desktop `Notification` bridge. Native delivery delegates to `notify-rust`, including its macOS backend. A successful command does not prove that the OS displayed a notification. Managed command-forwarding tests and native build checks have passed; actual notification display, installed-app icons, and user click behavior remain unverified.

`NetWiv.Plugin.GlobalShortcut` connects shortcut registration, removal, queries, and Channel events to native `global-hotkey`. `global-shortcut:default` grants no commands. Explicitly grant the permissions you need: `global-shortcut:allow-register`, `global-shortcut:allow-unregister`, `global-shortcut:allow-unregister-all`, and `global-shortcut:allow-is-registered`. Background key input and Wayland behavior have not yet been verified through the GUI.

`NetWiv.Plugin.Stronghold` connects Stronghold snapshot, vault, and store commands to the native IOTA engine. With `new StrongholdPlugin()`, the default salt path is the snapshot path plus `.salt`. To open an existing Tauri snapshot, provide that app's salt file with `new StrongholdPlugin(saltPath: "...")`. Saving and reloading new snapshots has passed NativeAOT integration checks on Windows, Linux and Apple Silicon macOS. Interoperability with existing Tauri snapshots and all cryptographic procedure inputs have not yet been compared.

## macOS plugin limits

Core WKWebView support does not imply that every official plugin has macOS OS integration.

| Area | Current macOS scope |
| --- | --- |
| OS information | macOS platform/type, Unix family/EOL, product version and executing-app architecture are implemented. Managed and arm64 NativeAOT payload/permission/query-fixture tests passed on a physical Apple Silicon Mac. Unchanged official guest execution in WKWebView and Rosetta/Intel execution require separate evidence. |
| External opening | Opener URL/path operations and Shell `open` use `/usr/bin/open` on macOS; Opener reveal uses `open -R` to select the item in Finder. Linux retains `xdg-open` (reveal opens the containing directory). Explicit application selection remains unsupported. |
| Autostart and DeepLink | OS registration/query/removal paths reject macOS. Parsing explicitly supplied launch URLs is separate from OS URL activation. |
| Log and persisted scope | Shared managed persistence is available. Core's macOS app log path is `~/Library/Logs/<identifier>`; managed regression checks do not establish installed-app behavior. |
| Dialog, clipboard and notification | Native dependencies compile for macOS; actual dialogs, pasteboard contents, notifications and OS permission interaction remain unverified. |
| GlobalShortcut and Positioner | Shortcut queries/native dispatch and positioning code exist. Background key input, multi-monitor positioning and tray anchoring remain unverified. |
| SQL and SingleInstance | Managed native-SQLite and separate-process checks passed on Apple Silicon; NativeAOT SQL distribution and actual duplicate GUI launches remain unverified. |
| Stronghold | New snapshot save/reload passed the Apple Silicon NativeAOT smoke. Existing Tauri snapshot interoperability is a separate gap. |
| WindowState | Native bounds/state are exposed; automatic persistence/restoration across macOS GUI relaunch is unverified. |

See [macOS](macos.md) for runtime/packaging limits and [API status](api-status.md)
for the complete extension inventory. None of these limits are resolved by
adding a capability permission.

## Select, read, and write a file

Register both plugins with the app's `Builder`:

```csharp
using NetWiv.Plugin.Dialog;
using NetWiv.Plugin.Fs;

builder.Plugin(new FsPlugin())
    .Plugin(new DialogPlugin());
```

Use the official package functions directly in the frontend:

```ts
import { open } from '@tauri-apps/plugin-dialog'
import { readTextFile, writeTextFile } from '@tauri-apps/plugin-fs'

const selected = await open({ filters: [{ name: 'Text', extensions: ['txt'] }] })
if (typeof selected === 'string') {
  const content = await readTextFile(selected)
  await writeTextFile(selected, content + '\n')
}
```

Frontend calls require capability permissions. To read and write a selected file, add `dialog:default`, `fs:allow-read-text-file`, and `fs:allow-write-text-file` to the app's capability. Paths selected by `open()` or `save()` are added to the filesystem and asset scopes for the app session. Allow other paths separately through `fs:scope` rules. Both file access and command permissions are required, and deny rules take precedence over allow rules.

HTTP requests require an explicit allowed URL scope in the capability. Clipboard `default` grants no commands; specify permissions such as `clipboard-manager:allow-read-text` as needed.
Shell execution requires a scope containing `name`, `cmd`, and `args` on `shell:allow-execute` or `shell:allow-spawn`. `sidecar` is not yet supported.

Support for official guest call formats does not establish full behavioral parity.
See the [API status table](api-status.md) for platform-specific features that remain
unimplemented or unverified.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
