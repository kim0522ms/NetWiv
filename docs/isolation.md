# Isolation and IPC transport

NetWiv supports the pinned TAURI® isolation pattern through its Wry backend.
Use matching SDK and native package versions; older development packages may
not provide this transport. Full security parity remains under validation.

```json
{
  "app": {
    "security": {
      "pattern": { "use": "isolation", "options": { "dir": "isolation" } },
      "csp": "default-src 'self'; connect-src 'self' ipc: http://ipc.localhost"
    }
  }
}
```

The directory is relative to the configuration file and must contain index.html
and a script setting `window.__TAURI_ISOLATION_HOOK__`. NetWiv.Build embeds these
assets before NativeAOT. CLI dev watches the isolation directory even when
additional watch folders are overridden. Effective config relocation passes
the original absolute asset directory to MSBuild via NetWivIsolationDist.

The sandboxed iframe receives application payloads before AES-GCM encryption.
Its key is generated per application instance and never sent to the parent.
Core authenticates and decrypts the envelope before capability and command
dispatch. Bundled asset CSP hashes and nonces use an HTML5 parser. The iframe
protocol rejects cross-origin reads and serves only its secure index route.
Every isolation invoke requires an authenticated envelope, including commands
without arguments; an empty protocol body cannot bypass the hook.

Wry registers an asynchronous IPC protocol. Fetch uses native request headers,
WebView identity and document generation. Request injection exposes its JSON
or raw body and headers. CSP/browser rejection switches to postMessage for the
rest of that document, matching the pinned fallback's JSON byte-array behavior.
Headers instances fail structured cloning at the isolation boundary as in Tauri;
use a plain header record for isolation invokes.

## Automatic differential suite

`tests/NetWiv.Isolation.Smoke` and its locked Tauri reference build the same
frontend. After building both native executables:

```sh
node tests/NetWiv.Isolation.Smoke/run.mjs /path/to/reference /path/to/NetWiv.Isolation.Smoke
node --test tests/NetWiv.Isolation.Smoke/compare.test.mjs
```

On WSL, reference/build-linux.sh stages reference sources on a Linux filesystem
because upstream codegen changes permissions unsupported on some shared Windows
files. Set NETWIV_REFERENCE_TARGET to the Linux Cargo output directory. Run the
Node comparison runner under scripts/run-linux-smoke-desktop.sh for X11 or
Wayland. Build both hosts from the same stable source before the pair. The
runner consumes functional assertions from stdout in memory; it does not save
reports, logs or source inventories. Missing results and identical public
failures fail comparison.

Twenty-one cases exercise public behavior: queued startup, nested IPC values,
three binary input shapes, header records/clone errors, structured errors,
Channel ordering, hook blocking, origin boundaries, reload recovery and CSP
fallback. The added combinations check fallback Channel ordering, hook
blocking, opaque-frame denial and successful IPC after a second reload.
Three additional NetWiv security regressions inject forged private messages
or reuse old private callback IDs after protocol and fallback requests.
They must pass on NetWiv. Pinned
Tauri's failures appear as observations, not counted as public conformance
failures or passes. Direct mutation of `__TAURI_INTERNALS__` is outside the
repository's public compatibility guarantee.

The Core assertion harness separately covers isolation on/off, protocol/forced
fallback and replacement/close combinations. Each combination starts with an
allowed local invoke, denies a remote source carrying spoofed local payload
and fallback headers, then verifies invocation cancellation and document-owned
resource disposal. A late handler deliberately ignores cancellation to check
fallback callback suppression. The fake runtime cannot establish browser frame
attestation or delivery of protocol responses after navigation; those require
the desktop fixture. These checks are NetWiv security regressions rather than
additional upstream business-test comparisons.

Windows x64 managed and NativeAOT desktop comparisons passed all twenty-one
public cases and the three NetWiv security regressions. The integrated Linux
X11 NativeAOT candidate also passed all twenty-four assertions under Xvfb with
a tray host. That headless execution is not a paired reference comparison or
physical Linux desktop acceptance. The integrated macOS arm64 NativeAOT
candidate also passed all twenty-four assertions; its reference comparison and
manual interaction remain unverified.

This suite does not establish full API coverage, package-install acceptance or
manual GUI acceptance. The upstream business-test parity gate remains open.

## macOS source-linked smoke

After building the native adapter, run from the repository root in a logged-in
Mac desktop session:

```sh
dotnet publish tests/NetWiv.Isolation.Smoke -c Release -r osx-arm64
tests/NetWiv.Isolation.Smoke/bin/Release/net8.0/osx-arm64/publish/NetWiv.Isolation.Smoke
```

Use `osx-x64` on Intel. Physical Apple Silicon and hosted arm64/x64 runs passed
the NetWiv encrypted IPC, Channel, forged/opaque-frame, reload and fallback smoke.
These NetWiv-only runs are not the paired Tauri differential suite above;
macOS reference comparison remains open. Do not apply WebKitGTK frame/CORS observations to
WKWebView without a matching macOS result. See [macOS validation](macos.md#validation-and-limits).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
