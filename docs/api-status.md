# NetWiv API implementation status

Core public API counts are a **2026-09-12 source-review snapshot**.
Plugin and SDK descriptions include later contracts; execution results do not update automatically.
Targets: **Windows x64 / Linux x64 / macOS arm64 and x64**, official `@tauri-apps/api@2.11.1`.
This is a source-based feature inventory, not a declaration of complete TAURI® equivalence or release acceptance.

The macOS entries below reflect the new runtime paths; historical aggregate counts
remain the dated snapshot and are not recalculated as compatibility passes.

## Reading the status table

| Status | Meaning |
| --- | --- |
| Implemented | An execution path exists within the stated scope. For types and constants, this means the official npm declaration is provided. It does not imply individual comparison or GUI acceptance. |
| Unsupported (intentional) | Deliberately rejected to preserve NetWiv runtime identity; this is a documented compatibility difference, not a pending implementation or a compatibility pass. |
| Planned | Included in the support goals, with implementation or validation work remaining. This does not mean ready for use or scheduled for a specific release. |
| Partial | Some behavior is available, with limits on features, platforms or validation. |
| Not implemented | The feature is unavailable, including declarations that only return empty results or ignore options. Support timelines are announced separately. |

- **Functions, objects, constructors, types, configuration fields and object members are distinct.** An implemented object/type does not imply every member is implemented. Options distinguish declarations from actual application.
- APIs that provide behavior on at least one target OS identify that OS. Unimplemented Apple/mobile features remain explicitly marked. A no-op on an OS unsupported by upstream is not necessarily a NetWiv-specific compatibility defect.
- Window behavior depends on the OS, window manager and WebView version. Linux child WebViews follow GTK Box layout. Read the [platform behavior notes](pinned-upstream-behavior.md) alongside this table.
- The comparison column records only the **presence** of an [API fixture mapping](../compatibility/implementation-evidence.json). Comparison results apply to the tested source, platform and version. A missing mapping does not prove a missing implementation; a mapping does not establish full compatibility.
- This table does not mark per-API GUI acceptance complete. See [support and validation](implementation-status.md) for NativeAOT, execution, GUI and package acceptance boundaries.

## Desktop plugins and extensions

The updater runtime remains **Not implemented in v1**.
The following feature groups cover official desktop plugins outside the core npm inventory. They do not enumerate every method of each plugin. Partial implementations do not count as full compatibility.

