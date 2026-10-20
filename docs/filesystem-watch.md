# Desktop filesystem watching

Register `FsPlugin` in the .NET app and import `watch` or `watchImmediate` from
`@netwiv/api/plugins/fs`. They accept one path or an array of paths, including
file URLs. Paths must already exist. Relative paths require `baseDir`.

```ts
import { BaseDirectory, watch } from '@netwiv/api/plugins/fs';

const unwatch = await watch('documents', (event) => {
  const attrs = event.attrs as { rescan?: boolean; error?: string };
  if (attrs.rescan) {
    // Refresh the application's scoped directory listing.
    return;
  }
  if (attrs.error) {
    // The watcher has stopped. Offer retry or refresh after fixing the cause.
    return;
  }
  refreshChangedPaths(event.paths);
}, { baseDir: BaseDirectory.AppData, recursive: true, delayMs: 250 });

// On component teardown:
unwatch();
```

The example assumes `documents` exists and `refreshChangedPaths` is supplied by
the application. Watching provides change notifications; use ordinary scoped
filesystem operations to obtain the current contents.

## Permissions and ownership

Watching requires `fs:allow-watch` and a matching path scope. It is not included
in the fs default permissions. The returned unwatch function uses the standard
resource close command, requiring `core:resources:allow-close` (also available
through the appropriate core default permission set).

For example, a capability's permissions can contain:

```json
[
  "fs:allow-watch",
  "core:resources:allow-close",
  {
    "identifier": "fs:scope",
    "allow": [
      { "path": "$APPDATA/documents" },
      { "path": "$APPDATA/documents/**" }
    ],
    "deny": [
      { "path": "$APPDATA/documents/private" },
      { "path": "$APPDATA/documents/private/**" }
    ]
  }
]
```

All requested roots must pass the existing fs path, traversal, selected-path,
deny and symlink checks before native watchers start. Every event path passes
the same scope check again, including after a debounce delay. An event with no
authorized paths is dropped. A rename across the scope boundary includes only
the authorized endpoint and uses rename mode `from` or `to`. A root whose
resolved target has changed no longer emits events; close and register it again.
The watcher also holds a directory handle and verifies its identity or current
OS path before delivery. Moving a watched directory or its ancestor out of scope cannot keep
publishing events under the old pathname, even if that pathname is replaced.
File watches verify the containing directory. Linux requires `/proc/self/fd` for
this check; registration fails if the directory's identity cannot be verified.

A frontend watcher belongs to its originating webview document. Another
document cannot close it. Reload, navigation that replaces the document,
webview close, plugin disposal and app disposal close the native watcher,
discard pending events and end the channel. The guest unwatch function starts
an asynchronous close and returns `void`; do not assume it provides an awaited
barrier. Backend resource close prevents further data delivery. In-flight
native rearm and worker cleanup can finish after that close returns.

Registration rejects malformed options, empty path arrays, missing paths,
forbidden paths and canceled invocations. A failed multi-path registration
disposes any watchers already constructed. Invocation cancellation governs
registration; after success, the resource lifetime governs the watch.

## Event and platform limits

NetWiv uses .NET `FileSystemWatcher` on Windows, Linux and macOS. The supported
frontend shape follows the pinned plugin-fs 2.5.2 guest, but native event parity
with its Rust `notify` implementation is not claimed. This is a deliberate
implementation difference for a maintained, NativeAOT-compatible .NET backend;
using Rust notify would add another native integration solely for event detail.
No frontend import or call migration is needed.

| Notification | `event.type` |
| --- | --- |
| Create | `{ create: { kind: 'any' } }` |
| Content or metadata change | `{ modify: { kind: 'any' } }` |
| Remove | `{ remove: { kind: 'any' } }` |
| Rename | `{ modify: { kind: 'rename', mode: 'both' \| 'from' \| 'to' } }` |
| Lost events / pending batch capacity | `'any'`, empty paths, `attrs.rescan: true` |
| Terminal watcher or callback failure | `'other'`, empty paths, `attrs.error: 'watch-failed'`, then channel end |

Ordinary events have empty `attrs`. Errors contain no native exception text or
paths. After event loss, refresh the authorized roots; after a terminal error,
close the resource and retry registration when appropriate. An unavailable
callback runtime may prevent even the terminal notification from being delivered.

Recursive directory watches replace the native watcher after a rename, deletion or
native buffer overflow. Some inotify backends retain watches on directories moved
out of the subscribed tree, and delayed batches can contain paths whose parent
has moved. Retiring the old instance prevents their callbacks
from disclosing names under stale path prefixes. Retired callbacks and debounce
batches are discarded, and affected authorized activity requests a rescan. Changes
confined to denied paths still issue a pathless rescan because the retired
native queue may also contain authorized events. No denied names are included.
The replacement keeps the original root identity check;
it does not follow a relocated root. Rearming has a short notification gap, so
applications must refresh scoped state when they receive a rescan hint.

`watchImmediate` delivers without an intentional delay. `watch` defaults to
2000 ms through the guest. A nonnegative integer `delayMs` up to 2147483647 is
accepted; zero delivers immediately. Positive delays use a fixed window starting
with the first event and coalesce identical kind/path tuples within that window.
This avoids starving applications under continuous writes. A pending batch is
limited to 4096 distinct tuples; exceeding it requests a rescan. NetWiv does not
reproduce notify's rename correlation or debounce transformations.

Directories are nonrecursive by default. `recursive: true` includes descendants;
file watches only report that file's path, including rename from/to that path,
and do not follow its new name. Nonrecursive directory watches can report a
direct child directory's metadata change when a deeper file changes. Native
backends differ in event counts, ordering, duplication, rename pairing, and
root-deletion behavior. Access/open/close details and file/folder subtype
classification are not synthesized. Atomic saves may appear as several events.
Watchers are not a journal, and cannot guarantee delivery under filesystem races,
buffer pressure, network filesystems or root replacement. Refresh actual state
rather than treating an event stream as a complete audit log.

Events are historical observations and do not guarantee that a path still exists
at delivery. An authorized observation queued before a move may arrive afterward;
generation resets may instead discard that batch. Names first created outside
the authorized tree must not be reported through stale native path prefixes.

## Focused verification

`FsWatchTests` exercises real temporary filesystem mutations with the test
runtime for callback delivery. Managed and NativeAOT executions passed on physical
Windows x64 and macOS arm64 hosts and Linux x64 under WSL, through headless test
processes. It covers CRUD and rename notifications, explicit
close, file URLs, multiple files, recursion, debounce, allow/deny filtering,
cross-webview ownership, reload/close cleanup, invalid input, cancellation and
plugin disposal. The Linux symlink case checks escaped roots and queued events
after an ancestor is retargeted. Root-move tests cover root/ancestor/descendant
moves, replacement paths and newly created outside filenames, followed by
continued delivery inside the authorized tree. Ancestor moves run on Linux/macOS
because Windows denies that move with an active child watcher. Concurrent rearm
and close across multiple roots and fresh debounce windows have regression
coverage. These tests do not establish live WebView interaction or exact
cross-platform event parity. Native buffer overflow and
terminal OS watcher errors require separate fault validation; their handling
must not be counted as a passing real-device scenario solely from these tests.

The wire reference is the pinned
[plugin-fs guest](https://github.com/tauri-apps/plugins-workspace/blob/6aa2854f314481a459be1189b02c65a2450789ab/plugins/fs/guest-js/index.ts#L1131-L1277)
recorded in `compatibility/upstream-lock.json`.
