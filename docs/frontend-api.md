# NetWiv desktop frontend SDK

`@netwiv/api` is the single supported JavaScript/TypeScript entry point for
NetWiv desktop core and plugin APIs. It is maintained in `packages/api` in this
monorepo. Direct `@tauri-apps/api` and `@tauri-apps/plugin-*` use is no longer a
supported application integration. This is a breaking frontend contract change.

The internal facade remains: pinned Tauri core and plugin implementations are
bundled privately, with plugin imports routed through the same NetWiv core.
Applications do not install upstream npm dependencies. This change does not
replace IPC transport or native toolchains, or expand backend plugin support.

```ts
import { getCurrentWindow } from '@netwiv/api/window';
import { open } from '@netwiv/api/plugins/dialog';
import { readTextFile } from '@netwiv/api/plugins/fs';
import { commands } from './generated/commands.js';

await getCurrentWindow().setTitle('My application');
const selected = await open({ multiple: false });
if (selected) console.log(await readTextFile(selected));
await commands.greet({ name: 'World' });
```

The example requires the corresponding .NET plugins, capability permissions and
filesystem scope. Generated application commands also require explicit permission
when an app manifest exists. Installing a frontend module grants no permissions.
See [plugins](official-plugins.md) and [generated bindings](frontend-generator.md).

If an `invoke()` protocol request or response-body read fails, that invocation
rejects without automatic replay. The native command may already have executed
or may still be running. Subsequent invocations use the message transport for
that page; a host known to lack protocol IPC uses it from the start. Previously,
protocol failure retried the same command over the message transport, which could
duplicate side effects. Handle rejection explicitly and retry state-changing work
only with application-level idempotency or a status check. This is not a guarantee
against retries initiated by application code.

When the document receives `pagehide`, pending `invoke()` and `invokeJson()`
calls reject with an error named `AbortError`. Their callbacks are removed;
queued isolation messages and late preparation results cannot dispatch those calls.
New calls also reject until `pageshow` resumes admission. This is a document
lifetime policy; native origin, nonce and epoch checks still authorize delivery.
Protocol completion and isolation preparation retain their original client
generation, so a restored page cannot attribute retired work to a reused callback ID.
This also applies when `pagehide` marks a page for the back/forward cache.
Rejection does not undo native work that already started. These lifetime rules
do not establish browser cache restoration support.

## Public modules

Core modules: `app`, `core`, `dpi`, `event`, `image`, `menu`, `path`, `resources`,
`tray`, `window`, `webview`, `webviewWindow`. The package root exposes these namespaces.

Plugin modules use `@netwiv/api/plugins/<name>`:
`autostart`, `cli`, `clipboard-manager`, `deep-link`, `dialog`, `fs`,
`global-shortcut`, `http`, `log`, `notification`, `opener`, `os`, `positioner`,
`process`, `shell`, `sql`, `store`, `stronghold`, `upload`, `websocket`, `window-state`.
SQL and WebSocket retain their default class exports. SingleInstance and Localhost
are backend-only and have no frontend module. PersistedScope is backend-only too.
The [API status table](api-status.md) describes each backend's existing limits;
a module's presence does not make all upstream behavior available on every OS.

Use exact subpaths without `.js`. Private implementation paths and upstream mocks
are not exported. Consumers deliberately installing upstream code are outside the
supported contract; the export map is not a security sandbox. Backend authorization
remains authoritative. Internal wire names are implementation details.

For native file-drop payloads, WebView-local physical coordinates, zoom-aware
DOM hit testing, and the Windows HTML drag-and-drop trade-off, see
[desktop drag and drop](drag-and-drop.md).

## Desktop boundary

Mobile targets and the updater runtime are excluded, not deferred. Core exports
omit mobile back-button APIs, activity/scene identifiers, input-accessory options
and mobile suspend/resume constants. Window factories return desktop handles.

Plugin exports omit mobile notification scheduling, actions and channels, mobile
file-picker modes and mobile security-scoped fs helpers. Notifications expose
`isPermissionGranted`, `requestPermission` and `sendNotification` with title,
body, icon and sound. The facade rejects excluded notification and picker options
at runtime as well as in TypeScript. Opener rejects the mobile `inAppBrowser`
selector at runtime; named desktop applications remain valid strings. OS types describe the supported desktop
platforms. macOS SDK sandbox/bookmark APIs are separate and unchanged.

Neither `isTauri` nor `isNetWiv` is exported or injected as a window marker.
No replacement detection helper or runtime version API is introduced.
`app.getVersion()` continues to return the application's version.

## Installation and migration

Before registry publication, starters and the HostingApp sample carry a portable
`file:vendor/netwiv-api` dependency. The built package contains its implementation
and notices; no upstream runtime npm dependency or maintainer checkout is needed.
The Blazor starter uses the equivalent standalone vendored modules.

For an existing application:

1. Copy the complete `vendor/netwiv-api` directory from a starter generated with
   the matching NetWiv SDK release. Install it with
   `npm install --save-exact ./vendor/netwiv-api`.
2. Replace supported `@tauri-apps/api/<module>` imports with
   `@netwiv/api/<module>`, and `@tauri-apps/plugin-<name>` imports with
   `@netwiv/api/plugins/<name>`. Remove the old frontend dependencies and update
   the lockfile. Keep the required .NET plugin registrations and permissions.
3. Remove `isTauri` / `isNetWiv` guards and window-marker checks. Run native calls
   inside the NetWiv application; web-only previews must avoid those calls or use
   application-owned mocks. Remove mobile-only usages rather than bypassing the facade.
4. Regenerate command bindings, build, and execute the affected desktop paths.
   Existing command names and payloads are unchanged.

The package and runtime must be used in a supported version combination. This is
local package distribution, not a claim that an npm registry release exists.

## Maintenance and validation

Pin upstream sources and integrity in `compatibility/upstream-lock.json` and
`packages/api/package-lock.json`. Build the SDK, then regenerate template/sample
snapshots with `node scripts/generate-frontend-template-overlays.mjs`.

Package tests install a real tarball in an isolated consumer, exercise the core
and plugin exports, and check NodeNext/Bundler declarations and excluded APIs.
Backend and real-WebView checks cover runtime integration separately. Upstream
comparison fixtures remain internal behavioral references, not a supported second
frontend entry point or a claim of full compatibility. Generated inventories retain
unsupported entries. Package tests alone do not prove GUI or every-platform support.

Replacing Wry, Tao, Muda, Tray-icon or the native ecosystem/toolchain is outside
this frontend independence work.
