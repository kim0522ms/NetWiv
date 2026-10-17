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

Both runs must exit zero and print 13 `PASS` assertions. Run them in a logged-in
macOS GUI session. The read phase removes the fixture's files and local-storage
entry; no personal files are read. The OS-owned app container may remain.
Keep the outside directory outside the app's sandbox container. A watchdog
fails the test if WebView execution does not complete.

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
