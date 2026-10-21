# @netwiv/api

The single supported NetWiv desktop frontend API, including plugin guests.
Pinned Tauri implementations remain private inside the package; applications
need no `@tauri-apps/api` or `@tauri-apps/plugin-*` dependencies.

For generated Blazor clients, `core.invokeJson(command, argsJson, options?)`
transports JSON text without converting numbers to JavaScript `number`. Input
must encode an object; the result is JSON text. Binary responses are unsupported.
This requires a matching NetWiv runtime. Ordinary `invoke` and `JSON.parse`
retain JavaScript's numeric precision limits.

```ts
import { invoke } from '@netwiv/api/core';
import { getCurrentWindow } from '@netwiv/api/window';
import { open } from '@netwiv/api/plugins/dialog';

const message = await invoke<string>('greet', { name: 'Desktop' });
await getCurrentWindow().setTitle(message);
const selected = await open({ multiple: false });
```

Calls require the matching runtime, command registrations, .NET plugins and
capability permissions. A frontend import does not grant access. Module availability
does not establish support for every operation on every platform.

Core subpaths: `/app`, `/core`, `/dpi`, `/event`, `/image`, `/menu`, `/path`,
`/resources`, `/tray`, `/window`, `/webview`, `/webviewWindow`. The root exports
these namespaces. Plugins use `/plugins/<name>`: `autostart`, `cli`,
`clipboard-manager`, `deep-link`, `dialog`, `fs`, `global-shortcut`, `http`, `log`,
`notification`, `opener`, `os`, `positioner`, `process`, `shell`, `sql`, `store`,
`stronghold`, `upload`, `websocket`, `window-state`. SQL and WebSocket have default
class exports. Backend-only plugins do not have guest modules.

Use exact subpaths without `.js`. ES modules and TypeScript declarations are
provided; CommonJS, mocks and private implementation subpaths are not exported.

Mobile targets and the updater are excluded. Mobile core handles/options,
notification scheduling/actions/channels, mobile picker modes and mobile fs
security-scoped helpers are absent. Notifications support title, body, icon and
sound. Window creation and plugin option wrappers reject excluded mobile options.
Opener rejects the mobile `inAppBrowser` selector. Desktop platform restrictions
and .NET plugin limitations still apply.

Direct Tauri frontend integration is retired. Migrate core imports to
`@netwiv/api/<module>` and plugin imports to `@netwiv/api/plugins/<name>`, remove
upstream frontend dependencies, and regenerate command bindings. Remove `isTauri`
and `isNetWiv` calls: neither helper nor global marker exists. No replacement
detection or runtime version API is introduced. Application command wire names
and payloads remain unchanged. Browser previews must avoid native operations or
use application-owned mocks.

Before npm publication, generated starters include this built package as
`file:vendor/netwiv-api`. Keep its files and notices together. See the repository's
[frontend SDK guide](https://github.com/kim0522ms/NetWiv/blob/main/docs/frontend-api.md)
for migration and backend support limits.

Maintainers use `npm ci && npm test`; `npm pack` builds the distributable.
Runtime JavaScript and declarations are copied privately under `lib/upstream`;
plugin imports share the NetWiv facade's core objects. See `THIRD-PARTY-NOTICES.md`
for versions, sources, license notices and modifications.

## License and support

Provided AS IS under [Apache-2.0](LICENSE-APACHE), subject to its warranty
disclaimer and limitation of liability. Technical support descriptions and
validation results do not offer additional warranties or a service-level
agreement. No ongoing maintenance, security updates or response or resolution
time is promised. Applicable legal requirements and any separate written
agreement remain unaffected. Third-party components retain their own licenses.
