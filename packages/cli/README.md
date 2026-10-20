# `@netwiv/cli`

This package locates and launches a NetWiv CLI executable for the current platform. Set
`NETWIV_CLI_PATH` to test a local build. Native binaries must be supplied in the
`native/<platform>-<architecture>` directory when packaging a release.

Updater key generation and signing use the pinned minisign 0.12 sidecar in the
same native directory. Set `NETWIV_MINISIGN_PATH` only when testing a separate
verified minisign 0.12 executable.

Provided AS IS under [Apache-2.0](LICENSE-APACHE), subject to its warranty
disclaimer and limitation of liability. Technical support descriptions and
validation results do not offer additional warranties or a service-level
agreement. No ongoing maintenance, security updates or response or resolution
time is promised. Applicable legal requirements and any separate written
agreement remain unaffected. Third-party components retain their own licenses.

## macOS

The launcher resolves `native/darwin-arm64/netwiv` or
`native/darwin-x64/netwiv` from Node's platform/architecture. Those paths are
lookup conventions, not evidence that this checkout or a published npm package
contains macOS executables. It can also run a supplied `native/netwiv.dll`
through `dotnet`, or find `netwiv` on PATH.

For macOS application development, use the `NetWiv.Cli` .NET tool and matching
desktop NuGet assets described in the [macOS guide](../../docs/macos.md) and
[package quickstart](../../docs/integration-quickstart.md). The npm sidecar lock
`native-tools.json` records Windows/Linux minisign binaries and the verified
0.12 source archive used for macOS tests. `scripts/prepare-test-tools.mjs` builds
that source on macOS arm64; export its printed `NETWIV_MINISIGN_PATH` when
running local signer tests. This does not populate npm release sidecars. Updater minisign signatures are separate from macOS
`codesign` signatures and do not notarize an app.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
