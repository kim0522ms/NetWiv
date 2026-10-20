# NetWiv API implementation status

Core classifications were reconciled with the source on **2026-10-13**.
The classification checks dispatch paths, platform adapters, options and test
definitions. It does not establish full desktop acceptance or every argument/error
combination; feature-specific execution checks and limits are noted separately.
Plugin rows remain feature-group inventories, not method-by-method audits.
The plugin implementation notes and status explanations were reviewed on **2026-10-20**.
Targets: **Windows x64 / Linux x64 / macOS arm64**. Reference inventory: `@tauri-apps/api@2.11.1`.
This is a source-based feature inventory, not a declaration of complete TAURI® equivalence or release acceptance.

Applications use only [`@netwiv/api`](frontend-api.md), including plugin guests.
Direct Tauri frontend imports are no longer supported. This table inventories
the upstream compatibility surface, not the facade's public exports. Mobile-only
entries remain visible here as unsupported even though the facade excludes them.
See [Linux X11/Wayland](linux.md) for runtime support and compositor limits.

Implementation, declaration availability and validation are separate. Counts below
are derived from the individual core rows, not from CI results or a completion score.
The ten core **Not implemented** entries are mobile features excluded from NetWiv;
they are not a desktop implementation backlog. **Declaration available** entries
are types/constants, not pending functions. Plugin implementation gaps are listed
separately below and do not contribute to those core counts.

## Reading the status table

| Status | Meaning |
| --- | --- |
| Declaration available | A TypeScript type or a constant is present in the pinned upstream reference. This is not an unimplemented function: types describe values/callbacks/options, while constants supply values. The desktop facade exposes only its selected subset. Runtime support for members/options is listed separately. |
| Implemented | An execution path exists within the stated platform scope. This includes guest-only implementations and documented platform no-ops. It does not imply complete argument/error equivalence, individual comparison or GUI acceptance. |
| Unsupported (intentional) | Deliberately rejected to preserve NetWiv runtime identity; this is a documented compatibility difference, not a pending implementation or a compatibility pass. |
| Planned | Included in the support goals, with implementation or validation work remaining. This does not mean ready for use or scheduled for a specific release. |
| Partial | In the core inventory, part of the stated functional or platform scope is missing. Coarse plugin rows also retain their stated readiness limits; they are not method-level classifications. Core validation gaps are tracked separately. |
| Not implemented | The feature is unavailable, including declarations that only return empty results or ignore options. No implementation or delivery date is promised. |

