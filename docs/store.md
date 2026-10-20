# JSON store persistence

Register `NetWiv.Plugin.Store.StorePlugin` in the application builder and grant
the store commands through the application's capabilities. Frontend code uses
the supported desktop facade:

```typescript
import { load } from '@netwiv/api/plugins/store';

const settings = await load('settings.json', {
  defaults: { theme: 'system' },
  autoSave: 250
});
await settings.set('theme', 'dark');
// Wait for an explicit persistence result when the next action depends on it.
await settings.save();
```

Paths must be relative to the application data directory, without `.` or `..`
segments. The format is a JSON object; custom Rust serializer names are rejected.
The implementation uses `JsonElement` and `Utf8JsonWriter`, including under
NativeAOT, without runtime-generated serializers.

## Automatic and explicit saving

| `autoSave` | Behavior |
| --- | --- |
| Omitted, `true`, or `null` | Save 100 milliseconds after the latest mutation. |
| Positive unsigned integer | Save after that many milliseconds without another mutation. |
| `0` | Save synchronously during each mutation. |
| `false` | Save only when `save()` is called. Disposal does not save these changes. |

The numeric range is 0 through 18446744073709551615 milliseconds. Negative,
fractional, out-of-range and non-boolean/non-numeric values are rejected.
JavaScript callers should use safe integer values to avoid numeric precision loss.
Long delays use bounded timer intervals without truncating the configured delay.

`set`, successful `delete`, `clear` and `reset` schedule saving. A new mutation
restarts the debounce interval. `save()` cancels a pending automatic write and
synchronously writes the current snapshot, even with `autoSave: false`. A later
mutation starts a new interval. Positive-delay mutations complete after updating
memory and scheduling persistence; they do not wait for the disk write.

Successful `reload()` replaces the in-memory values and cancels their pending
automatic save, including a later shutdown flush. Reloading a missing file does
not create it. A failed reload preserves the current values and pending save;
a new mutation after a successful reload schedules saving normally.

This corrects earlier NetWiv behavior that saved immediately for every option
except `false`. Applications relying on that immediate persistence should call
`save()` explicitly or select `autoSave: 0`.

All handles for one normalized path share one cache, save queue and lock. The
first load's options apply to subsequent `load` and `getStore` handles. Closing
one document-owned handle does not cancel another handle's pending save. Stores
remain owned by the plugin until application/plugin disposal. These are NetWiv's
existing resource ownership rules; they differ from upstream's shared resource
identifier and close lifetime.

Writes use a temporary file in the destination directory, flush it, then replace
the destination atomically. Mutations, snapshots, automatic saves and disposal
serialize under the same store lock. This protects handles within one plugin
instance; separate applications, plugin instances and external file editors are
not coordinated.

## Shutdown and errors

Dispose the application normally, for example with `using var app = ...`, so the
plugin cancels its timers and flushes dirty stores with automatic saving enabled.
Pending writes and previously failed automatic writes are retried during disposal.
Disposal attempts every store and reports an aggregate exception if any flush
fails. A completed disposal cannot start another automatic write. Abrupt process
termination or power loss cannot run this cleanup; use `save()` at important
application boundaries.

A failed background save leaves the current values dirty, emits a generic warning
through the configured `NetWiv.Store` logger, and does not continuously retry.
Another mutation schedules a new attempt; `save()` and disposal also retry.
`save()` returns the actual write failure to its caller. Background warnings omit
store paths and exception details; an explicit save failure can contain ordinary
filesystem exception details. After a disposal flush fails, the plugin is closed;
handle the exception rather than assuming the write succeeded.

## Validation boundary

The Core harness checks option parsing, debounce across mutations, shared
concurrent handles, explicit-save and successful-reload cancellation, failed
reload preservation, atomic replacement, disposal
races, flush failures and recovery. These backend checks use a fake desktop
runtime and synthetic JSON files. Managed and NativeAOT executions passed on
physical Windows x64 and macOS arm64 hosts and Linux x64 under WSL, through
headless test processes. They do not establish interactive WebView guest or
clean-machine distribution acceptance. The timing contract is based on pinned
[`plugin-store` 2.4.5](https://github.com/tauri-apps/plugins-workspace/blob/3c5d2677677dc7ebea618514af56fc951449cc1b/plugins/store/src/store.rs)
and its guest declarations; this focused implementation is not a full upstream
comparison pass.
