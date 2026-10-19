# NativeAOT Mac App Store example and sandbox smoke

This source-linked example uses the normal `app-store` CLI target. It checks
sandbox policy, SDK application paths, persisted files, WKWebView guest IPC and
local storage across two processes. Its fixed identifier is test-owned. It is a
functional test, not a distribution approval check. Prepare the macOS native
asset as described in [the macOS guide](../../docs/macos.md).

From the repository root on macOS:

```sh
dotnet build src/NetWiv.Cli -c Release
repo="$PWD"
cd tests/NetWiv.MacOS.Sandbox.Smoke
dotnet "$repo/src/NetWiv.Cli/bin/Release/net8.0/netwiv.dll" build --aot --target osx-arm64 --bundles app-store --no-sign

outside=$(mktemp -d)
export NETWIV_SANDBOX_OUTSIDE_DIRECTORY="$outside"
app="target/osx-arm64/release/bundle/app-store/NetWiv-Sandbox-Smoke.app"
"$app/Contents/MacOS/NetWiv.MacOS.Sandbox.Smoke" --sandbox write
"$app/Contents/MacOS/NetWiv.MacOS.Sandbox.Smoke" --sandbox read
rmdir "$outside"
unset NETWIV_SANDBOX_OUTSIDE_DIRECTORY
```

Both runs must exit zero and print 14 `PASS` assertions. Run them in a logged-in
macOS GUI session. The read phase removes the fixture's files and local-storage
entry; no personal files are read. A fixed synthetic sentinel remains in the
test-owned local storage so cleanup does not empty the database. The OS-owned
app container may remain.
Keep the outside directory outside the app's sandbox container. A watchdog
fails the test if WebView execution does not complete.

Each write uses a new synthetic value, also kept in the container marker for the
read phase. A previous run's value cannot satisfy the persistence assertion.
Before exiting, each phase waits up to ten seconds for the WebKit SQLite store
to expose the committed write or removal through an independent read-only
connection. The fixture reads only its own test container and synthetic key;
it never changes the database directly or retains a validation report.
Keeping the sentinel avoids repeatedly exercising WebKit's asynchronous deletion
of empty databases between these consecutive process launches. That teardown
behavior is outside this restart-persistence fixture's assertions.
This check follows the `localstorage.sqlite3`/`ItemTable` layout exercised on
macOS 15. An unrecognized storage layout fails the commit deadline rather than
passing. Other OS versions still require their own execution checks.

The commit barrier matters: completing `localStorage.setItem()` and reading the
same page's cached value did not reliably survive immediate process exit in the
original fixture. This verifies restart persistence after an observed commit;
it does not establish immediate-exit durability or change product shutdown.
Guest failures identify IPC, storage access, value mismatch or cleanup with fixed
labels, without printing exceptions, stored values or paths.

Focused regressions:

```sh
dotnet test tests/NetWiv.Api.Tests -c Release --filter FullyQualifiedName~LocalStoragePersistenceTests
node --test tests/NetWiv.MacOS.Sandbox.Smoke/guest.test.mjs
```

The macOS API cases use real SQLite transactions to check pending writes,
stale values, commit/removal, missing storage and cancellation. They are skipped
explicitly on other platforms. The guest tests execute the actual inline script
with controlled IPC and storage boundaries; they do not replace the packaged run.

The produced `*-unsigned.pkg` can be expanded with `pkgutil --expand-full` into
a fresh temporary directory. Check the extracted `.app` with `codesign --verify
--deep --strict` and run its executable with the same two phases. This validates
payload preservation without installing a test application system-wide.

For a non-sandbox control, override the target and remove entitlements:

```sh
dotnet "$repo/src/NetWiv.Cli/bin/Release/net8.0/netwiv.dll" bundle --target osx-arm64 --bundles app --no-sign --config '{"bundle":{"macOS":{"entitlements":null}}}'
```

Run the resulting `bundle/macos/NetWiv-Sandbox-Smoke.app` executable with
`--control write` and `--control read`, setting a fresh outside directory as
above. The control expects that outside write to succeed.

For certificate-backed packaging, supply the three `APPLE_APP_STORE_*` variables
documented in the [Store workflow](../../docs/macos.md#mac-app-store-packages)
and repeat `bundle --bundles app-store` without `--no-sign`. Use your own registered
identifier in both `netwiv.conf.json` and `Program.cs`. Do not commit the signing
identities or profile. This fixture deliberately has no production icon or
encryption declaration; adapt those to your application before distribution.