- **Functions, objects, constructors, types, configuration fields and object members are distinct.** An implemented object/type does not imply every member is implemented. Options distinguish declarations from actual application.
- APIs that provide behavior on at least one target OS identify that OS. Unimplemented Apple/mobile features remain explicitly marked. A no-op on an OS unsupported by upstream is not necessarily a NetWiv-specific compatibility defect.
- Window behavior depends on the OS, window manager and WebView version. Linux child WebViews follow GTK Box layout. Read the [platform behavior notes](pinned-upstream-behavior.md) alongside this table.
- The comparison column records only the **presence** of an [API fixture mapping](../compatibility/implementation-evidence.json). Comparison results apply to the tested source, platform and version. A missing mapping does not prove a missing implementation; a mapping does not establish full compatibility.
- **Validation pending** is tracked in the behavior/limits column and the [validation backlog](#implementation-decisions-and-validation-backlog), independently of implementation status. All implemented rows retain this boundary, including rows without an individually named validation gap.
- This table does not mark per-API GUI acceptance complete. See [support and validation](implementation-status.md) for NativeAOT, execution, GUI and package acceptance boundaries.

## Desktop plugins and extensions

Mobile targets and the updater runtime remain **Not implemented and excluded from project work scope**. Expansion of distribution notarization is also excluded. These are not deferred milestones; existing signing/packaging support is separate.
The following feature groups cover official desktop plugins and NetWiv extensions outside the core npm inventory. They do not enumerate every method of each plugin. Partial implementations do not count as full compatibility.

| Feature ID | Feature | Status | Scope and alternatives |
| --- | --- | --- | --- |
| EXT-UPDATER | Update checks, downloads and installation | **Not implemented** | Excluded from project work scope; not deferred. CLI signing support does not provide an updater runtime. |
| EXT-DIALOG | Native open/save and message dialogs | **Partial** | `NetWiv.Plugin.Dialog` uses asynchronous GTK responses on Linux and rfd on Windows/macOS. Linux pending requests support caller cancellation and parent/WebView/document/app cleanup. Selected managed/NativeAOT file/folder/save, message and cancellation checks passed on Ubuntu 22.04 GNOME Wayland. See the [dialog probe](../tests/NetWiv.Dialog.Smoke/README.md) and [validation scope](implementation-status.md#wayland-validation). Windows/macOS GUI selection and cancellation, portal/compositor coverage and full upstream equivalence remain unverified. |
| EXT-FS | Frontend file and directory read/write APIs | **Partial** | Main commands, scopes and binary IPC are implemented. Desktop `watch`/`watchImmediate` use scoped, document-owned filesystem watchers with batching and cleanup. Recursive monitoring is bounded to 128 directory registrations; Windows holds directory move/delete locks until close. See [filesystem watching](filesystem-watch.md). Full file-option/metadata coverage, live guest acceptance and exact upstream event parity remain incomplete. |
| EXT-OS | OS information, hostname and locale | **Partial** | `NetWiv.Plugin.Os` reports macOS and its product version with process architecture. Windows x64 and Apple Silicon managed/NativeAOT payload and ACL tests passed. Unchanged official OS guest and minimum-macOS comparison remain unverified; see [OS information](official-plugins.md). |
| EXT-PROCESS | Exit and restart | **Planned** | Permission, exit and restart paths are implemented in `NetWiv.Plugin.Process`. Installed-app restart validation remains incomplete. |
| EXT-HTTP | Native HTTP client API | **Planned** | `NetWiv.Plugin.Http` implements official fetch IPC, binary response streaming, URL scopes and redirect checks. Proxy and unsafe TLS options, and upstream error/cookie/cancellation comparisons remain incomplete. |
| EXT-SHELL | Process execution and sidecar management APIs | **Planned** | `NetWiv.Plugin.Shell` implements scoped `execute`, `spawn`, stdin, kill and events. macOS `open` uses `/usr/bin/open` for permitted URL schemes. Sidecars, `openWith`, full encoding support and OS-specific termination-signal comparison remain incomplete. |
| EXT-OPENER | Open URLs and paths in external browsers or default apps | **Planned** | `NetWiv.Plugin.Opener` implements URL/path opening, reveal-in-folder and URL scopes. macOS uses `/usr/bin/open` for URL/path opening and `open -R` for Finder selection. Explicit apps (`with`), cross-platform GUI acceptance and upstream error equivalence remain incomplete. |
| EXT-CLIPBOARD | Clipboard text and image APIs | **Planned** | `NetWiv.Plugin.ClipboardManager` and arboard implement text, HTML, images and clearing. Actual Windows/Linux X11/Wayland/macOS clipboard roundtrips need validation. Default permissions allow no commands. |
| EXT-NOTIFICATION | OS notifications and notification permissions | **Planned** | `NetWiv.Plugin.Notification` implements the official desktop `notify` command, two permission commands, the JS `Notification` bridge and native `notify-rust` delivery. Managed/Rust automated checks passed. Actual Windows notification-center/Linux D-Bus/macOS notification display, installed-app identity and error equivalence remain unverified. Mobile channels and scheduling are out of scope. |
| EXT-GLOBAL-SHORTCUT | Global shortcuts while the app is inactive | **Planned** | `NetWiv.Plugin.GlobalShortcut` connects four official commands, native `global-hotkey` registration/removal and Channel events. C#/Rust automated checks passed. Actual inactive-app input on Windows/Linux/macOS, X11/Wayland behavior and reload races remain unverified. Default permissions allow no commands. |
| EXT-STORE | Persistent key-value storage API | **Partial** | Guest commands, JSON persistence and shared handles are implemented. Boolean/numeric autosave debounce, explicit-save cancellation, graceful disposal flushing and failed-save recovery are implemented; see [store persistence](store.md). Custom Rust serializers are unsupported; live guest acceptance and full upstream lifecycle equivalence remain unverified. |
| EXT-SQL | Frontend SQL database API | **Partial** | SQLite `load`, `execute`, `select` and `close` are implemented. Backend-registered forward migrations run transactionally on load/preload, validate applied history and roll back failures; see [SQLite migrations](sqlite.md). MySQL/PostgreSQL, declared-type result conversion and SQLx history import are unsupported. Live guest and clean-machine NativeAOT distribution acceptance remain separate from functional checks. |
| EXT-AUTOSTART | Registration for launch at OS login | **Planned** | `NetWiv.Plugin.Autostart` implements official `enable`, `disable` and `is_enabled`, the Windows user Run registry and Linux XDG autostart `.desktop` entries. Linux temporary-directory regression checks passed. macOS registration is unsupported. Actual login launch, Windows registration and upstream AppImage comparison remain incomplete. |
| EXT-SINGLE-INSTANCE | Single-instance enforcement and argument forwarding | **Planned** | `NetWiv.Plugin.SingleInstance` detects duplicates, forwards arguments/working directory and requests second-instance exit. Separate-process automated checks passed on Windows/Linux and Apple Silicon. Actual GUI duplicate-launch validation is pending. Linux uses .NET named pipes instead of upstream D-Bus and does not interoperate with Snap/Flatpak service declarations. |
| EXT-DEEP-LINK | Launch and route apps through external URIs | **Planned** | `NetWiv.Plugin.DeepLink` implements four official commands, declared-scheme launch arguments/events and user protocol registration/removal. Windows/Linux registration checks passed; macOS registration is unsupported. Actual OS URI activation, installer integration and upstream error equivalence remain incomplete. |
| EXT-CLI-PLUGIN | Application launch-argument parsing | **Planned** | `NetWiv.Plugin.Cli` implements official `cli_matches` responses and argument/subcommand/value validation. Basic flags, multiple values and subcommands passed automated checks. Advanced clap conditional constraints and help output are explicitly unsupported; upstream comparison remains incomplete. |
| EXT-LOCALHOST | Localhost asset server | **Planned** | `NetWiv.Plugin.Localhost` serves bundled assets over HTTP on a configured port, with CSP, response headers and an `onRequest` hook. GET/HEAD/shutdown checks passed. Actual WebView origin authorization, all HTTP boundaries and upstream equivalence remain unverified. |
| EXT-LOG | Frontend and backend logging | **Partial** | `NetWiv.Plugin.Log` implements the official log command, file/stdout/WebView outputs and file-size/archive-count limits. Long UTF-8 records and concurrent writes have regression coverage. Configured `ILoggerFactory` integration is implemented through the `NetWiv.Webview` category; the full Tauri target/filter/rotation policy remains incomplete. |
| EXT-PERSISTED-SCOPE | Restore file scopes after restart | **Planned** | `NetWiv.Plugin.PersistedScope` restores dialog-selected fs/asset scopes after relaunch; automated restart checks passed. Arbitrary backend scope changes, deny patterns and Tauri's bincode format are unsupported. Storage uses NetWiv JSON and is not interoperable with the upstream file. |
| EXT-POSITIONER | Position windows relative to screens or trays | **Planned** | `NetWiv.Plugin.Positioner` implements three official commands and screen/tray anchor calculations. Synthetic-monitor checks passed. Actual multi-monitor/tray GUI validation and upstream equivalence remain incomplete. |
| EXT-STRONGHOLD | Encrypted storage | **Planned** | `NetWiv.Plugin.Stronghold` forwards 11 official commands to IOTA Stronghold. Native snapshot/vault/store/procedure paths and new snapshot save/reload passed NativeAOT integration checks on Windows/Linux and Apple Silicon. The default Argon2 salt path is `snapshotPath.salt`; an existing Tauri salt path can be passed to the constructor. Existing Tauri snapshot interoperability and all procedure inputs remain unverified, so this is not marked ready for use. |
| EXT-UPLOAD | HTTP file uploads and downloads | **Planned** | `NetWiv.Plugin.Upload` implements official `upload`/`download`, POST/PUT/PATCH, progress Channels and HTTP failure handling. Local-server checks passed. Large transfers, cancellation, TLS and upstream progress equivalence remain unverified. |
| EXT-WEBSOCKET | WebSocket client | **Planned** | `NetWiv.Plugin.Websocket` implements official guest `connect`/`send`, text/binary/close and Channel reception. Local echo and reload cleanup checks passed. Ping/Pong and some buffer/frame options are explicitly unsupported; TLS and upstream comparison remain incomplete. |
| EXT-WINDOW-STATE | Window-state persistence and restoration | **Partial** | Official JS commands, JSON persistence, per-flag restoration, monitor checks, automatic startup restoration and normal-shutdown persistence are implemented. Maximized-window shutdown and restoration in a new process were verified on Windows/Linux X11. macOS save/relaunch restoration, forced-exit durability and full upstream equivalence remain unverified. Wayland absolute position restoration is outside the native baseline. |
| EXT-MAC | macOS runtime | **Partial** | WKWebView/AppKit, NativeAOT and app/DMG support; hosted integration and package-consumer checks passed on arm64, with selected physical Apple Silicon GUI checks. Minimum-OS acceptance and full conformance remain unverified. See [macOS](macos.md). |
| EXT-BLAZOR-HYBRID | Managed Razor UI hosting | **Implemented** | Optional `NetWiv.BlazorHybrid` targets Windows x64, Linux x64 and macOS arm64 and defaults to Microsoft's managed WebView services via `AddNetWivBlazorHybrid()`. Standard component and JS-invokable discovery do not require the experimental registry. Selected native rendering, command IPC, interop and lifecycle checks passed on physical Windows/macOS and Linux under WSLg on X11 and native Wayland. Rider/Visual Studio UI acceptance, Hot Reload and arbitrary component libraries remain unverified. See [Blazor Hybrid](blazor-hybrid.md) for platform and debugger validation. |
| EXT-BLAZOR-HYBRID-AOT | NativeAOT Razor UI hosting | **Experimental** | Explicit `AddNetWivBlazorHybridAot(...)` and `NetWivBlazorExperimentalAot=true` opt-in. Requires static component, generated JSON and callback registration. Focused native rendering/interop/lifecycle checks pass on Windows x64, Linux x64 under WSLg and macOS arm64, but routing, streams, custom events and broader component-library support are incomplete and upstream AOT warnings remain. Not a full Blazor AOT compatibility claim. See [AOT research](blazor-hybrid-aot-research.md). |
| EXT-MOBILE | Android and iOS runtimes | **Not implemented** | Excluded from project work scope; not deferred. |

This is an implementation inventory, not a catalog of fully validated installations.
`core.checkPermissions`, `requestPermissions` and `addPluginListener` forward commands to a target plugin; they do not provide these features automatically.

## Core public API counts

All **688 identifiers** in the [pinned declaration inventory](../compatibility/generated/api-manifest.json) are included. Root barrel re-exports are excluded; per-module re-exports in the inventory are retained. Inventory SHA-256: `31a4018f16fd798ae30c1b38268168c1d00b10d3b73aee0badf4a9f64626367b`.

**These are not independent feature counts or development completion percentages.** Window/WebviewWindow entry points for the same behavior, types and fields are counted separately.

| Module | Total | Declaration available | Implemented | Partial | Not implemented | Intentional difference | Has fixture mapping |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [app](#module-app) | 17 | 3 | 12 | 0 | 1 | 1 | 10 |
| [core](#module-core) | 28 | 4 | 23 | 0 | 0 | 1 | 15 |
| [dpi](#module-dpi) | 46 | 0 | 46 | 0 | 0 | 0 | 46 |
| [event](#module-event) | 15 | 7 | 8 | 0 | 0 | 0 | 4 |
| [image](#module-image) | 14 | 2 | 12 | 0 | 0 | 0 | 8 |
| [menu](#module-menu) | 123 | 8 | 115 | 0 | 0 | 0 | 56 |
| [mocks](#module-mocks) | 6 | 1 | 5 | 0 | 0 | 0 | 4 |
| [path](#module-path) | 34 | 1 | 33 | 0 | 0 | 0 | 33 |
| [tray](#module-tray) | 35 | 7 | 28 | 0 | 0 | 0 | 4 |
| [webview](#module-webview) | 56 | 4 | 51 | 0 | 1 | 0 | 13 |
| [webviewWindow](#module-webviewwindow) | 100 | 2 | 96 | 0 | 2 | 0 | 6 |
| [window](#module-window) | 214 | 18 | 190 | 0 | 6 | 0 | 44 |
| **Total** | **688** | **57** | **619** | **0** | **10** | **2** | **243** |

## Window-effect support

Window effects are implemented as described below. Distinguish argument validation from
the Linux no-op behavior; this does not establish visual-effect or full GUI equivalence.

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `webviewWindow.WebviewWindow.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. macOS replacement and clearing deliberately differ from pinned upstream behavior; native view lifecycle is checked separately from visual appearance. Linux validates, then follows the upstream no-op. See [effect differences](pinned-upstream-behavior.md#window-effects-and-frame-bridge-availability). | [N] | 1 mapped |
| `window.Window.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. macOS replacement and clearing deliberately differ from pinned upstream behavior; native view lifecycle is checked separately from visual appearance. Linux validates, then follows the upstream no-op. See [effect differences](pinned-upstream-behavior.md#window-effects-and-frame-bridge-availability). | [N] | 1 mapped |
| `window.WindowOptions.windowEffects` | Configuration member | **Implemented** | The pinned WindowEffectsConfig validates effects, color, state, radius and unknown fields. Invalid creation options are rejected before allocating a window. | [N] | 1 mapped |

## Implementation decisions and validation backlog

The 25 core rows previously marked Partial all have execution paths in the
reviewed platform adapters. They are classified as Implemented below, while
retaining their validation limits. This is a source classification change, not
25 new implementations or 25 new compatibility passes. No known missing portion
was identified in those 25 paths by this review; it does not rule out defects in
unexercised arguments, platform versions or interactions.

The inventory now separates **619 entries with execution paths**, **57 type or
constant declarations**, **10 unimplemented mobile entries**, and **two intentional
identity differences**. These are identifier counts, not independent features.
The 243 fixture-mapped entries overlap those categories and are not a pass count.
The ten mobile entries remain in the upstream inventory for completeness; they
are excluded from the implementation backlog.

### Existing implementation with validation still needed

| API group | Implementation classification and source | Existing checks and remaining work |
| --- | --- | --- |
| App visibility: `app.show`, `hide`, `setDockVisibility` | Implemented on macOS through the event-loop target in [app dispatch](../native/netwiv-native/src/runtime/commands.rs); no native mutation on Windows/Linux. | [Desktop smoke](../tests/NetWiv.Desktop.Smoke/Program.cs) calls these APIs. The mapped `app.desktop-platform-contract` fixture asserts non-Apple defaults; it does not validate macOS hide/show interaction. Broader focus and activation acceptance remains pending. |
| Website stores: `app.fetchDataStoreIdentifiers`, `removeDataStore`, `webview.WebviewOptions.dataStoreIdentifier` | Implemented through [WKWebsiteDataStore](../native/netwiv-native/src/macos.rs) and [WebView creation](../native/netwiv-native/src/runtime/webview.rs). Enumeration/removal require macOS 14+; creation on older systems retains Wry's default-store behavior. Windows/Linux app operations use the pinned empty/no-op behavior. | [Guest/SDK store probe](../tests/NetWiv.Desktop.Smoke/MacOsDataStoreProbe.cs) checks enumeration, persistence, in-use errors and removal/recreation. The bounded SDK-created-store comparison passed on macOS arm64 in managed/NativeAOT; the public creation option passed separately in NetWiv and deliberately differs from pinned upstream. Full-suite and minimum-OS execution remain pending. See [the storage difference](pinned-upstream-behavior.md#website-data-store-creation-and-removal). |
| macOS menu roles and credits: `menu.Submenu.setAsHelpMenuForNSApp`, `setAsWindowsMenuForNSApp`, `menu.AboutMetadata.credits` | Implemented in [resource dispatch](../native/netwiv-native/src/resources.rs) and [About metadata conversion](../native/netwiv-native/src/resources/menus.rs). | [Help Search, window-menu switching and displayed About name/version/credits](../tests/NetWiv.MenuTray.Smoke/README.md) passed with actual remote desktop input on physical macOS arm64 in managed and NativeAOT runs. Other roles and About fields remain unverified. |
| macOS tray template images: `tray.TrayIcon.setIconAsTemplate`, `setIconWithAsTemplate`, `tray.TrayIconOptions.iconAsTemplate` | Implemented by [tray-icon setters](../native/netwiv-native/src/resources.rs) and [tray creation](../native/netwiv-native/src/resources/trays.rs). | [Dedicated menu/tray probe](../tests/NetWiv.MenuTray.Smoke/README.md) passed on physical macOS arm64 in managed/NativeAOT: template/color/image replacement, left-click menu off/on, right/left actions, menu replacement and cleanup. Other input gestures/themes remain unverified. Linux requires a tray host and has different input support. |
| macOS WebView options: `acceptFirstMouse`, `allowLinkPreview`, `backgroundThrottling` | Implemented in [WebView creation](../native/netwiv-native/src/runtime/webview.rs); the latter two also appear through `window.WindowOptions`. | [Live native option checks](macos-webview-options.md) pass in managed and NativeAOT for initial/dynamic root/child WebViews, including the three macOS 14+ scheduling values. Remote desktop input confirms first-click behavior in both modes. Preview gestures, background timing and paired upstream comparison remain unverified. |
| macOS window appearance: `setBadgeLabel`, `setTitleBarStyle`, `Effects.state`, `Effects.radius`, and `hiddenTitle`, `tabbingIdentifier`, `titleBarStyle`, `trafficLightPosition` options | Implemented through [window dispatch](../native/netwiv-native/src/runtime/window.rs), [macOS adapters](../native/netwiv-native/src/macos.rs) and WebView creation where applicable. Window/WebviewWindow setters share the same paths. | Desktop smoke calls title-bar setters, badges and Sidebar vibrancy/clearing. Parsing tests check some malformed options. Full title/tab/button layout, material/state/radius appearance and paired upstream comparison remain pending. The documented intermittent AppKit SIGTRAP remains unresolved; see [runtime limits](dotnet-compatibility.md). |

The same separation applies to rows that were already Implemented. For example,
macOS `setSimpleFullscreen`, shadows and effect clearing already have adapters;
older descriptions saying they were Windows-only or out of scope were stale.
Their presence does not establish interactive acceptance. Configuration acceptance
alone is insufficient: the option must reach a runtime consumer on its stated OS.

### Unimplemented and intentionally different behavior

| Classification | APIs / scope | Implementation decision |
| --- | --- | --- |
| Unsupported (intentional) | `core.isTauri` | Detection helpers and window markers are retired; migrate to the NetWiv-only frontend entry point without runtime detection guards. |
| Not implemented; excluded from project scope | `app.onBackButtonPress` (one entry) | Android back-button listener behavior is absent. The official JS wrapper does not supply a mobile runtime. |
| Not implemented; excluded from project scope | `Window.activityName`, `Window.sceneIdentifier` and their WebviewWindow counterparts (four entries) | No native activity/scene command handlers. Do not treat successful desktop window operations as support for these mobile methods. |
| Not implemented; excluded from project scope | `WindowOptions.activityName`, `createdByActivityName`, `requestedBySceneIdentifier` (three entries); `disableInputAccessoryView` in WindowOptions/WebviewOptions (two entries) | Mobile configuration is not applied by the desktop creation paths. These five options are excluded work, not future implementation tasks. |
| Unsupported (intentional) | `app.getTauriVersion` | [Core dispatch](../src/NetWiv.Core/BuiltIn/BuiltInCommands.cs) explicitly rejects impersonating the Tauri runtime. This is not a missing version getter; use `getVersion` for app metadata. See [identity differences](implementation-status.md#runtime-identity-differences). |
| Documented difference within implemented window APIs | Default window title and macOS per-window `skipTaskbar=true` | The title is `NetWiv App`. macOS rejects the per-window taskbar operation; application-wide Dock visibility is the alternative. Keep these differences visible in comparisons instead of declaring them passes. |

### Core paths and existing test scope

This table identifies code and test definitions, not newly executed results.
Tests cover selected behavior; fixture mapping is maintained separately and can
lag behind tests added to a suite.

| Area | Execution path reviewed | Existing checks / remaining boundary |
| --- | --- | --- |
| Core IPC, Channel and resources | [Initialization](../src/NetWiv.Core/Ipc/InitializationScript.cs), [protocol IPC](../src/NetWiv.Core/Ipc/AppHandle.Protocol.cs), [Channel](../src/NetWiv.Core/Ipc/Channel.cs), [resources](../src/NetWiv.Core/Resources/ResourceTable.cs) | [Protocol tests](../tests/NetWiv.Core.Tests/ProtocolIpcTests.cs), [document lifetime tests](../tests/NetWiv.Core.Tests/IpcBoundaryLifetimeTests.cs), [ownership tests](../tests/NetWiv.Core.Tests/ResourceOwnershipTests.cs) and `core.*`/`channel.*` fixtures. Isolation child frames and forced fallback transport still lack full comparison. |
| Events and paths | [Built-in dispatch](../src/NetWiv.Core/BuiltIn/BuiltInCommands.cs), [event bus](../src/NetWiv.Core/Events/EventBus.cs), [native directories](../native/netwiv-native/src/paths.rs) | [Upstream event tests](../tests/NetWiv.Core.Tests/UpstreamEventTests.cs), [path tests](../tests/NetWiv.Core.Tests/UpstreamPathAndUrlTests.cs) and `event.*`/`path.*` fixtures. OS directory availability and full event-trigger interaction remain separate. |
| Images, menus and trays | [Native resource dispatch](../native/netwiv-native/src/resources.rs), [images](../native/netwiv-native/src/resources/images.rs), [menus](../native/netwiv-native/src/resources/menus.rs), [trays](../native/netwiv-native/src/resources/trays.rs) | [Native resource tests](../native/netwiv-native/src/resources/tests.rs), desktop smoke and `image.*`/`menu.*`/`tray.*` fixtures. Display, accelerators, popup roles and native input are not established by resource roundtrips alone. |
| Window / Webview / WebviewWindow | [Window dispatch](../native/netwiv-native/src/runtime/window.rs), [WebView dispatch](../native/netwiv-native/src/runtime/webview.rs), [platform adapters](../native/netwiv-native/src/runtime/platform.rs), [C# wrappers](../src/NetWiv.Core/Api/WindowApi.cs) | [API tests](../tests/NetWiv.Api.Tests/WindowApiTests.cs), [native option tests](../native/netwiv-native/src/runtime/tests.rs), desktop smoke and window/WebView fixtures including reparenting. OS effects, native input, multi-monitor transitions and Wayland geometry remain separate acceptance work. |
| Guest-only DPI and mocks | Unchanged official guest package pinned by [upstream lock](../compatibility/upstream-lock.json) | `dpi.public-shapes-and-conversions` and `mocks.public-functions` in the [shared suite](../tests/conformance/frontend/suite.js). C# numeric conversions have separate [DPI oracle tests](../tests/NetWiv.Core.Tests/DpiOracleTests.cs). JS helper availability does not prove physical DPI transitions. |

### SDK and tooling implementation gaps

- Referenced command aggregation is implemented in
  [CommandGenerator.FrontendReferences.cs](../src/NetWiv.Generators/CommandGenerator.FrontendReferences.cs),
  with [reference tests](../tests/NetWiv.Generators.Tests/ReferencedFrontendBindingsTests.cs)
  and a [package consumer](../tests/frontend-bindings/ReferencedCommands.cs).
  Both producer and consumer must opt in; aggregation does not recursively load
  arbitrary dependencies. This is no longer a missing feature.
- Explicitly tagged polymorphic mappings are implemented within the GEN-ADVANCED
  limits. Arbitrary converters, fallback polymorphism and unsupported metadata
  combinations remain rejected with `DTOXGEN009`. See
  [serialization limits](frontend-generator.md#serialization-and-current-boundaries).
- Interactive `info --fix` and plugin workflow generation remain explicitly
  unsupported tooling features. They do not block the desktop core API inventory.
- Plugin implementations remain a separate, coarser feature inventory above.
  Their Planned/Partial labels must not be read as an absence of code. Filesystem
  watching, Store autosave debounce and SQLite forward migrations now have
  implementations and focused functional tests. Shell sidecars and full fs
  metadata/options remain implementation work; dialog GUI selection and
  installed-app restart require validation of existing paths. A method-level plugin audit is still needed
  before scheduling a plugin as a whole.

## Complete public JavaScript/TypeScript API inventory

For method parameters and overloads, look up the same identifier in the [pinned declaration inventory](../compatibility/generated/api-manifest.json).
Union alternatives within type aliases and every enum value are not expanded into separate rows.

<a id="module-app"></a>

### app

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `app.BundleType` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.DataStoreIdentifier` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.defaultWindowIcon` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | No individual mapping |
| `app.fetchDataStoreIdentifiers` | Function | **Implemented** | macOS 14+ uses asynchronous WKWebsiteDataStore enumeration/removal; older macOS returns an explicit availability error. macOS arm64 managed/NativeAOT SDK-created-store comparisons passed with unchanged public guest APIs; pinned non-Apple empty/no-op behavior is retained. The exact in-use guest removal error matches upstream while the C# SDK retains its detailed error. Full-suite and minimum-OS checks remain pending; see [storage limits](pinned-upstream-behavior.md#website-data-store-creation-and-removal). | [A] | 1 mapped |
| `app.getBundleType` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getIdentifier` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getName` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getTauriVersion` | Function | **Unsupported (intentional)** | Rejects because NetWiv does not embed the TAURI framework. The command identifier remains accepted; no upstream or NetWiv version is substituted. See [identity differences](implementation-status.md#runtime-identity-differences). | [B] | Not a compatibility pass |
| `app.getVersion` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.hide` | Function | **Implemented** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.onBackButtonPress` | Function | **Not implemented** | Android back-button behavior is not implemented (excluded from project scope). Only the JS entry point exists. | [U] [A] | No individual mapping |
| `app.OnBackButtonPressPayload` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.removeDataStore` | Function | **Implemented** | macOS 14+ uses asynchronous WKWebsiteDataStore enumeration/removal; older macOS returns an explicit availability error. macOS arm64 managed/NativeAOT SDK-created-store comparisons passed with unchanged public guest APIs; pinned non-Apple empty/no-op behavior is retained. The exact in-use guest removal error matches upstream while the C# SDK retains its detailed error. Full-suite and minimum-OS checks remain pending; see [storage limits](pinned-upstream-behavior.md#website-data-store-creation-and-removal). | [A] | 1 mapped |
| `app.setDockVisibility` | Function | **Implemented** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.setTheme` | Function | **Implemented** | Applies the theme to all windows through the native runtime. | [N] | No individual mapping |
| `app.show` | Function | **Implemented** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.supportsMultipleWindows` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |

<a id="module-core"></a>

### core

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `core.addPluginListener` | Function | **Implemented** | Official JS wrapper is provided. The target C# plugin must register its listener/permission commands; this is not a general OS permission facility. | [U] [H] [C] | No individual mapping |
| `core.Channel` | Object | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.Channel.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.Channel.constructor` | Constructor | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.Channel.id` | Object member | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.Channel.onmessage` | Object member | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.Channel.toJSON` | Object member | **Implemented** | Official Channel object/serialization is connected to C# delivery. Document generations and callback lifetimes are managed; complete boundary equivalence requires separate validation. | [U] [C] [I] | 1 mapped |
| `core.checkPermissions` | Function | **Implemented** | Official JS wrapper is provided. The target C# plugin must register its listener/permission commands; this is not a general OS permission facility. | [U] [H] [C] | No individual mapping |
| `core.convertFileSrc` | Function | **Implemented** | File-URL conversion and asset-protocol integration. Loading requires file-scope and CSP permissions. | [I] [F] | 1 mapped |
| `core.invoke` | Function | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | 12 mapped |
| `core.InvokeArgs` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `core.InvokeOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `core.InvokeOptions.headers` | Configuration member | **Implemented** | Forwards request headers to the IPC Request. | [P] | No individual mapping |
| `core.isTauri` | Function | **Unsupported (intentional)** | Runtime identity detection is retired; neither `isTauri` nor `isNetWiv` is exported or injected. | [U] [I] | 2 mapped (historical) |
| `core.PermissionState` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `core.PluginListener` | Object | **Implemented** | Official JS API connected to the C# IPC bridge. Object availability does not imply every member is complete. | [U] [I] [P] | No individual mapping |
| `core.PluginListener.channelId` | Object member | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | No individual mapping |
| `core.PluginListener.constructor` | Constructor | **Implemented** | Official JS API connected to the C# IPC bridge for supported options; see option rows for unsupported behavior. | [U] [I] [P] | No individual mapping |
| `core.PluginListener.event` | Object member | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | No individual mapping |
| `core.PluginListener.plugin` | Object member | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | No individual mapping |
| `core.PluginListener.unregister` | Object member | **Implemented** | Official JS wrapper is provided. The target C# plugin must register its listener/permission commands; this is not a general OS permission facility. | [U] [H] [C] | No individual mapping |
| `core.requestPermissions` | Function | **Implemented** | Official JS wrapper is provided. The target C# plugin must register its listener/permission commands; this is not a general OS permission facility. | [U] [H] [C] | No individual mapping |
| `core.Resource` | Object | **Implemented** | Connects resource IDs and close operations to managed/native resource cleanup. | [U] [R] [M] | 1 mapped |
| `core.Resource.close` | Object member | **Implemented** | Connects resource IDs and close operations to managed/native resource cleanup. | [U] [R] [M] | 1 mapped |
| `core.Resource.constructor` | Constructor | **Implemented** | Connects resource IDs and close operations to managed/native resource cleanup. | [U] [R] [M] | 1 mapped |
| `core.Resource.rid` | Object member | **Implemented** | Connects resource IDs and close operations to managed/native resource cleanup. | [U] [R] [M] | 1 mapped |
| `core.SERIALIZE_TO_IPC_FN` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | 1 mapped |
| `core.transformCallback` | Function | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | 1 mapped |

<a id="module-dpi"></a>

### dpi

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `dpi.LogicalPosition` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.LogicalPosition.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.x` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalPosition.y` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.LogicalSize.height` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.LogicalSize.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.LogicalSize.width` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.PhysicalPosition.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.x` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalPosition.y` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.PhysicalSize.height` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.PhysicalSize.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.PhysicalSize.width` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.Position.position` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Position.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 2 mapped |
| `dpi.Size.size` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `dpi.Size.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |

<a id="module-event"></a>

### event

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `event.emit` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 3 mapped |
| `event.emitTo` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 1 mapped |
| `event.Event` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.Event.event` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.Event.id` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.Event.payload` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.EventCallback` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.EventName` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.EventTarget` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.listen` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 4 mapped |
| `event.once` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 1 mapped |
| `event.Options` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.Options.target` | Configuration member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.TauriEvent` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.UnlistenFn` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |

<a id="module-image"></a>

### image

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `image.Image` | Object | **Implemented** | Image decoding, RGBA and resource lifetime handling. Object availability does not imply every member is complete. | [M] [R] | No individual mapping |
| `image.Image.close` | Object member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.Image.constructor` | Constructor | **Implemented** | Image decoding, RGBA and resource lifetime handling for supported options; see option rows for unsupported behavior. | [M] [R] | No individual mapping |
| `image.Image.rgba` | Object member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 2 mapped |
| `image.Image.rid` | Object member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | No individual mapping |
| `image.Image.size` | Object member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 2 mapped |
| `image.Image.static.fromBytes` | Static member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 2 mapped |
| `image.Image.static.fromPath` | Static member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 2 mapped |
| `image.Image.static.new` | Static member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.ImageSize` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `image.ImageSize.height` | Data member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.ImageSize.width` | Data member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.MenuIcon` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `image.transformImage` | Function | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | No individual mapping |

<a id="module-menu"></a>

### menu

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `menu.AboutMetadata` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.AboutMetadata.authors` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.AboutMetadata.comments` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.copyright` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.credits` | Configuration member | **Implemented** | Credits display passed physical macOS arm64 managed/NativeAOT interaction checks; other OS versions remain unverified. | [M] [U] | No individual mapping |
| `menu.AboutMetadata.icon` | Configuration member | **Implemented** | Decodes the image and forwards it to muda's About dialog on Linux/macOS; unsupported on Windows. Visual acceptance remains separate. | [M] [U] | 1 mapped |
| `menu.AboutMetadata.license` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.name` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.AboutMetadata.shortVersion` | Configuration member | **Implemented** | Forwarded to muda. Displayed in parentheses after the version on Windows/Linux. | [M] [U] | No individual mapping |
| `menu.AboutMetadata.version` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.AboutMetadata.website` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.websiteLabel` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItem` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.CheckMenuItem.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItem.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.CheckMenuItem.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItem.isChecked` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.CheckMenuItem.isEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItem.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItem.setAccelerator` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.CheckMenuItem.setChecked` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.CheckMenuItem.setEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItem.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItem.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItem.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.CheckMenuItemOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.CheckMenuItemOptions.accelerator` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItemOptions.action` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItemOptions.checked` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItemOptions.enabled` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItemOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.CheckMenuItemOptions.text` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItem` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.IconMenuItem.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.IconMenuItem.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItem.isEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItem.setAccelerator` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.setEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.setIcon` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItem.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.IconMenuItemOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.IconMenuItemOptions.accelerator` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItemOptions.action` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItemOptions.enabled` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItemOptions.icon` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItemOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.IconMenuItemOptions.text` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.itemFromKind` | Function | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Menu` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.Menu.append` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.Menu.get` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.Menu.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Menu.insert` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.items` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.Menu.popup` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Menu.prepend` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.remove` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.removeAt` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Menu.setAsAppMenu` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Menu.setAsWindowMenu` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.Menu.static.default` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Menu.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 3 mapped |
| `menu.MenuItem` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.MenuItem.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.MenuItem.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.MenuItem.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItem.isEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItem.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItem.setAccelerator` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItem.setEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItem.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItem.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItem.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.MenuItemOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.MenuItemOptions.accelerator` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.action` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.enabled` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.text` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.MenuOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuOptions.items` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.NativeIcon` | Constant | **Declaration available** | Official enum declaration; its values are forwarded to muda for macOS system icons. Visual appearance remains unverified. | [U] [M] | No individual mapping |
| `menu.PredefinedMenuItem` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.PredefinedMenuItem.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.PredefinedMenuItem.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.PredefinedMenuItem.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.PredefinedMenuItemOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.PredefinedMenuItemOptions.item` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItemOptions.text` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.Submenu.append` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.Submenu.get` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.insert` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.isEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.items` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.popup` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.prepend` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.remove` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.removeAt` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.setAsHelpMenuForNSApp` | Object member | **Implemented** | Forwarded to muda on macOS; Help Search and window switching passed physical macOS arm64 managed/NativeAOT interaction checks. Other platforms/versions remain unverified. | [M] | No individual mapping |
| `menu.Submenu.setAsWindowsMenuForNSApp` | Object member | **Implemented** | Forwarded to muda on macOS; Help Search and window switching passed physical macOS arm64 managed/NativeAOT interaction checks. Other platforms/versions remain unverified. | [M] | No individual mapping |
| `menu.Submenu.setEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.setIcon` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.SubmenuOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |

<a id="module-mocks"></a>

### mocks

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `mocks.clearMocks` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.mockConvertFileSrc` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.mockIPC` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.MockIPCOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `mocks.MockIPCOptions.shouldMockEvents` | Configuration member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `mocks.mockWindows` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |

<a id="module-path"></a>

### path

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `path.appCacheDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.appConfigDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.appDataDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.appLocalDataDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.appLogDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.audioDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.BaseDirectory` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `path.basename` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.cacheDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.configDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.dataDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.delimiter` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.desktopDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.dirname` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.documentDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.downloadDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.executableDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.extname` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.fontDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.homeDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 2 mapped |
| `path.isAbsolute` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 2 mapped |
| `path.join` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.localDataDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.normalize` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.pictureDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.publicDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.resolve` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.resolveResource` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.resourceDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.runtimeDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.sep` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.tempDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.templateDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |
| `path.videoDir` | Function | **Implemented** | Path operations and OS directory queries. Missing directories may produce errors. | [B] [D] | 1 mapped |

<a id="module-tray"></a>

### tray

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `tray.MouseButton` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.MouseButtonState` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIcon` | Object | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `tray.TrayIcon.close` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.constructor` | Constructor | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. See option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `tray.TrayIcon.id` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.rid` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setIcon` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setIconAsTemplate` | Object member | **Implemented** | Template/color/image replacement passed physical macOS arm64 managed/NativeAOT interaction checks; other themes and OS versions remain unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setIconWithAsTemplate` | Object member | **Implemented** | Template/color/image replacement passed physical macOS arm64 managed/NativeAOT interaction checks; other themes and OS versions remain unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setMenu` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setMenuOnLeftClick` | Object member | **Implemented** | Forwards left-click menu behavior to tray-icon on Windows/macOS. Unsupported on Linux. menuOnLeftClick is a deprecated alias. Left-click menu off/on and native actions passed macOS arm64 managed/NativeAOT GUI checks; Windows input remains separately unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setShowMenuOnLeftClick` | Object member | **Implemented** | Forwards left-click menu behavior to tray-icon on Windows/macOS. Unsupported on Linux. menuOnLeftClick is a deprecated alias. Left-click menu off/on and native actions passed macOS arm64 managed/NativeAOT GUI checks; Windows input remains separately unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setTempDirPath` | Object member | **Implemented** | Configures the temporary icon path on Linux. No effect on Windows. | [M] | No individual mapping |
| `tray.TrayIcon.setTitle` | Object member | **Implemented** | Forwards tray titles on macOS/Linux; Linux requires icon/host support. Unsupported on Windows; native display requires GUI validation. | [M] | No individual mapping |
| `tray.TrayIcon.setTooltip` | Object member | **Implemented** | Forwards tooltips to tray-icon on Windows/macOS. Unsupported on Linux; native display requires GUI validation. | [M] | No individual mapping |
| `tray.TrayIcon.setVisible` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.getById` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.new` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.removeById` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIconClickEvent` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEvent` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEventBase` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEventType` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconOptions.action` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.icon` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.iconAsTemplate` | Configuration member | **Implemented** | Template/color/image replacement passed physical macOS arm64 managed/NativeAOT interaction checks; other themes and OS versions remain unverified. | [M] | No individual mapping |
| `tray.TrayIconOptions.id` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.menu` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.menuOnLeftClick` | Configuration member | **Implemented** | Forwards left-click menu behavior to tray-icon on Windows/macOS. Unsupported on Linux. menuOnLeftClick is a deprecated alias. Left-click menu off/on and native actions passed macOS arm64 managed/NativeAOT GUI checks; Windows input remains separately unverified. | [M] | No individual mapping |
| `tray.TrayIconOptions.showMenuOnLeftClick` | Configuration member | **Implemented** | Forwards left-click menu behavior to tray-icon on Windows/macOS. Unsupported on Linux. menuOnLeftClick is a deprecated alias. Left-click menu off/on and native actions passed macOS arm64 managed/NativeAOT GUI checks; Windows input remains separately unverified. | [M] | No individual mapping |
| `tray.TrayIconOptions.tempDirPath` | Configuration member | **Implemented** | Configures the temporary icon path on Linux. No effect on Windows. | [M] | No individual mapping |
| `tray.TrayIconOptions.title` | Configuration member | **Implemented** | Forwards tray titles on macOS/Linux; Linux requires icon/host support. Unsupported on Windows; native display requires GUI validation. | [M] | No individual mapping |
| `tray.TrayIconOptions.tooltip` | Configuration member | **Implemented** | Forwards tooltips to tray-icon on Windows/macOS. Unsupported on Linux; native display requires GUI validation. | [M] | No individual mapping |

<a id="module-webview"></a>

### webview

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `webview.Color` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.DragDropEvent` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.getAllWebviews` | Function | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 2 mapped |
| `webview.getCurrentWebview` | Function | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview` | Object | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `webview.Webview.clearAllBrowsingData` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.close` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.constructor` | Constructor | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply; see option rows for unsupported behavior. | [N] [I] | 2 mapped |
| `webview.Webview.emit` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.emitTo` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.hide` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.label` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.listen` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.listeners` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.once` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.onDragDropEvent` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.position` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 2 mapped |
| `webview.Webview.reparent` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 2 mapped |
| `webview.Webview.setAutoResize` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.setBackgroundColor` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.setFocus` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.setPosition` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.setSize` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.setZoom` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.show` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.size` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 3 mapped |
| `webview.Webview.static.getAll` | Static member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.static.getByLabel` | Static member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.Webview.static.getCurrent` | Static member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.Webview.window` | Object member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewLabel` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.WebviewOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.WebviewOptions.acceptFirstMouse` | Configuration member | **Implemented** | Forwarded to Wry on macOS; managed/NativeAOT remote desktop input confirms activation-click behavior for both values. See [validation limits](macos-webview-options.md). The option has no effect on Windows/Linux. | [N] [U] | No individual mapping |
| `webview.WebviewOptions.allowLinkPreview` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.backgroundColor` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.backgroundThrottling` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.dataDirectory` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.dataStoreIdentifier` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. NetWiv-only managed/NativeAOT public constructor checks cover isolated persistence, incognito precedence, deletion errors and empty recreation. Pinned Tauri drops this public creation option; SDK-created-store comparison is separate. The minimum OS remains unverified; see [the deliberate difference](pinned-upstream-behavior.md#website-data-store-creation-and-removal). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.devtools` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Mobile input-accessory option (excluded from project scope). Not applied during native creation. | [N] [U] | No individual mapping |
| `webview.WebviewOptions.dragDropEnabled` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.focus` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.generalAutofillEnabled` | Configuration member | **Implemented** | Configures WebView2 autocomplete on Windows. No-op on Linux. | [N] | No individual mapping |
| `webview.WebviewOptions.height` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.incognito` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.javascriptDisabled` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.proxyUrl` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.scrollBarStyle` | Configuration member | **Implemented** | Configures WebView2 fluentOverlay on Windows. Unsupported on Linux. | [N] | No individual mapping |
| `webview.WebviewOptions.transparent` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.url` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.useHttpsScheme` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.userAgent` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | 1 mapped |
| `webview.WebviewOptions.width` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.x` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.y` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.zoomHotkeysEnabled` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |

<a id="module-webviewwindow"></a>

### webviewWindow

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `webviewWindow.Color` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webviewWindow.DragDropEvent` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webviewWindow.getAllWebviewWindows` | Function | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | 1 mapped |
| `webviewWindow.getCurrentWebviewWindow` | Function | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow` | Object | **Implemented** | Combined window and WebView object. Individual member/option limits apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (excluded from project scope). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.center` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.clearAllBrowsingData` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.clearEffects` | Object member | **Implemented** | Clears Windows effects and macOS vibrancy. Linux validates the request and performs no native mutation. Visual acceptance remains separate. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.close` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | 1 mapped |
| `webviewWindow.WebviewWindow.constructor` | Constructor | **Implemented** | Combined window and WebView object. Individual member/option limits apply; see option rows for unsupported behavior. | [N] [I] | 5 mapped |
| `webviewWindow.WebviewWindow.destroy` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.emit` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.emitTo` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.hide` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.innerPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.innerSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isAlwaysOnTop` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isClosable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isDecorated` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isEnabled` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isFocused` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isFullscreen` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isMaximizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isMaximized` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isMinimizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isMinimized` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isResizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.isVisible` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.label` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.listen` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.listeners` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.maximize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.minimize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.once` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | 1 mapped |
| `webviewWindow.WebviewWindow.onCloseRequested` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.onDragDropEvent` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.onFocusChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.onMoved` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.onResized` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.onScaleChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.onThemeChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `webviewWindow.WebviewWindow.outerPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.outerSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.position` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.reparent` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.requestUserAttention` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.scaleFactor` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (excluded from project scope). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.setAlwaysOnBottom` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setAlwaysOnTop` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setAutoResize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setBackgroundColor` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setBadgeCount` | Object member | **Implemented** | Uses the macOS Dock badge label or Linux desktop badge integration. Linux requires host support. No badge mutation on Windows; use setOverlayIcon instead. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setBadgeLabel` | Object member | **Implemented** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setClosable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setContentProtected` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorGrab` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorIcon` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorVisible` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setDecorations` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. macOS replacement and clearing deliberately differ from pinned upstream behavior; native view lifecycle is checked separately from visual appearance. Linux validates, then follows the upstream no-op. See [effect differences](pinned-upstream-behavior.md#window-effects-and-frame-bridge-availability). | [N] | 1 mapped |
| `webviewWindow.WebviewWindow.setEnabled` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setFocus` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setFocusable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setFullscreen` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setIcon` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setIgnoreCursorEvents` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setMaximizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setMaxSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setMinimizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setMinSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setOverlayIcon` | Object member | **Implemented** | Windows overlay-icon integration. Linux and macOS return an explicit unsupported error. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setProgressBar` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setResizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setShadow` | Object member | **Implemented** | Applies the Windows undecorated shadow or macOS shadow setter. No native mutation on Linux; visual appearance remains platform-dependent. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setSimpleFullscreen` | Object member | **Implemented** | Calls Tao simple fullscreen on macOS; Windows/Linux use ordinary borderless fullscreen. macOS simple-fullscreen interaction remains unverified. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.setSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setSizeConstraints` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setSkipTaskbar` | Object member | **Implemented** | Windows/Linux use the native taskbar setting. macOS rejects true for an individual window; use application-wide Dock visibility. See [macOS limits](macos.md). | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTheme` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTitle` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTitleBarStyle` | Object member | **Implemented** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setVisibleOnAllWorkspaces` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setZoom` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.show` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.size` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.startDragging` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.startResizeDragging` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.static.getAll` | Static member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.static.getByLabel` | Static member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | 1 mapped |
| `webviewWindow.WebviewWindow.static.getCurrent` | Static member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.theme` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.title` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.toggleMaximize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.unmaximize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.unminimize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.window` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |

<a id="module-window"></a>

### window

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `window.availableMonitors` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.BackgroundThrottlingPolicy` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.CloseRequestedEvent` | Object | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.constructor` | Constructor | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply; see option rows for unsupported behavior. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.event` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.id` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.isPreventDefault` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.preventDefault` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Color` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.currentMonitor` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CursorIcon` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.cursorPosition` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.DragDropEvent` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effect` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effects` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effects.color` | Configuration member | **Implemented** | Validates the pinned color representation and applies it to Windows blur/acrylic. macOS uses material/state/radius; Linux has no native effect mutation. | [N] | No individual mapping |
| `window.Effects.effects` | Configuration member | **Implemented** | Validates the effect array and selects a supported Windows effect or macOS vibrancy material. Linux validates without applying an effect; full visual equivalence remains unverified. | [N] | No individual mapping |
| `window.Effects.radius` | Configuration member | **Implemented** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.Effects.state` | Configuration member | **Implemented** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.EffectState` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.getAllWindows` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.getCurrentWindow` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.LogicalPosition` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.x` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalPosition.y` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.height` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.toPhysical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.LogicalSize.width` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.Monitor` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Monitor.name` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Monitor.position` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Monitor.scaleFactor` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Monitor.size` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Monitor.workArea` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.monitorFromPoint` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.PhysicalPosition` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.x` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalPosition.y` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize` | Object | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.__TAURI_TO_IPC_KEY__` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.constructor` | Constructor | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.height` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.toJSON` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.toLogical` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.type` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.PhysicalSize.width` | Object member | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | No individual mapping |
| `window.primaryMonitor` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ProgressBarState` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ProgressBarState.progress` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ProgressBarState.status` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ProgressBarStatus` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ScaleFactorChanged` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ScaleFactorChanged.scaleFactor` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ScaleFactorChanged.size` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ScrollBarStyle` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Theme` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.TitleBarStyle` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.UserAttentionType` | Constant | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Window` | Object | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `window.Window.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (excluded from project scope). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.center` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.clearEffects` | Object member | **Implemented** | Clears Windows effects and macOS vibrancy. Linux validates the request and performs no native mutation. Visual acceptance remains separate. | [N] | No individual mapping |
| `window.Window.close` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.constructor` | Constructor | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply; see option rows for unsupported behavior. | [N] [I] | 1 mapped |
| `window.Window.destroy` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.Window.emit` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.emitTo` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.hide` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.innerPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.innerSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isAlwaysOnTop` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isClosable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isDecorated` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isEnabled` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isFocused` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.isFullscreen` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isMaximizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isMaximized` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isMinimizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isMinimized` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.isResizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.isVisible` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.Window.label` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.listen` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.listeners` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.maximize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.minimize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.once` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.onCloseRequested` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `window.Window.onDragDropEvent` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.onFocusChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `window.Window.onMoved` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `window.Window.onResized` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | 1 mapped |
| `window.Window.onScaleChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `window.Window.onThemeChanged` | Object member | **Implemented** | Official event subscriptions connected to native event conversion. OS-specific event triggers and full GUI validation are separate. | [N] [I] [E] | No individual mapping |
| `window.Window.outerPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.outerSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.requestUserAttention` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.scaleFactor` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (excluded from project scope). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.setAlwaysOnBottom` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setAlwaysOnTop` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setBackgroundColor` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setBadgeCount` | Object member | **Implemented** | Uses the macOS Dock badge label or Linux desktop badge integration. Linux requires host support. No badge mutation on Windows; use setOverlayIcon instead. | [N] | 1 mapped |
| `window.Window.setBadgeLabel` | Object member | **Implemented** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.Window.setClosable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setContentProtected` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorGrab` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorIcon` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorVisible` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setDecorations` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. macOS replacement and clearing deliberately differ from pinned upstream behavior; native view lifecycle is checked separately from visual appearance. Linux validates, then follows the upstream no-op. See [effect differences](pinned-upstream-behavior.md#window-effects-and-frame-bridge-availability). | [N] | 1 mapped |
| `window.Window.setEnabled` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setFocus` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setFocusable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setFullscreen` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setIcon` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setIgnoreCursorEvents` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setMaximizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setMaxSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setMinimizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setMinSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setOverlayIcon` | Object member | **Implemented** | Windows overlay-icon integration. Linux and macOS return an explicit unsupported error. | [N] | No individual mapping |
| `window.Window.setPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setProgressBar` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setResizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setShadow` | Object member | **Implemented** | Applies the Windows undecorated shadow or macOS shadow setter. No native mutation on Linux; visual appearance remains platform-dependent. | [N] | No individual mapping |
| `window.Window.setSimpleFullscreen` | Object member | **Implemented** | Calls Tao simple fullscreen on macOS; Windows/Linux use ordinary borderless fullscreen. macOS simple-fullscreen interaction remains unverified. | [N] [U] | No individual mapping |
| `window.Window.setSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.Window.setSizeConstraints` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setSkipTaskbar` | Object member | **Implemented** | Windows/Linux use the native taskbar setting. macOS rejects true for an individual window; use application-wide Dock visibility. See [macOS limits](macos.md). | [N] [I] | No individual mapping |
| `window.Window.setTheme` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setTitle` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setTitleBarStyle` | Object member | **Implemented** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.Window.setVisibleOnAllWorkspaces` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.show` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.Window.startDragging` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.startResizeDragging` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.static.getAll` | Static member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.static.getByLabel` | Static member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.static.getCurrent` | Static member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.static.getFocusedWindow` | Static member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.theme` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.title` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 3 mapped |
| `window.Window.toggleMaximize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.unmaximize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.unminimize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowLabel` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.WindowOptions` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.WindowOptions.activityName` | Configuration member | **Not implemented** | Mobile activity/scene or input-accessory creation option (excluded from project scope). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.allowLinkPreview` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.alwaysOnBottom` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.alwaysOnTop` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.backgroundColor` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.backgroundThrottling` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.center` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.closable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.contentProtected` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.createdByActivityName` | Configuration member | **Not implemented** | Mobile activity/scene or input-accessory creation option (excluded from project scope). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.decorations` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Mobile activity/scene or input-accessory creation option (excluded from project scope). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.focus` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.focusable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.fullscreen` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.height` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.hiddenTitle` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.javascriptDisabled` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.maxHeight` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.maximizable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.maximized` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.maxWidth` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.minHeight` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.minimizable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.minWidth` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.parent` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.preventOverflow` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.requestedBySceneIdentifier` | Configuration member | **Not implemented** | Mobile activity/scene or input-accessory creation option (excluded from project scope). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.resizable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.scrollBarStyle` | Configuration member | **Implemented** | Configures WebView2 fluentOverlay on Windows. Unsupported on Linux. | [N] | No individual mapping |
| `window.WindowOptions.shadow` | Configuration member | **Implemented** | Applies the Windows undecorated shadow or macOS shadow setter. No native mutation on Linux; visual appearance remains platform-dependent. | [N] | No individual mapping |
| `window.WindowOptions.skipTaskbar` | Configuration member | **Implemented** | Windows/Linux use the native taskbar setting. macOS rejects true for an individual window; use application-wide Dock visibility. See [macOS limits](macos.md). | [N] [I] | No individual mapping |
| `window.WindowOptions.tabbingIdentifier` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.theme` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.title` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.titleBarStyle` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.trafficLightPosition` | Configuration member | **Implemented** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.transparent` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.visible` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.WindowOptions.visibleOnAllWorkspaces` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.width` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.windowEffects` | Configuration member | **Implemented** | The pinned WindowEffectsConfig validates effects, color, state, radius and unknown fields. Invalid creation options are rejected before allocating a window. | [N] | 1 mapped |
| `window.WindowOptions.x` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.y` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowSizeConstraints` | Type | **Declaration available** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.WindowSizeConstraints.maxHeight` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowSizeConstraints.maxWidth` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowSizeConstraints.minHeight` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowSizeConstraints.minWidth` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |

## C# SDK, security and tooling

The following table groups .NET functionality by **feature**. It is not a one-to-one inventory of Rust crates or all public C# members, and it is excluded from the 688-identifier count above. See the [C# contract](csharp-api.md) for naming and ownership rules.

| Feature ID | API / feature | Status | Available scope and remaining work | Evidence |
| --- | --- | --- | --- | --- |
| SDK-APP | Builder / App / AppHandle / Run / Dispose | **Implemented** | Configuration, startup, UI loop, shutdown and resource ownership. Windows entry points require STA; macOS Run must stay on the process main thread. | [Builder](../src/NetWiv.Core/Builder.cs), [App](../src/NetWiv.Core/App.cs) |
| SDK-STATE | Manage / State / TryState / Unmanage | **Implemented** | Type-keyed state, ownership transfer and cleanup. CLR references and IDisposable replace Rust borrow lifetimes. | [State](../src/NetWiv.Core/State/State.cs), [contract](csharp-api.md) |
| SDK-COMMAND | Command / InvokeHandler / Task·ValueTask | **Implemented** | Static command registration, injection and generated JSON serialization. | [Generator](../src/NetWiv.Generators/CommandGenerator.cs) |
| SDK-EVENT | Listen / Once / ListenAny / Emit / EmitTo / Unlisten | **Implemented** | App/window/WebView-targeted events, reentrant once listeners and unsubscription. | [ManagerEvents](../src/NetWiv.Core/Events/ManagerEvents.cs) |
| SDK-WINDOW | Window / Webview / WebviewWindow / builders | **Implemented** | Multiple windows/WebViews, lookup and reparenting. Platform/effect API limits are shared with the table above. | [Window API](../src/NetWiv.Core/Api/WindowApi.cs), [WebView API](../src/NetWiv.Core/Api/WebviewApi.cs) |
| SDK-RESOURCE | Channel / Resource / Image / Menu / TrayIcon | **Implemented** | SDK wrappers, callbacks and resource cleanup. See the table above for individual unsupported features. | [Channel](../src/NetWiv.Core/Ipc/Channel.cs), [resources](../src/NetWiv.Core/Resources/ResourceTable.cs), [API](csharp-api.md) |
| SDK-PLUGIN | IPlugin / PluginContext / static plugin registration | **Implemented** | NuGet plugin commands, configuration, permissions and lifecycle. Separate from official plugin implementations. | [Host](../src/NetWiv.Core/Plugins/Plugin.cs) |
| SDK-ASSET | Embedded assets / RegisterUriSchemeProtocol / asynchronous protocols | **Implemented** | Embedded frontend, custom URI handlers and response buffers. | [Builder](../src/NetWiv.Core/Builder.cs), [assets](../src/NetWiv.Core/Assets/EmbeddedAssets.cs) |
| SDK-ACL | Capabilities / allow and deny / app manifest policy | **Implemented** | Local/remote defaults, manifest-dependent policy and deny precedence. Equivalence across all input combinations is separate. | [ACL](../src/NetWiv.Core/Security/Acl.cs) |
| SDK-SCOPE | FileScope / AllowFile·Directory / ForbidFile·Directory | **Implemented** | Dynamic file scopes, path boundaries and deny precedence. Review of all upstream edge cases remains incomplete. | [FileScope](../src/NetWiv.Core/Security/FileScope.cs), [mutations](../src/NetWiv.Core/Security/FileScope.Mutations.cs) |
| SDK-CSP | CSP / isolation / document-generation IPC | **Implemented** | Security enforcement and isolation transport exist. This does not establish full security acceptance. | [CSP](../src/NetWiv.Core/Security/ContentSecurityPolicy.cs), [isolation](../src/NetWiv.Core/Security/IsolationPattern.cs), [IPC](../src/NetWiv.Core/Ipc/AppHandle.Protocol.cs) |
| SDK-REMOTE-PARITY | Detailed remote URLPattern and iframe-origin equivalence | **Implemented** | Compared 363 URLPatterns and 3364 URL decisions with upstream. Standard IPC comparisons cover per-frame Windows origin checks and the Linux main-frame bridge. Isolation child frames and forced fallback are outside this scope and listed in knownGaps. | [URLPattern](../src/NetWiv.Core/Security/RemoteUrlPattern.cs), [known gaps](../compatibility/implementation-evidence.json) |
| GEN-BINDINGS | Generated JavaScript functions and TypeScript DTOs | **Implemented** | C#-named input/output DTOs, name disambiguation, generics, build integration and opted-in referenced command aggregation. Missing/version-incompatible producer metadata produces DTOXGEN013; JSON mappings retain the separate GEN-ADVANCED limits. | [Usage](frontend-generator.md), [Emitter](../src/NetWiv.Generators/FrontendBindingEmitter.cs) |
| GEN-SERIALIZATION | JSON names, enums, nulls, directional ignores and supported ExtensionData | **Implemented** | Generates only supported DTO shapes. Does not guess types or silently use any. | [Supported scope](frontend-generator.md) |
| GEN-ADVANCED | Polymorphic JSON mappings and advanced serialization | **Partial** | Explicit JsonPolymorphic abstract/interface roots with uniquely tagged sealed JsonDerivedType variants produce directional TS unions and source-generated command JSON. String/integer tags, nullable and recursive variants are supported. The discriminator must be the first JSON property. Fallback, concrete roots, nonsealed or nested declarations, arbitrary converters, unsupported ExtensionData and nonpublic JsonInclude remain rejected with DTOXGEN009. | [Limits](frontend-generator.md), [Emitter](../src/NetWiv.Generators/FrontendBindingEmitter.cs) |
| CLI-NEW | netwiv new / React template | **Implemented** | NuGet consumer project, CLI manifest and pinned versions. | [NewCommand](../src/NetWiv.Cli/Commands/NewCommand.cs), [usage](project-creation.md) |
| CLI-INIT | netwiv init | **Implemented** | Initializes configuration and .NET integration for an existing project. | [InitCommand](../src/NetWiv.Cli/Commands/InitCommand.cs) |
| CLI-DEV | netwiv dev / managed and AOT / watching / restart / hooks | **Implemented** | Development execution and child-process cleanup. Does not claim equivalence for every upstream option. | [DevCommand](../src/NetWiv.Cli/Commands/DevCommand.cs), [development workflow](react-development.md) |
| CLI-BUILD | netwiv build / managed or NativeAOT publish | **Implemented** | Publishes self-contained .NET by default, framework-dependent with `--framework-dependent`, or NativeAOT with `--aot`; includes frontend output, configuration and embedded assets. See [deployment](deployment.md). | [BuildCommand](../src/NetWiv.Cli/Commands/BuildCommand.cs) |
| CLI-BINDINGS | netwiv bindings / automatic MSBuild generation | **Implemented** | Exports command metadata to JS/TS without executing application code. | [BindingsCommand](../src/NetWiv.Cli/Commands/BindingsCommand.cs) |
| CLI-CONFIG | JSON, JSON5 and TOML / discovery, merging and validation | **Implemented** | netwiv/tauri inputs, platform overlays and ambiguity errors. | [ConfigLoader](../src/NetWiv.Build/Configuration/ConfigLoader.cs) |
| CLI-INFO | netwiv info | **Implemented** | Environment diagnostics. Automatic fixes are listed separately below. | [InfoCommand](../src/NetWiv.Cli/Commands/InfoCommand.cs) |
| CLI-INFO-FIX | Interactive automatic fixes in info | **Not implemented** | Rejected with NotSupportedException. | [InfoCommand](../src/NetWiv.Cli/Commands/InfoCommand.cs) |
| CLI-ICON | netwiv icon | **Implemented** | Icon generation command. | [IconCommand](../src/NetWiv.Cli/Commands/IconCommand.cs) |
| CLI-COMPLETIONS | netwiv completions | **Implemented** | Shell completion generation. | [CompletionsCommand](../src/NetWiv.Cli/Commands/CompletionsCommand.cs) |
| CLI-ACL | netwiv permission / capability / inspect | **Implemented** | Permission/capability authoring and checks. | [PermissionCommand](../src/NetWiv.Cli/Commands/PermissionCommand.cs), [CapabilityCommand](../src/NetWiv.Cli/Commands/CapabilityCommand.cs), [InspectCommand](../src/NetWiv.Cli/Commands/InspectCommand.cs) |
| CLI-PLUGIN | netwiv plugin new·init / add / remove | **Implemented** | .NET plugin scaffolding, NuGet references and static registration. | [PluginCommand](../src/NetWiv.Cli/Commands/PluginCommand.cs) |
| CLI-PLUGIN-CI | plugin --github-workflows | **Not implemented** | Automatic CI workflow generation is explicitly rejected. | [PluginCommand](../src/NetWiv.Cli/Commands/PluginCommand.cs) |
| CLI-SIGNER | netwiv signer generate·sign | **Implemented** | Key/signature format support through minisign. The updater runtime remains unimplemented. | [SignerCommand](../src/NetWiv.Cli/Commands/SignerCommand.cs) |
| CLI-BUNDLE | MSI, NSIS, deb, rpm, AppImage, app and DMG generation | **Implemented** | Generation paths exist and require OS-specific packaging tools. macOS supports ad-hoc/identity signing configuration; notarization expansion is excluded and notarization automation is not implemented. Installation/execution/removal acceptance for all formats remains incomplete. | [NativeBundler](../src/NetWiv.Cli/Commands/NativeBundler.cs), [support and validation](implementation-status.md) |
| SDK-NUGET | RID-specific native assets / app consumption without Rust | **Implemented** | Native assets cover win-x64, linux-x64 and osx-arm64. macOS package-only consumption remains unverified; separate from public NuGet distribution. | [Getting started](integration-quickstart.md) |
| TEMPLATE-IDE | React .sln and .esproj / IDE build integration | **Implemented** | Build integration exists with automated execution evidence. Actual Rider/VS solution loading and debugger GUI acceptance remain unverified. | [Template](../templates/react-app/ReactExample.sln), [support and validation](implementation-status.md) |

## Remaining validation and update rules

Implementation status and the acceptance work below are separate. Each must be updated using its own evidence.

- API fixtures map 243 of 688 entries. A row with a source link may still lack a comparison fixture or execution evidence.
- Further comparison is needed across errors, arguments, permissions, lifetimes, isolation, file scopes and remote origins. Specific gaps are maintained in [knownGaps](../compatibility/implementation-evidence.json).
- Upstream business-test ports, full GUI acceptance, package installation/execution/removal and performance measurements are tracked separately in [support and validation](implementation-status.md).
- Before changing a status, review the implementation, supported OSes and behavior, and record fixtures and execution results separately. Adding a type or IPC command name alone does not make a feature implemented.
- Reconcile identifier sets when updating the upstream inventory. This table is a maintained source inventory for the pinned version, not an execution report. Recompute aggregates from the individual rows and the fixture map when classifications change.
- Include a feature ID such as `EXT-DIALOG`, or an API identifier such as `window.Window.setEffects`, and the target OS in feature requests and bug reports.

## Source legend

- **U**: [Official JS and type reference](../compatibility/upstream-lock.json)
- **B**: [Core commands](../src/NetWiv.Core/BuiltIn/BuiltInCommands.cs)
- **A**: [App commands](../src/NetWiv.Core/BuiltIn/ApplicationCommands.cs)
- **I**: [JS bridge](../src/NetWiv.Core/Ipc/InitializationScript.cs)
- **P**: [IPC transport](../src/NetWiv.Core/Ipc/AppHandle.Protocol.cs)
- **C**: [Channel](../src/NetWiv.Core/Ipc/Channel.cs)
- **R**: [Managed resources](../src/NetWiv.Core/Resources/ResourceTable.cs)
- **N**: [Native windows and WebViews](../native/netwiv-native/src/runtime.rs)
- **M**: [Native menus, trays and images](../native/netwiv-native/src/resources.rs)
- **D**: [Native paths](../native/netwiv-native/src/paths.rs)
- **F**: [File asset protocol](../src/NetWiv.Core/Assets/FileAssetProtocol.cs)
- **H**: [Plugin host](../src/NetWiv.Core/Plugins/Plugin.cs)
- **E**: [Event bus](../src/NetWiv.Core/Events/EventBus.cs)

[U]: ../compatibility/upstream-lock.json "Official JS and type reference"
[B]: ../src/NetWiv.Core/BuiltIn/BuiltInCommands.cs "Core commands"
[A]: ../src/NetWiv.Core/BuiltIn/ApplicationCommands.cs "App commands"
[I]: ../src/NetWiv.Core/Ipc/InitializationScript.cs "JS bridge"
[P]: ../src/NetWiv.Core/Ipc/AppHandle.Protocol.cs "IPC transport"
[C]: ../src/NetWiv.Core/Ipc/Channel.cs "Channel"
[R]: ../src/NetWiv.Core/Resources/ResourceTable.cs "Managed resources"
[N]: ../native/netwiv-native/src/runtime.rs "Native windows and WebViews"
[M]: ../native/netwiv-native/src/resources.rs "Native menus, trays and images"
[D]: ../native/netwiv-native/src/paths.rs "Native paths"
[F]: ../src/NetWiv.Core/Assets/FileAssetProtocol.cs "File asset protocol"
[H]: ../src/NetWiv.Core/Plugins/Plugin.cs "Plugin host"
[E]: ../src/NetWiv.Core/Events/EventBus.cs "Event bus"

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
