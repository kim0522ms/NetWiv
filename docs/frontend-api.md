# NetWiv desktop frontend SDK

`@netwiv/api` is the NetWiv-owned desktop JavaScript/TypeScript entry point,
maintained under `packages/api` in this monorepo. The initial facade reuses
`@tauri-apps/api@2.11.1` internally. It is the first step toward frontend
independence, not an independent transport implementation or a full Tauri
compatibility claim.

```ts
import { isNetWiv } from '@netwiv/api/core';
import { getCurrentWindow } from '@netwiv/api/window';
import { commands } from './generated/commands.js';

if (isNetWiv()) {
  await getCurrentWindow().setTitle('My application');
  await commands.greet({ name: 'World' });
}
```

`commands.greet` is an example generated from an application's registered C#
command. The frontend package does not grant permissions or register commands.
Backend capability checks remain authoritative. Runtime detection is a
convenience for the UI, not a security boundary.
The title example needs `core:window:allow-set-title` in a capability applying to
the calling window. With an app manifest, `greet` also needs an explicitly
resolved application-command permission; see [generated bindings](frontend-generator.md).

## Ownership and compatibility

NetWiv owns the public facade contract and can replace its internal dependencies
module by module. The official Tauri package remains unchanged as a separately
tested compatibility path. Existing applications can continue to import it;
facade-only tests do not establish Tauri compatibility.

The package lives with the C# runtime, generators and templates so a protocol or
type change can be reviewed and tested together. Its npm identity is independent
of repository layout. Consumers must still use supported frontend/runtime
combinations; sharing a repository does not make independently installed
versions interchangeable. The initial facade and the runtime revision that
introduces `window.isNetWiv` must be used together for NetWiv detection.

New bindings import the NetWiv facade. Existing generated files need the new
package installed before regeneration. Backend command names and wire payloads
are unchanged in this step. `getVersion` continues to mean the application's
version; it must not be interpreted as the NetWiv runtime version.

## Desktop boundary

Only explicit desktop module exports are public. Mobile-only functionality is
excluded now, not scheduled as future work. This includes mobile back-button
handlers, activity/scene identifiers and creation options, input-accessory-view
options and named mobile suspend/resume events. These exclusions cover facade
types and ordinary runtime access, including handles returned by factories.

Options shared by desktop and mobile remain where they support desktop behavior,
such as macOS WebView data stores. Generic plugin listener and permission helpers
remain usable with registered desktop plugin commands; they do not implement
mobile permissions or make a plugin available automatically. Android-specific
permission rationale is not part of the facade's permission-state type.

The updater remains excluded. Official plugin implementations are separate from
this core facade. Unsupported upstream entries stay visible in the compatibility
inventory; do not remove them to inflate compatibility coverage.

The facade is not a sandbox against consumers deliberately importing the
underlying Tauri dependency. Desktop support and authorization are enforced by
the runtime, independently of the TypeScript API surface.

## Local package distribution

Before registry publication, starters use a portable `file:vendor/netwiv-api`
dependency containing the package's shipped files. The official pinned dependency
is restored transitively. No personal paths or dependency on the maintainer's
checkout are needed in the generated application. This is a local distribution
mechanism, not an assertion that the package is published on npm.

For a new JS/TS app, follow the [quickstart](integration-quickstart.md) and run
`npm ci` in the generated directory. Import desktop APIs from modules such as
`@netwiv/api/core`, `@netwiv/api/event`, `@netwiv/api/window`,
`@netwiv/api/webviewWindow`, `@netwiv/api/menu`, `@netwiv/api/tray` and
`@netwiv/api/path`. The package's explicit export map is the public module
boundary; private upstream internals and the upstream `mocks` module are not
facade exports.

To migrate an existing JS/TS app before npm publication:

1. Generate a starter with the same NetWiv release as the app's SDK and copy its
   complete `vendor/netwiv-api` directory, including notices, into the app.
2. From the app directory, run `npm install --save-exact ./vendor/netwiv-api`
   and retain the updated manifest, lockfile and vendored package together.
3. Update application imports from supported `@tauri-apps/api/*` modules to
   `@netwiv/api/*`, replace bridge detection with `isNetWiv`, and regenerate
   command bindings with the matching SDK. Remove mobile-only usages rather
   than routing them around the desktop facade.
4. Build and run the affected frontend/backend paths. Capability permissions,
   command registration and platform limits still apply.

Keep direct `@tauri-apps/api` imports where deliberately testing or retaining
the official compatibility path. Official plugin packages such as
`@tauri-apps/plugin-dialog` are separate and keep their existing names; this
facade does not replace them. Blazor and Rust/WASM starters use a standalone
NetWiv core module instead of an npm install; see [template coverage](template-coverage.md).

The facade's package tests must install an actual npm tarball in an isolated
consumer and check the allowed exports, desktop declarations and excluded mobile
surface. Integration tests also exercise generated bindings through the real
backend. A successful package build alone does not prove WebView execution,
NativeAOT compatibility or behavior on every OS.

Replacing Wry, Tao, Muda, Tray-icon or the native ecosystem/toolchain is outside
this frontend independence work.
