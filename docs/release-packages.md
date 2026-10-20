# Release package bundles

NetWiv initially distributes experimental packages as a GitHub Release ZIP.
The Release workflow does **not** publish to NuGet.org or GitHub Packages and
does not need a NuGet account, API key or Trusted Publishing policy.
Start with a toy project; full release acceptance is still in progress.

## Download and use a bundle

When the maintainer publishes a Release, download `NetWiv-VERSION-packages.zip`
and `SHA256SUMS` from its assets. Verify the ZIP hash before extracting it
(`sha256sum -c SHA256SUMS` on Linux or `shasum -a 256 -c SHA256SUMS` on macOS;
on Windows use `Get-FileHash -Algorithm SHA256` and compare the hash).
The archive contains:

- `packages/`: Core, Hosting, optional BlazorHybrid, Generators, Build, Desktop, CLI and managed plugin
  `.nupkg` files at the same version. Desktop includes Windows x64, Linux x64 and
  macOS arm64 native libraries.
- License texts, notices and pinned upstream dependency manifests.
- `SOURCE.txt` with the source commit and `SHA256SUMS` for the extracted files.

`NetWiv.BlazorHybrid` is an optional Windows x64, Linux x64 and macOS arm64 integration supporting managed
execution by default and explicitly opted-in experimental NativeAOT Hybrid hosting. Its shared Razor
UI can also use WebAssembly; that build excludes the Hybrid dependency.
Other applications do not acquire the Microsoft
WebView renderer through the ordinary NetWiv packages. See
[Blazor Hybrid](blazor-hybrid.md) for the mode switch and validation limits.

Use the extracted `packages` directory as a local feed. For example in PowerShell:

```powershell
$feed = (Resolve-Path './packages').Path
$version = 'PACKAGE_VERSION' # Replace with the downloaded version.
dotnet tool install --global NetWiv.Cli --version $version --add-source $feed
netwiv new MyNetWivApp --template react --source $feed
```

Or in macOS/Linux shells:

```sh
feed="$(pwd)/packages"
version='PACKAGE_VERSION' # Replace with the downloaded version.
dotnet tool install --global NetWiv.Cli --version "$version" --add-source "$feed"
export PATH="$HOME/.dotnet/tools:$PATH"
netwiv new MyNetWivApp --template react --source "$feed"
```

Keep the feed at that location; generated projects refer to its absolute path.
The CLI's JS/TS starters carry a portable `vendor/netwiv-api` package. Running
`npm ci` in the generated app installs the self-contained `@netwiv/api` package
locally, including its private core/plugin implementations. No separate upstream
frontend dependency or public `@netwiv/api` registry release is required. See [frontend SDK distribution](frontend-api.md#installation-and-migration).
Use `dotnet tool update` if the CLI is already installed. You still need the
.NET 8 or later SDK, your frontend toolchain and platform WebView/NativeAOT prerequisites.
Continue with the repository's
[package quickstart](https://github.com/kim0522ms/NetWiv/blob/main/docs/integration-quickstart.md).

## Maintainer workflow

After merging the workflow and intended code, push a version tag:

```sh
version='NEXT_VERSION' # Replace with a new, unused semantic version.
git tag "v$version"
git push origin "v$version"
```

Only `vMAJOR.MINOR.PATCH` tags with an optional prerelease suffix are accepted;
build metadata and leading zeroes in numeric identifiers are rejected.
Ordinary branch pushes and PRs run CI without creating a Release.

`.github/workflows/release.yml` reuses the existing three-platform CI at the
tagged commit. Each platform builds its own native library and must pass tests
and NativeAOT validation before exporting native inputs. A packaging job then
collects those exact files and runs `scripts/pack-integration.ps1`, including
Hosting, optional BlazorHybrid and every managed plugin. The bundle validator checks package versions,
the expected package set, license notices and all four native payload files
against the CI inputs. Five fresh consumer jobs reuse that single bundle:
Linux x64 with SDK/runtime 8, 9 and 10, plus Windows x64 and macOS arm64 with
SDK/runtime 8. Each has only its selected SDK/runtime major visible in a private
installation. They
install the packaged CLI, publish and run a generated .NET 8 React app with
`netwiv build --framework-dependent` through its
GUI/IPC smoke flow, and verify its actual runtime major. Each job also publishes
a desktop smoke consumer with NativeAOT for its own target framework and executes
windowless startup and configuration binding. The .NET 8 baseline jobs additionally
publish and execute the default managed self-contained React app, checking its
bundled runtime policy and GUI/IPC behavior. The macOS job also bundles and
executes the managed `.app` in both deployment modes through the GUI/IPC smoke flow.
All five consumer jobs also generate and publish a matching-target managed
`blazor-hybrid` starter, then execute a package-only Hybrid fixture in the
ordinary Microsoft managed host and experimental NativeAOT host. The fixture
checks native DOM events, interop, reload and page-scope cleanup. Linux runs
these checks in a nested X11 desktop; native Wayland needs separate validation.
All five must pass before the draft Release job runs.
See [SDK compatibility](dotnet-compatibility.md).

The final job creates a **draft Release**, attaches the ZIP and its SHA-256
checksum, and sets prerelease status for tags such as `v0.1.0-beta.1`.
Review it under GitHub Releases and publish it manually when ready. Rerunning
the workflow can replace assets on a draft; it refuses to modify a published
Release. Use a new version tag for changed code. No Release job runs after a
validation or packaging failure.

A draft is visible to repository users with push access. Once published, a
Release in this public repository is public. Tags, source code, Actions logs
and workflow artifacts in a public repository are not a private distribution
channel; a draft only delays publication of the Release page/assets.

Source and package-consumer CI cover all three supported desktop RIDs. These hosted smoke
checks do not establish physical-device acceptance, Windows/macOS package
consumers on .NET 9/10, the macOS 12 minimum OS, or full installer lifecycle
acceptance on every supported platform. Adding a job does not prove it passed;
inspect the tagged workflow's results before making release claims. These
packages do not provide Developer ID signing or macOS notarization.