| Feature ID | Feature | Status | Scope and alternatives |
| --- | --- | --- | --- |
| EXT-UPDATER | Update checks, downloads and installation | **Not implemented** | Outside v1. CLI signing support does not provide an updater runtime. |
| EXT-DIALOG | Native open/save and message dialogs | **Planned** | `NetWiv.Plugin.Dialog` and the native rfd path are implemented. Windows/Linux/macOS GUI selection, cancellation, parent windows and custom buttons need validation. |
| EXT-FS | Frontend file and directory read/write APIs | **Planned** | Main `NetWiv.Plugin.Fs` commands, scopes and binary IPC are implemented. Watching, the full file-option/metadata surface and upstream comparison remain incomplete. |
| EXT-OS | OS information, hostname and locale | **Planned** | Official guest properties and two commands are implemented in `NetWiv.Plugin.Os`. macOS is incorrectly reported as Linux by the current non-Windows branch; macOS detection and OS-specific version-string comparison remain incomplete. |
| EXT-PROCESS | Exit and restart | **Planned** | Permission, exit and restart paths are implemented in `NetWiv.Plugin.Process`. Installed-app restart validation remains incomplete. |
| EXT-HTTP | Native HTTP client API | **Planned** | `NetWiv.Plugin.Http` implements official fetch IPC, binary response streaming, URL scopes and redirect checks. Proxy and unsafe TLS options, and upstream error/cookie/cancellation comparisons remain incomplete. |
| EXT-SHELL | Process execution and sidecar management APIs | **Planned** | `NetWiv.Plugin.Shell` implements scoped `execute`, `spawn`, stdin, kill and events. macOS `open` still uses `xdg-open` and is incomplete. Sidecars, `openWith`, full encoding support and OS-specific termination-signal comparison remain incomplete. |
| EXT-OPENER | Open URLs and paths in external browsers or default apps | **Planned** | `NetWiv.Plugin.Opener` implements URL/path opening, reveal-in-folder and URL scopes. macOS still uses the non-Windows `xdg-open` path; native macOS opening is incomplete. Explicit apps (`with`), actual GUI launch and error equivalence remain incomplete. |
| EXT-CLIPBOARD | Clipboard text and image APIs | **Planned** | `NetWiv.Plugin.ClipboardManager` and arboard implement text, HTML, images and clearing. Actual Windows/Linux X11/Wayland/macOS clipboard roundtrips need validation. Default permissions allow no commands. |
| EXT-NOTIFICATION | OS notifications and notification permissions | **Planned** | `NetWiv.Plugin.Notification` implements the official desktop `notify` command, two permission commands, the JS `Notification` bridge and native `notify-rust` delivery. Managed/Rust automated checks passed. Actual Windows notification-center/Linux D-Bus/macOS notification display, installed-app identity and error equivalence remain unverified. Mobile channels and scheduling are out of scope. |
| EXT-GLOBAL-SHORTCUT | Global shortcuts while the app is inactive | **Planned** | `NetWiv.Plugin.GlobalShortcut` connects four official commands, native `global-hotkey` registration/removal and Channel events. C#/Rust automated checks passed. Actual inactive-app input on Windows/Linux/macOS, X11/Wayland behavior and reload races remain unverified. Default permissions allow no commands. |
| EXT-STORE | Persistent key-value storage API | **Planned** | `NetWiv.Plugin.Store` implements official guest commands, JSON persistence and shared handles. Autosave debounce, custom serializers and full lifecycle validation remain incomplete. |
| EXT-SQL | Frontend SQL database API | **Planned** | `NetWiv.Plugin.Sql` connects official `load`, `execute`, `select` and `close` IPC to system SQLite. Managed query/binding checks passed on Windows/Linux and Apple Silicon; macOS NativeAOT database distribution remains unverified. MySQL/PostgreSQL, migrations, preload validation and upstream type/error comparison remain incomplete. |
| EXT-AUTOSTART | Registration for launch at OS login | **Planned** | `NetWiv.Plugin.Autostart` implements official `enable`, `disable` and `is_enabled`, the Windows user Run registry and Linux XDG autostart `.desktop` entries. Linux temporary-directory regression checks passed. macOS registration is unsupported. Actual login launch, Windows registration and upstream AppImage comparison remain incomplete. |
| EXT-SINGLE-INSTANCE | Single-instance enforcement and argument forwarding | **Planned** | `NetWiv.Plugin.SingleInstance` detects duplicates, forwards arguments/working directory and requests second-instance exit. Separate-process automated checks passed on Windows/Linux and Apple Silicon. Actual GUI duplicate-launch validation is pending. Linux uses .NET named pipes instead of upstream D-Bus and does not interoperate with Snap/Flatpak service declarations. |
| EXT-DEEP-LINK | Launch and route apps through external URIs | **Planned** | `NetWiv.Plugin.DeepLink` implements four official commands, declared-scheme launch arguments/events and user protocol registration/removal. Windows/Linux registration checks passed; macOS registration is unsupported. Actual OS URI activation, installer integration and upstream error equivalence remain incomplete. |
| EXT-CLI-PLUGIN | Application launch-argument parsing | **Planned** | `NetWiv.Plugin.Cli` implements official `cli_matches` responses and argument/subcommand/value validation. Basic flags, multiple values and subcommands passed automated checks. Advanced clap conditional constraints and help output are explicitly unsupported; upstream comparison remains incomplete. |
| EXT-LOCALHOST | Localhost asset server | **Planned** | `NetWiv.Plugin.Localhost` serves bundled assets over HTTP on a configured port, with CSP, response headers and an `onRequest` hook. GET/HEAD/shutdown checks passed. Actual WebView origin authorization, all HTTP boundaries and upstream equivalence remain unverified. |
| EXT-LOG | Frontend and backend logging | **Partial** | `NetWiv.Plugin.Log` implements the official log command, file/stdout/WebView outputs and file-size/archive-count limits. Long UTF-8 records and concurrent writes have regression coverage. The full Tauri target/filter/rotation policy and backend logger integration remain incomplete. |
| EXT-PERSISTED-SCOPE | Restore file scopes after restart | **Planned** | `NetWiv.Plugin.PersistedScope` restores dialog-selected fs/asset scopes after relaunch; automated restart checks passed. Arbitrary backend scope changes, deny patterns and Tauri's bincode format are unsupported. Storage uses NetWiv JSON and is not interoperable with the upstream file. |
| EXT-POSITIONER | Position windows relative to screens or trays | **Planned** | `NetWiv.Plugin.Positioner` implements three official commands and screen/tray anchor calculations. Synthetic-monitor checks passed. Actual multi-monitor/tray GUI validation and upstream equivalence remain incomplete. |
| EXT-STRONGHOLD | Encrypted storage | **Planned** | `NetWiv.Plugin.Stronghold` forwards 11 official commands to IOTA Stronghold. Native snapshot/vault/store/procedure paths and new snapshot save/reload passed NativeAOT integration checks on Windows/Linux and Apple Silicon. The default Argon2 salt path is `snapshotPath.salt`; an existing Tauri salt path can be passed to the constructor. Existing Tauri snapshot interoperability and all procedure inputs remain unverified, so this is not marked ready for use. |
| EXT-UPLOAD | HTTP file uploads and downloads | **Planned** | `NetWiv.Plugin.Upload` implements official `upload`/`download`, POST/PUT/PATCH, progress Channels and HTTP failure handling. Local-server checks passed. Large transfers, cancellation, TLS and upstream progress equivalence remain unverified. |
| EXT-WEBSOCKET | WebSocket client | **Planned** | `NetWiv.Plugin.Websocket` implements official guest `connect`/`send`, text/binary/close and Channel reception. Local echo and reload cleanup checks passed. Ping/Pong and some buffer/frame options are explicitly unsupported; TLS and upstream comparison remain incomplete. |
| EXT-WINDOW-STATE | Window-state persistence and restoration | **Partial** | Official JS commands, JSON persistence, per-flag restoration, monitor checks, automatic startup restoration and normal-shutdown persistence are implemented. Maximized-window shutdown and restoration in a new process were verified on Windows/Linux X11. macOS save/relaunch restoration, forced-exit durability, Wayland position restoration and full upstream equivalence remain unverified. |
| EXT-MAC | macOS runtime | **Partial** | WKWebView/AppKit, NativeAOT and app/DMG support; Apple Silicon integration checked. Intel execution and full conformance are unverified. See [macOS](macos.md). |
| EXT-MOBILE | Android and iOS runtimes | **Not implemented** | Outside v1. |

