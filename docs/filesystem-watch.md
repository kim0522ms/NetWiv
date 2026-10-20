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
OS path before delivery. On Linux and macOS, native movement notifications for
the watched directory and its ancestors permanently invalidate that registration,
including when a directory leaves and returns before a callback runs. On Windows,
the verification handle prevents moving or deleting the watched directory until
the watch closes. This is a deliberate difference from an ordinary
`FileSystemWatcher`: protecting scoped notifications takes precedence over
allowing a watched directory to move.
For a recursive Windows watch, this restriction also applies to registered
descendant directories. Close the watch before moving or deleting those
directories; file creation, modification, rename and deletion remain available.
File watches protect the containing directory. Linux requires `/proc/self/fd`
and inotify; macOS requires vnode notifications. Registration fails if the
directory's identity or movement cannot be verified.

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

Recursive watches use one nonrecursive native registration per observed
directory, each with its own movement guard. This avoids a .NET 9/10 Linux
`FileSystemWatcher` failure that silently stops a recursive watcher when a
descendant directory moves out. New and moved-in directories gain registrations;
removed and moved-out subtrees lose them. Symbolic-link descendants are not
traversed. Other descendants are registered even when their directory names do
not match the scope: a deeper filename may still be allowed. Every delivered
path passes the full scope check, and denied paths are never included.
Topology changes discard pending batches and request a pathless rescan;
native buffer overflow rebuilds registrations while preserving the original
root guard. Retired callbacks cannot resume delivery after a directory returns
to its old path.

Each watch resource accepts at most **128 directory registrations**, shared
across its requested roots, including registrations under denied directories.
This bounds native handles and Linux watcher
threads; recursive watching is not intended for unrestricted large trees.
Initial registration exceeding the limit fails and releases its registrations.
Later growth beyond the limit stops the watch with `watch-failed`. Operating
system resource limits can reject a registration earlier. Prefer narrower
roots or nonrecursive watches for large directory trees.

Registration and topology updates have notification gaps, so applications must
refresh scoped state when they receive a rescan hint. No denied names are
included in these hints.

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
continued delivery inside the authorized tree. Move-out-and-back checks run on
Linux/macOS without an intermediate identity check. Windows verifies blocked
directory moves and successful movement after watch disposal. Concurrent rearm
and close across multiple roots and fresh debounce windows have regression
coverage. These tests do not establish live WebView interaction or exact
cross-platform event parity. Native buffer overflow and
terminal OS watcher errors require separate fault validation; their handling
must not be counted as a passing real-device scenario solely from these tests.

The wire reference is the pinned
[plugin-fs guest](https://github.com/tauri-apps/plugins-workspace/blob/6aa2854f314481a459be1189b02c65a2450789ab/plugins/fs/guest-js/index.ts#L1131-L1277)
recorded in `compatibility/upstream-lock.json`.
