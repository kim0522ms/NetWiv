# @netwiv/api

The NetWiv desktop frontend API. This first facade delegates to the unchanged,
exactly pinned `@tauri-apps/api@2.11.1` dependency. Applications can use NetWiv
imports while the implementation can evolve separately. This is not a new IPC
transport or a claim that every desktop operation works on every platform.

```ts
import { invoke, isNetWiv } from '@netwiv/api/core';
import { getCurrentWindow } from '@netwiv/api/window';

if (isNetWiv()) {
  const message = await invoke<string>('greet', { name: 'Desktop' });
  await getCurrentWindow().setTitle(message);
}
```

ES modules and TypeScript declarations are available at the package root and at
`/app`, `/core`, `/dpi`, `/event`, `/image`, `/menu`, `/path`, `/resources`, `/tray`,
`/window`, `/webview` and `/webviewWindow`. The root exports matching namespaces
and `isNetWiv`. Use these exact subpaths without `.js`; internal files and other
subpaths are not public exports. CommonJS is not provided.

The facade includes desktop application metadata, commands, channels, events,
DPI values, images, menus, paths, resources, tray icons, windows and webviews.
Platform-specific desktop behavior retains its existing runtime requirements.
Generic plugin listeners and permission helpers require the corresponding
plugin commands to be registered; they do not provide OS permission handling.

Mobile targets and the updater runtime are excluded. The facade omits Android
back-button APIs, activity/scene methods and constructor options, the iOS input
accessory option, and mobile suspended/resumed event constants. Its
`PermissionState` contains `granted`, `denied` and `prompt`. Options also used by
macOS, such as data stores, link previews and background throttling, remain.
Window and webview factories return desktop handles; JavaScript constructors
reject the omitted mobile options. Handle wrappers preserve upstream receivers
and `instanceof` behavior without modifying upstream objects or prototypes.
They provide an API boundary, not a security sandbox against reflection.

`isNetWiv()` reads NetWiv's runtime marker and is safe during server rendering.
It does not infer NetWiv from Tauri compatibility internals. The facade does not
export `isTauri` or `getTauriVersion`; no NetWiv runtime version API is introduced.
Existing apps that require the official Tauri surface can keep importing the
unchanged official package directly. Migrating desktop imports to `@netwiv/api`
does not require changing command names or payloads.

Maintainers build with `npm ci && npm run build` and run focused tests with
`npm test` in this directory. `npm pack` builds the distributable automatically.
The package includes generated declaration-only copies of the pinned upstream
types with explicit relative module extensions for NodeNext consumers; runtime
code always uses the unchanged dependency. See `THIRD-PARTY-NOTICES.md`.
