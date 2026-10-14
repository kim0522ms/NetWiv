# Differential desktop conformance

The same browser code imports the unchanged official `@tauri-apps/api@2.11.1` package and runs in the Rust TAURI® reference host and NetWiv. The build script downloads the pinned npm tarball, verifies SHA-512, and copies its JavaScript without modification.

```sh
node tests/conformance/scripts/build-frontend.mjs
```

Build both hosts from one stable checkout, then run the pair sequentially. Functional case and completion records travel over stdout and are compared in memory. The runner does not retain reports or record source, executable or environment inventories. Rebuild both hosts after source changes; do not edit the checkout while a pair is running.

Reference host, from the repository root (use the platform's native executable path):

```sh
cargo build --locked --manifest-path tests/conformance/reference/src-tauri/Cargo.toml
```

For the current fixture profile, build NetWiv through the CLI below. A direct
project publish without generated configuration does not select the file-based
ACL and scoped asset profile.

Direct project publishing remains useful for separate host development:

```sh
dotnet publish tests/conformance/netwiv/NetWiv.Conformance.csproj -c Release -r linux-x64 -p:NetWivRuntime=wry -o artifacts/conformance/linux-x64
node tests/conformance/scripts/run-host.mjs --reference tests/conformance/reference/src-tauri/target/debug/netwiv-tauri-reference --candidate artifacts/conformance/linux-x64/NetWiv.Conformance
```

For the file-based application ACL profile, run the CLI from
`tests/conformance/netwiv`:

```sh
netwiv build --no-bundle -- -p:NetWivRuntime=wry
```

The generated `NetWivConfigPath` selects the host's embedded-configuration branch,
which contains no manual `Permission`, `Capability` or `AppCommandManifest`
calls. Feed the resulting `target/<target-triple>/release/NetWiv.Conformance`
executable to `run-host.mjs`. The `src-netwiv/permissions` and `capabilities` files
are the ACL input for this profile. On WSL, use a source copy in the Linux
filesystem if the Windows mount rejects MSBuild timestamp updates.

CLI integration is a separate check: change into `tests/conformance/netwiv`, then run `netwiv dev`. The CLI `--config` option is a merge overlay and is not a project selector.

Build `netwiv-native` explicitly before publishing the Wry host. On Windows use `win-x64` and `.exe` paths. Under WSL keep Cargo's build directory on the Linux filesystem, set `CARGO_TARGET_DIR` explicitly and adjust the reference executable path. Select `GDK_BACKEND=x11` or `GDK_BACKEND=wayland` for separate comparisons; WSLg's Wayland socket may require `WAYLAND_DISPLAY=/mnt/wslg/runtime-dir/wayland-0`.

The runner sets `NETWIV_CONFORMANCE_ROOT` for fixture assets and passes the
validated source-selected inventory through `NETWIV_CONFORMANCE_CASES`. For a
focused regression, put `--cases invoke.invalid-arguments,invoke.json` before
`--reference`. Unknown, empty and duplicate names are errors. Each executable
must emit every selected case exactly once and a matching completion count.
The runner rejects malformed records, failures, missing/duplicate cases,
nonzero exits and timeouts, and closes its child and origin servers on failure.
On Windows, the runner uses system Windows PowerShell to place each suspended
host in a kill-on-close Job Object before it starts. This also cleans descendants
after the host exits. Linux requires Python 3 and uses a child subreaper that
terminates and waits for owned descendants before exiting, including when PID 1
does not reap or a child creates a separate session. Reaping uses `waitpid`;
live adopted children are identified by PPID in standard `/proc/<pid>/stat`,
without requiring the optional procfs `task/children` entry. macOS uses an owned process
group. The isolation runner shares these launchers. No process inventory or
execution report is retained.
Availability skips require an exact known upstream/platform boundary and are
counted separately from passes. Unselected cases do not count toward API
coverage; a focused comparison does not replace the full desktop gate.

Menu and tray tests validate construction, mutation and resource disposal;
native pointer-event automation needs a separate OS driver.

Both hosts use the same explicit core capability permissions. The
`security.literal-command-permissions` fixture grants the literal command keys
`*` and `custom_*`, and denies the literal key `echo_*`. It verifies that the
ungranted `restricted` command remains denied while the explicitly granted
`echo_json` command succeeds. These strings are exact keys, not command globs.

`webview.reparent-inflight-channel` pauses `stream_numbers` after channel value
`0` using a harness event gate. It moves the invoking WebView, destroys its old
parent, verifies that a new command loses the old window's capability, then
resumes the admitted invocation and checks its ordered `1,2` messages and reply.
The child imports the unchanged official Channel, invoke and event APIs. Run
this case alongside `webview.reparent-capability`, `channel.ordered` and
`security.frame-origins` to check the neighboring native document and ACL paths.
The separate Core tests selected by `--filter "IPC boundary"` check both IPC
transports with a fake runtime: replacement/close cancellation, rejected late
resource creation, asynchronous DI scope disposal, and reparented channel and
resource lifetime. Those tests do not establish native desktop behavior.

The checksum-addressed upstream config schema is checked out without line-ending
conversion, including when Windows Git uses `core.autocrlf=true`. Run
`node tests/conformance/scripts/verify-fixtures.mjs` to verify its pinned bytes;
regenerate compatibility data through the upstream manifest script when needed.

# Reference feature profile

The full desktop comparison enables Tauri `tray-icon`, `unstable`, `image-png`
and `image-ico`, plus `protocol-asset`, in addition to its default features. Child Webview creation is
feature-gated upstream; image decoding also requires its format feature. The
profile is pinned in `compatibility/upstream-lock.json` and the reference
`Cargo.toml`. Executables from the earlier tray-only profile must be rebuilt for the expanded
profile; matching results do not prove that a stale executable used the current lock.

The `core.convert-file-src-http` fixture verifies successful scoped text and
binary fetches, HEAD, ranges, denied/missing paths, traversal, directory and NUL
errors. Multipart boundary randomness alone is normalized. WebView2 rejects
suffix/multipart range preflights; WebKitGTK permits those custom-scheme requests.
The fixture requires the ordinary GET/HEAD/range successes before recording this
browser-specific rejection, so two non-working protocols cannot pass together.

Core's separate MIME corpus check uses `NETWIV_INFER_TESTDATA` pointing to
`testdata` in infer commit `4256ea07165a9393186b6c190541fac4b5e6f9f5`.
`tests/NetWiv.Core.Tests/fixtures/mime-infer-0.19.0-oracle.json` records the pinned
Rust oracle output and SHA-256 for 63 files and their 8192-byte prefixes. Without
that directory, the corpus check explicitly reports a skip; signature unit
checks still run. The asset batch ran the corpus on both NativeAOT hosts.

## macOS runs

Build the native adapter first with `bash scripts/build-native.sh`. Use a
logged-in macOS desktop, the original process main thread and matching native
assets. The NetWiv conformance project defaults to Wry on macOS; no GTK wrapper
or `GDK_BACKEND` setting is needed. For the file-based ACL profile, run from
`tests/conformance/netwiv`:

```sh
netwiv build --target osx-arm64 --no-bundle -- -p:NetWivRuntime=wry
```

Then, from the repository root:

```sh
node tests/conformance/scripts/run-host.mjs --reference tests/conformance/reference/src-tauri/target/debug/netwiv-tauri-reference --candidate tests/conformance/netwiv/target/osx-arm64/release/NetWiv.Conformance
```

Use `osx-x64` on Intel and a matching reference binary on the same Mac. The runner
uses the current platform only to validate availability skips; it does not record
platform metadata. Run each architecture separately. If platform-specific
fixtures need adjustment, keep failures/skips visible rather than changing the
unchanged official guest or treating two failures as a match. Windows/WebKitGTK observations
above do not establish WKWebView behavior.

For a source-linked NativeAOT fixture, reuse the expanded configuration produced
by the managed CLI build above. From the repository root:

```sh
dotnet publish tests/conformance/netwiv/NetWiv.Conformance.csproj -c Release -r osx-arm64 --self-contained true \
  -p:PublishAot=true -p:NetWivRuntime=wry \
  -p:NetWivConfigPath="$PWD/tests/conformance/netwiv/src-netwiv/obj/netwiv/osx-arm64/release/effective.conf.json" \
  -p:NetWivResourceBaseDirectory="$PWD/tests/conformance/netwiv/src-netwiv" \
  -p:NetWivFrontendDist="$PWD/tests/conformance/dist" -o artifacts/conformance/aot
```

Use `artifacts/conformance/aot/NetWiv.Conformance` as the candidate. This route
was published and executed for the focused store comparison. The source-linked
fixture's CLI `build --aot` currently propagates `PublishTrimmed=true` to its
`netstandard2.0` generator project and fails with `NETSDK1124`; the direct publish
above avoids that global property. It does not establish that CLI route as passing.

The Apple Silicon smoke and local app/DMG tests described in the
[macOS guide](../../docs/macos.md) have run; the full macOS reference/candidate
comparison described here remains unverified.

### Website data stores

Select `app.website-data-store-sdk-lifecycle` for the bounded website-data-store
comparison. Both hosts execute the same unchanged public guest app and window
functions from `@tauri-apps/api@2.11.1`. Windows/Linux must return an empty
identifier list and successful no-op removal. A test-only command uses each
host's SDK to create two random test-owned stores on macOS 14+; this fixture
does not compare the public JavaScript creation option. The guest APIs assert
exact identifier bytes, isolated persistence after close, active-store removal
rejection, incognito precedence, closed-store deletion and empty recreation.
Cleanup removes only the generated identifiers, never ambient stores. Initial
enumeration waits up to five seconds for WebKit to create the persistent store
after its first write. Only the pinned removal-error string after closing is
retried, for at most five seconds; other errors remain failures. The pinned
runtime collapses native removal failures into that string.

The host exposes only an OS enum and feature-availability boolean for this test's
branching. macOS below 14 skips the unsafe pinned-upstream identifier selectors
with a specific availability reason; NetWiv's SDK smoke still checks its explicit
unsupported error. A matching skip is not a macOS compatibility pass. The focused
SDK lifecycle comparison passed managed and NativeAOT on physical macOS arm64;
the full suite, Intel and minimum-OS executions remain unverified.

For the independent NetWiv public creation-option regression, run a NetWiv
conformance executable through the same in-memory assertion transport:

```sh
NETWIV_CONFORMANCE_STORE_OPTIONS=1 node --input-type=module -e "import { runHost } from './tests/conformance/scripts/run-host.mjs'; await runHost(['path/to/NetWiv.Conformance'], ['webview.data-store-public-option']);"
```

Run separately for managed and NativeAOT on macOS 14+. This page creates windows
with the unchanged public `WebviewWindow` constructor and performs the same
storage lifecycle assertions. The pinned upstream's guest/config conversion
does not forward `dataStoreIdentifier`, so this independent result is not an
upstream compatibility pass; see [the source and observed difference](../../docs/pinned-upstream-behavior.md#website-data-store-creation-and-removal).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
