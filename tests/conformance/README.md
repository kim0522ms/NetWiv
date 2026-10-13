# Differential desktop conformance

The same browser code imports the unchanged official `@tauri-apps/api@2.11.1` package and runs in the Rust TAURI® reference host and NetWiv. The build script downloads the pinned npm tarball, verifies SHA-512, and copies its JavaScript without modification.

```sh
node tests/conformance/scripts/build-frontend.mjs
```

Build both hosts from one stable checkout, then launch their executables through the provenance wrapper. A run records source, suite, upstream lock and executable hashes, platform, display backend and process failures. Source hashes describe the checkout at execution time; rebuild both hosts after source changes. Parallel source edits invalidate a run.

Reference host, from the repository root (use the platform's native executable path):

```sh
cargo build --locked --manifest-path tests/conformance/reference/src-tauri/Cargo.toml
node tests/conformance/scripts/run-host.mjs reference native tests/conformance/results/reference.jsonl -- tests/conformance/reference/src-tauri/target/debug/netwiv-tauri-reference
```

For the current fixture profile, build NetWiv through the CLI below. A direct
project publish without generated configuration does not select the file-based
ACL and scoped asset profile.

Direct project publishing remains useful for separate host development:

```sh
dotnet publish tests/conformance/netwiv/NetWiv.Conformance.csproj -c Release -r linux-x64 -p:NetWivRuntime=wry -o artifacts/conformance/linux-x64
node tests/conformance/scripts/run-host.mjs netwiv nativeaot tests/conformance/results/netwiv.jsonl -- artifacts/conformance/linux-x64/NetWiv.Conformance
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

Build `netwiv-native` explicitly before publishing the Wry host. On Windows use `win-x64` and `.exe` paths. Under WSL keep Cargo's build directory on the Linux filesystem, set `CARGO_TARGET_DIR` explicitly and adjust the reference executable path. Select `GDK_BACKEND=x11` or `GDK_BACKEND=wayland` and record separate runs; WSLg's Wayland socket may require `WAYLAND_DISPLAY=/mnt/wslg/runtime-dir/wayland-0`.

The wrapper sets `NETWIV_CONFORMANCE_RESULT` and `NETWIV_CONFORMANCE_ROOT`. Compare completed runs with:

```sh
node tests/conformance/scripts/compare-results.mjs tests/conformance/results/reference.jsonl tests/conformance/results/netwiv.jsonl
```

For a focused regression, pass `--cases invoke.invalid-arguments,invoke.json`
before `--` on both `run-host.mjs` commands. Unknown, empty and duplicate names
are errors. The wrapper passes the validated selection through
`NETWIV_CONFORMANCE_CASES`; the hosts expose it only to the harness frontend.
Reports retain the full suite inventory and explicitly mark focused runs.
Unselected cases have no result and do not count toward API coverage. Both
executables must include the current suite and harness; a focused run does not
replace the full desktop acceptance gate.

The runner records every real success, failure, and platform skip. A missing result, unexpected failure, or stubbed result fails comparison. Menu and tray tests validate construction, mutation and resource disposal; native pointer-event automation is tracked separately because it needs an OS driver.

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
`Cargo.toml`. Results from the earlier tray-only profile are not evidence for
the expanded profile. They are rejected by the lock identity check.

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
node tests/conformance/scripts/run-host.mjs netwiv nativeaot tests/conformance/results/netwiv-macos-arm64.jsonl -- tests/conformance/netwiv/target/osx-arm64/release/NetWiv.Conformance
```

Use `osx-x64` on Intel and a matching reference binary on the same Mac. The wrapper
records Node's `darwin` platform and CPU architecture; the native ready event
reports `macos`. Keep results for each architecture separate. If platform-specific
fixtures need adjustment, retain failures/skips rather than changing the unchanged
official guest or treating two failures as a match. Windows/WebKitGTK observations
above do not establish WKWebView behavior.

The Apple Silicon smoke and local app/DMG tests described in the
[macOS guide](../../docs/macos.md) have run; the full macOS reference/candidate
comparison described here remains unverified.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