This is an implementation inventory, not a catalog of fully validated installations.
`core.checkPermissions`, `requestPermissions` and `addPluginListener` forward commands to a target plugin; they do not provide these features automatically.

## Core public API counts

All **688 identifiers** in the [pinned declaration inventory](../compatibility/generated/api-manifest.json) are included. Root barrel re-exports are excluded; per-module re-exports in the inventory are retained. Inventory SHA-256: `31a4018f16fd798ae30c1b38268168c1d00b10d3b73aee0badf4a9f64626367b`.

**These are not independent feature counts or development completion percentages.** Window/WebviewWindow entry points for the same behavior, types and fields are counted separately.

| Module | Total | Implemented | Planned | Not implemented | Has fixture mapping |
| --- | ---: | ---: | ---: | ---: | ---: |
| [app](#module-app) | 17 | 11 | 0 | 6 | 11 |
| [core](#module-core) | 28 | 28 | 0 | 0 | 15 |
| [dpi](#module-dpi) | 46 | 46 | 0 | 0 | 46 |
| [event](#module-event) | 15 | 15 | 0 | 0 | 4 |
| [image](#module-image) | 14 | 14 | 0 | 0 | 8 |
| [menu](#module-menu) | 123 | 120 | 0 | 3 | 56 |
| [mocks](#module-mocks) | 6 | 6 | 0 | 0 | 4 |
| [path](#module-path) | 34 | 34 | 0 | 0 | 33 |
| [tray](#module-tray) | 35 | 32 | 0 | 3 | 4 |
| [webview](#module-webview) | 56 | 51 | 0 | 5 | 13 |
| [webviewWindow](#module-webviewwindow) | 100 | 96 | 0 | 4 | 6 |
| [window](#module-window) | 214 | 198 | 0 | 16 | 44 |
| **Total** | **688** | **651** | **0** | **37** | **244** |

## Window-effect support

Window effects are implemented as described below. Distinguish argument validation from
the Linux no-op behavior; this does not establish visual-effect or full GUI equivalence.

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `webviewWindow.WebviewWindow.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. Linux performs the same validation, then follows upstream's no-op behavior. | [N] | 1 mapped |
| `window.Window.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. Linux performs the same validation, then follows upstream's no-op behavior. | [N] | 1 mapped |
| `window.WindowOptions.windowEffects` | Configuration member | **Implemented** | The pinned WindowEffectsConfig validates effects, color, state, radius and unknown fields. Invalid creation options are rejected before allocating a window. | [N] | 1 mapped |

## Incomplete core entries

This includes partial implementations, features for unsupported platforms and declarations that only return empty results.
Not every entry is part of the v1 support goals.

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `app.fetchDataStoreIdentifiers` | Function | **Not implemented** | Explicit unsupported error on macOS; pinned non-Apple desktop empty/no-op behavior is retained. | [A] | 1 mapped |
| `app.hide` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.onBackButtonPress` | Function | **Not implemented** | Android back-button behavior is not implemented (outside v1). Only the JS entry point exists. | [U] [A] | No individual mapping |
| `app.removeDataStore` | Function | **Not implemented** | Explicit unsupported error on macOS; pinned non-Apple desktop empty/no-op behavior is retained. | [A] | 1 mapped |
| `app.setDockVisibility` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.show` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `menu.AboutMetadata.credits` | Configuration member | **Partial** | Metadata is forwarded to muda on macOS; credits display remains unverified. | [M] [U] | No individual mapping |
| `menu.Submenu.setAsHelpMenuForNSApp` | Object member | **Partial** | Forwarded to muda on macOS; interactive menu-role behavior remains unverified. | [M] | No individual mapping |
| `menu.Submenu.setAsWindowsMenuForNSApp` | Object member | **Partial** | Forwarded to muda on macOS; interactive menu-role behavior remains unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setIconAsTemplate` | Object member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setIconWithAsTemplate` | Object member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `tray.TrayIconOptions.iconAsTemplate` | Configuration member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `webview.WebviewOptions.acceptFirstMouse` | Configuration member | **Partial** | Forwarded to Wry on macOS; first-click interaction remains unverified. The option has no effect on Windows/Linux. | [N] [U] | No individual mapping |
| `webview.WebviewOptions.allowLinkPreview` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.backgroundThrottling` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.dataStoreIdentifier` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Apple-only WebView option (outside v1). Not applied during native creation. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.setBadgeLabel` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setTitleBarStyle` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.Effects.radius` | Configuration member | **Partial** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.Effects.state` | Configuration member | **Partial** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.Window.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.setBadgeLabel` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.Window.setTitleBarStyle` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.WindowOptions.activityName` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.allowLinkPreview` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.backgroundThrottling` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.createdByActivityName` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.hiddenTitle` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.requestedBySceneIdentifier` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.tabbingIdentifier` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.titleBarStyle` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.trafficLightPosition` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |

## Complete public JavaScript/TypeScript API inventory

For method parameters and overloads, look up the same identifier in the [pinned declaration inventory](../compatibility/generated/api-manifest.json).
Union alternatives within type aliases and every enum value are not expanded into separate rows.

<a id="module-app"></a>

### app

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `app.BundleType` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.DataStoreIdentifier` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.defaultWindowIcon` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | No individual mapping |
| `app.fetchDataStoreIdentifiers` | Function | **Not implemented** | Explicit unsupported error on macOS; pinned non-Apple desktop empty/no-op behavior is retained. | [A] | 1 mapped |
| `app.getBundleType` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getIdentifier` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getName` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.getTauriVersion` | Function | **Unsupported (intentional)** | Rejects because NetWiv does not embed the TAURI framework. The command identifier remains accepted; no upstream or NetWiv version is substituted. See [identity differences](implementation-status.md#runtime-identity-differences). | [B] | Not a compatibility pass |
| `app.getVersion` | Function | **Implemented** | Application metadata and desktop command handling. | [B] [A] | 1 mapped |
| `app.hide` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.onBackButtonPress` | Function | **Not implemented** | Android back-button behavior is not implemented (outside v1). Only the JS entry point exists. | [U] [A] | No individual mapping |
| `app.OnBackButtonPressPayload` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `app.removeDataStore` | Function | **Not implemented** | Explicit unsupported error on macOS; pinned non-Apple desktop empty/no-op behavior is retained. | [A] | 1 mapped |
| `app.setDockVisibility` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
| `app.setTheme` | Function | **Implemented** | Applies the theme to all windows through the native runtime. | [N] | No individual mapping |
| `app.show` | Function | **Partial** | Forwarded to the AppKit event-loop target on macOS; NativeAOT smoke exercised. Non-Apple desktop behavior remains a no-op. | [A] | 1 mapped |
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
| `core.InvokeArgs` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `core.InvokeOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `core.InvokeOptions.headers` | Configuration member | **Implemented** | Forwards request headers to the IPC Request. | [P] | No individual mapping |
| `core.isTauri` | Function | **Implemented** | Official JS API connected to the C# IPC bridge. | [U] [I] [P] | 2 mapped |
| `core.PermissionState` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `core.SERIALIZE_TO_IPC_FN` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | 1 mapped |
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
| `event.Event` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.Event.event` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.Event.id` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.Event.payload` | Data member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.EventCallback` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.EventName` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.EventTarget` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.listen` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 4 mapped |
| `event.once` | Function | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | 1 mapped |
| `event.Options` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.Options.target` | Configuration member | **Implemented** | Event registration, targeting, delivery and removal. | [B] [E] | No individual mapping |
| `event.TauriEvent` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `event.UnlistenFn` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |

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
| `image.ImageSize` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `image.ImageSize.height` | Data member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.ImageSize.width` | Data member | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | 1 mapped |
| `image.MenuIcon` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `image.transformImage` | Function | **Implemented** | Image decoding, RGBA and resource lifetime handling. | [M] [R] | No individual mapping |

<a id="module-menu"></a>

### menu

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `menu.AboutMetadata` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.AboutMetadata.authors` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.AboutMetadata.comments` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.copyright` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.AboutMetadata.credits` | Configuration member | **Partial** | Metadata is forwarded to muda on macOS; credits display remains unverified. | [M] [U] | No individual mapping |
| `menu.AboutMetadata.icon` | Configuration member | **Implemented** | Decodes the image and forwards it to muda's About dialog. Displayed on Linux; unsupported on Windows. | [M] [U] | 1 mapped |
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
| `menu.CheckMenuItemOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `menu.IconMenuItemOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `menu.MenuItemOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.MenuItemOptions.accelerator` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.action` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.enabled` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuItemOptions.text` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `menu.MenuOptions.id` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.MenuOptions.items` | Configuration member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.NativeIcon` | Constant | **Implemented** | The official enum is forwarded to muda for macOS system icons; visual appearance remains unverified. | [U] [M] | No individual mapping |
| `menu.PredefinedMenuItem` | Object | **Implemented** | muda menus, items and resources. Support varies by OS. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.close` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.PredefinedMenuItem.constructor` | Constructor | **Implemented** | muda menus, items and resources. Support varies by OS; see option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.id` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.rid` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.PredefinedMenuItem.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.PredefinedMenuItem.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.PredefinedMenuItem.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 2 mapped |
| `menu.PredefinedMenuItemOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `menu.Submenu.setAsHelpMenuForNSApp` | Object member | **Partial** | Forwarded to muda on macOS; interactive menu-role behavior remains unverified. | [M] | No individual mapping |
| `menu.Submenu.setAsWindowsMenuForNSApp` | Object member | **Partial** | Forwarded to muda on macOS; interactive menu-role behavior remains unverified. | [M] | No individual mapping |
| `menu.Submenu.setEnabled` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.setIcon` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | No individual mapping |
| `menu.Submenu.setText` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.static.new` | Static member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.Submenu.text` | Object member | **Implemented** | muda menus, items and resources. Support varies by OS. | [M] [N] | 1 mapped |
| `menu.SubmenuOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |

<a id="module-mocks"></a>

### mocks

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `mocks.clearMocks` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.mockConvertFileSrc` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.mockIPC` | Function | **Implemented** | Uses the official JS implementation; no separate native feature is required. | [U] | 1 mapped |
| `mocks.MockIPCOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `path.BaseDirectory` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `tray.MouseButton` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.MouseButtonState` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIcon` | Object | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. Object availability does not imply every member is complete. | [M] [N] | No individual mapping |
| `tray.TrayIcon.close` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.constructor` | Constructor | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. See option rows for unsupported behavior. | [M] [N] | No individual mapping |
| `tray.TrayIcon.id` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.rid` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setIcon` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setIconAsTemplate` | Object member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setIconWithAsTemplate` | Object member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `tray.TrayIcon.setMenu` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIcon.setMenuOnLeftClick` | Object member | **Implemented** | Configures left-click menus on Windows. Unsupported on Linux. menuOnLeftClick is a deprecated alias. | [M] | No individual mapping |
| `tray.TrayIcon.setShowMenuOnLeftClick` | Object member | **Implemented** | Configures left-click menus on Windows. Unsupported on Linux. menuOnLeftClick is a deprecated alias. | [M] | No individual mapping |
| `tray.TrayIcon.setTempDirPath` | Object member | **Implemented** | Configures the temporary icon path on Linux. No effect on Windows. | [M] | No individual mapping |
| `tray.TrayIcon.setTitle` | Object member | **Implemented** | Linux tray-title integration requires icon/host support. Unsupported on Windows. | [M] | No individual mapping |
| `tray.TrayIcon.setTooltip` | Object member | **Implemented** | Windows tray-tooltip integration. Unsupported on Linux. | [M] | No individual mapping |
| `tray.TrayIcon.setVisible` | Object member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.getById` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.new` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIcon.static.removeById` | Static member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | 1 mapped |
| `tray.TrayIconClickEvent` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEvent` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEventBase` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconEventType` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `tray.TrayIconOptions.action` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.icon` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.iconAsTemplate` | Configuration member | **Partial** | Forwarded to tray-icon on macOS; template-image appearance remains unverified. | [M] | No individual mapping |
| `tray.TrayIconOptions.id` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.menu` | Configuration member | **Implemented** | tray-icon integration. Linux requires a tray host; input-event support varies by platform. | [M] [N] | No individual mapping |
| `tray.TrayIconOptions.menuOnLeftClick` | Configuration member | **Implemented** | Configures left-click menus on Windows. Unsupported on Linux. menuOnLeftClick is a deprecated alias. | [M] | No individual mapping |
| `tray.TrayIconOptions.showMenuOnLeftClick` | Configuration member | **Implemented** | Configures left-click menus on Windows. Unsupported on Linux. menuOnLeftClick is a deprecated alias. | [M] | No individual mapping |
| `tray.TrayIconOptions.tempDirPath` | Configuration member | **Implemented** | Configures the temporary icon path on Linux. No effect on Windows. | [M] | No individual mapping |
| `tray.TrayIconOptions.title` | Configuration member | **Implemented** | Linux tray-title integration requires icon/host support. Unsupported on Windows. | [M] | No individual mapping |
| `tray.TrayIconOptions.tooltip` | Configuration member | **Implemented** | Windows tray-tooltip integration. Unsupported on Linux. | [M] | No individual mapping |

<a id="module-webview"></a>

### webview

| API identifier | Kind | Status | Behavior and limits | Source | Comparison fixtures |
| --- | --- | --- | --- | --- | --- |
| `webview.Color` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.DragDropEvent` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `webview.WebviewLabel` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.WebviewOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webview.WebviewOptions.acceptFirstMouse` | Configuration member | **Partial** | Forwarded to Wry on macOS; first-click interaction remains unverified. The option has no effect on Windows/Linux. | [N] [U] | No individual mapping |
| `webview.WebviewOptions.allowLinkPreview` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.backgroundColor` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.backgroundThrottling` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.dataDirectory` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.dataStoreIdentifier` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `webview.WebviewOptions.devtools` | Configuration member | **Implemented** | wry WebView behavior connected to the official JS objects. GTK layout constraints apply. | [N] [I] | No individual mapping |
| `webview.WebviewOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Apple-only WebView option (outside v1). Not applied during native creation. | [N] [U] | No individual mapping |
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
| `webviewWindow.Color` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webviewWindow.DragDropEvent` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `webviewWindow.getAllWebviewWindows` | Function | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | 1 mapped |
| `webviewWindow.getCurrentWebviewWindow` | Function | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow` | Object | **Implemented** | Combined window and WebView object. Individual member/option limits apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.center` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.clearAllBrowsingData` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.clearEffects` | Object member | **Implemented** | Windows effect removal is implemented. No effect on Linux. This is separate from remaining setEffects object-argument validation work. | [N] | No individual mapping |
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
| `webviewWindow.WebviewWindow.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.setAlwaysOnBottom` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setAlwaysOnTop` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setAutoResize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setBackgroundColor` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setBadgeCount` | Object member | **Implemented** | Linux badge-count integration depends on the desktop host. Unsupported on Windows; use setOverlayIcon instead. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setBadgeLabel` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setClosable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setContentProtected` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorGrab` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorIcon` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setCursorVisible` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setDecorations` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. Linux performs the same validation, then follows upstream's no-op behavior. | [N] | 1 mapped |
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
| `webviewWindow.WebviewWindow.setOverlayIcon` | Object member | **Implemented** | Windows overlay-icon integration. Linux returns an explicit unsupported error. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setPosition` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setProgressBar` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setResizable` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setShadow` | Object member | **Implemented** | Window shadows on Windows. No effect on Linux. | [N] | No individual mapping |
| `webviewWindow.WebviewWindow.setSimpleFullscreen` | Object member | **Implemented** | Windows/Linux use the same borderless fullscreen as setFullscreen, following the official JS non-macOS contract. The macOS-only mode is out of scope. | [N] [U] | No individual mapping |
| `webviewWindow.WebviewWindow.setSize` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setSizeConstraints` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setSkipTaskbar` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTheme` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTitle` | Object member | **Implemented** | Combined window and WebView object. Individual member/option limits apply. | [N] [I] | No individual mapping |
| `webviewWindow.WebviewWindow.setTitleBarStyle` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
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
| `window.BackgroundThrottlingPolicy` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.CloseRequestedEvent` | Object | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.constructor` | Constructor | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply; see option rows for unsupported behavior. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.event` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.id` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.isPreventDefault` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CloseRequestedEvent.preventDefault` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Color` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.currentMonitor` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.CursorIcon` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.cursorPosition` | Function | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.DragDropEvent` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effect` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effects` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Effects.color` | Configuration member | **Implemented** | Windows blur/acrylic color parsing and application. See setEffects for validation limits on the complete effect configuration. | [N] | No individual mapping |
| `window.Effects.effects` | Configuration member | **Implemented** | Processes effect-name arrays and selects a supported Windows effect. Apple effects are out of scope; see setEffects for full configuration validation. | [N] | No individual mapping |
| `window.Effects.radius` | Configuration member | **Partial** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.Effects.state` | Configuration member | **Partial** | Validated and applied through window-vibrancy on macOS; Sidebar effect and clearing exercised. | [N] | No individual mapping |
| `window.EffectState` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `window.Monitor` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| `window.ProgressBarState` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ProgressBarState.progress` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ProgressBarState.status` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ProgressBarStatus` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ScaleFactorChanged` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.ScaleFactorChanged.scaleFactor` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ScaleFactorChanged.size` | Data member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.ScrollBarStyle` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Theme` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.TitleBarStyle` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.UserAttentionType` | Constant | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.Window` | Object | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. Object availability does not imply every member is complete. | [N] [I] | No individual mapping |
| `window.Window.activityName` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.center` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.clearEffects` | Object member | **Implemented** | Windows effect removal is implemented. No effect on Linux. This is separate from remaining setEffects object-argument validation work. | [N] | No individual mapping |
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
| `window.Window.sceneIdentifier` | Object member | **Not implemented** | Mobile activity/scene lookup is not implemented (outside v1). No native command handler exists. | [N] [U] | No individual mapping |
| `window.Window.setAlwaysOnBottom` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setAlwaysOnTop` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setBackgroundColor` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setBadgeCount` | Object member | **Implemented** | Linux badge-count integration depends on the desktop host. Unsupported on Windows; use setOverlayIcon instead. | [N] | 1 mapped |
| `window.Window.setBadgeLabel` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
| `window.Window.setClosable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setContentProtected` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorGrab` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorIcon` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setCursorVisible` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setDecorations` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setEffects` | Object member | **Implemented** | Apply/remove Windows and macOS effects with full argument validation. Linux performs the same validation, then follows upstream's no-op behavior. | [N] | 1 mapped |
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
| `window.Window.setOverlayIcon` | Object member | **Implemented** | Windows overlay-icon integration. Linux returns an explicit unsupported error. | [N] | No individual mapping |
| `window.Window.setPosition` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setProgressBar` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setResizable` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setShadow` | Object member | **Implemented** | Window shadows on Windows. No effect on Linux. | [N] | No individual mapping |
| `window.Window.setSimpleFullscreen` | Object member | **Implemented** | Windows/Linux use the same borderless fullscreen as setFullscreen, following the official JS non-macOS contract. The macOS-only mode is out of scope. | [N] [U] | No individual mapping |
| `window.Window.setSize` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 2 mapped |
| `window.Window.setSizeConstraints` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setSkipTaskbar` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setTheme` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.Window.setTitle` | Object member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.Window.setTitleBarStyle` | Object member | **Partial** | Applied through Tao on macOS; NativeAOT smoke exercised. No mutation on Windows/Linux. | [N] | No individual mapping |
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
| `window.WindowLabel` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.WindowOptions` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
| `window.WindowOptions.activityName` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.allowLinkPreview` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.alwaysOnBottom` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.alwaysOnTop` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.backgroundColor` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.backgroundThrottling` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.center` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.closable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.contentProtected` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.createdByActivityName` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.decorations` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.disableInputAccessoryView` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.focus` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.focusable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.fullscreen` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.height` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.hiddenTitle` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
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
| `window.WindowOptions.requestedBySceneIdentifier` | Configuration member | **Not implemented** | Apple/mobile-only creation option (outside v1). Type/configuration acceptance does not apply it to native behavior. | [N] [U] | No individual mapping |
| `window.WindowOptions.resizable` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.scrollBarStyle` | Configuration member | **Implemented** | Configures WebView2 fluentOverlay on Windows. Unsupported on Linux. | [N] | No individual mapping |
| `window.WindowOptions.shadow` | Configuration member | **Implemented** | Window shadows on Windows. No effect on Linux. | [N] | No individual mapping |
| `window.WindowOptions.skipTaskbar` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.tabbingIdentifier` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.theme` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.title` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.titleBarStyle` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.trafficLightPosition` | Configuration member | **Partial** | Forwarded to the macOS Tao/Wry backend. Option-specific interaction and upstream comparison remain unverified; see [macOS](macos.md). | [N] [U] | No individual mapping |
| `window.WindowOptions.transparent` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.visible` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | 1 mapped |
| `window.WindowOptions.visibleOnAllWorkspaces` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.width` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.windowEffects` | Configuration member | **Implemented** | The pinned WindowEffectsConfig validates effects, color, state, radius and unknown fields. Invalid creation options are rejected before allocating a window. | [N] | 1 mapped |
| `window.WindowOptions.x` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowOptions.y` | Configuration member | **Implemented** | tao window behavior connected to the official JS objects. Window-manager constraints apply. | [N] [I] | No individual mapping |
| `window.WindowSizeConstraints` | Type | **Implemented** | Official npm declaration is provided. See member rows for runtime support. | [U] | No individual mapping |
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
| GEN-BINDINGS | Generated JavaScript functions and TypeScript DTOs | **Implemented** | C#-named input/output DTOs, name disambiguation, generics, build integration and actual React calls. | [Usage](frontend-generator.md), [Emitter](../src/NetWiv.Generators/FrontendBindingEmitter.cs) |
| GEN-SERIALIZATION | JSON names, enums, nulls, directional ignores and supported ExtensionData | **Implemented** | Generates only supported DTO shapes. Does not guess types or silently use any. | [Supported scope](frontend-generator.md) |
| GEN-ADVANCED | Custom converters, polymorphism, unsupported ExtensionData and nonpublic JsonInclude | **Not implemented** | Explicitly rejected with DTOXGEN009. No additional support timeline is set. | [Limits](frontend-generator.md), [Emitter](../src/NetWiv.Generators/FrontendBindingEmitter.cs) |
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
| CLI-BUNDLE | MSI, NSIS, deb, rpm, AppImage, app and DMG generation | **Implemented** | Generation paths exist and require OS-specific packaging tools. macOS supports ad-hoc/identity signing configuration; notarization is not implemented. Installation/execution/removal acceptance for all formats remains incomplete. | [NativeBundler](../src/NetWiv.Cli/Commands/NativeBundler.cs), [support and validation](implementation-status.md) |
| SDK-NUGET | RID-specific native assets / app consumption without Rust | **Implemented** | Native assets cover win-x64, linux-x64, osx-arm64 and osx-x64. macOS package-only consumption remains unverified; separate from public NuGet distribution. | [Getting started](integration-quickstart.md) |
| TEMPLATE-IDE | React .sln and .esproj / IDE build integration | **Implemented** | Build integration exists with automated execution evidence. Actual Rider/VS solution loading and debugger GUI acceptance remain unverified. | [Template](../templates/react-app/ReactExample.sln), [support and validation](implementation-status.md) |

## Remaining validation and update rules

Implementation status and the acceptance work below are separate. Each must be updated using its own evidence.

- API fixtures map 244 of 688 entries. A row with a source link may still lack a comparison fixture or execution evidence.
- Further comparison is needed across errors, arguments, permissions, lifetimes, isolation, file scopes and remote origins. Specific gaps are maintained in [knownGaps](../compatibility/implementation-evidence.json).
- Upstream business-test ports, full GUI acceptance, package installation/execution/removal and performance measurements are tracked separately in [support and validation](implementation-status.md).
- Before changing a status, review the implementation, supported OSes and behavior, and record fixtures and execution results separately. Adding a type or IPC command name alone does not make a feature implemented.
- Reconcile identifier sets when updating the upstream inventory. This table is a manually reviewed snapshot for the pinned version, not an automatically refreshed execution report.
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
