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

- `packages/`: Core, Hosting, Generators, Build, Desktop, CLI and managed plugin
  `.nupkg` files at the same version. Desktop includes Windows x64, Linux x64,
  macOS arm64 and macOS x64 native libraries.
- License texts, notices and pinned upstream dependency manifests.
- `SOURCE.txt` with the source commit and `SHA256SUMS` for the extracted files.

Use the extracted `packages` directory as a local feed. For example in PowerShell:

```powershell
$feed = (Resolve-Path './packages').Path
$version = '0.1.0-beta.1' # Replace with the downloaded version.
dotnet tool install --global NetWiv.Cli --version $version --add-source $feed
netwiv new MyNetWivApp --template react --source $feed
```

Or in macOS/Linux shells:

```sh
feed="$(pwd)/packages"
version='0.1.0-beta.1' # Replace with the downloaded version.
dotnet tool install --global NetWiv.Cli --version "$version" --add-source "$feed"
export PATH="$HOME/.dotnet/tools:$PATH"
netwiv new MyNetWivApp --template react --source "$feed"
```

Keep the feed at that location; generated projects refer to its absolute path.
Use `dotnet tool update` if the CLI is already installed. You still need the
.NET 8 or later SDK, your frontend toolchain and platform WebView/NativeAOT prerequisites.
Continue with the repository's
[package quickstart](https://github.com/kim0522ms/NetWiv/blob/main/docs/integration-quickstart.md).

## Maintainer workflow

After merging the workflow and intended code, push a version tag:

```sh
git tag v0.1.0-beta.1
git push origin v0.1.0-beta.1
```

Only `vMAJOR.MINOR.PATCH` tags with an optional prerelease suffix are accepted;
build metadata and leading zeroes in numeric identifiers are rejected.
Ordinary branch pushes and PRs run CI without creating a Release.

`.github/workflows/release.yml` reuses the existing four-platform CI at the
tagged commit. Each platform builds its own native library and must pass tests
and NativeAOT validation before exporting native inputs. A packaging job then
collects those exact files and runs `scripts/pack-integration.ps1`, including
Hosting and every managed plugin. The bundle validator checks package versions,
the expected package set, license notices and all five native payload files
against the CI inputs. A fresh consumer installs the packaged CLI, creates an
app, publishes the desktop smoke from package references with NativeAOT, and
executes its windowless startup check on Linux.

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

Source CI covers all four platforms. The additional package-consumer check is
Linux-only; full package-consumer GUI testing and installer lifecycle checks
on every supported platform remain separate release acceptance work. These
packages do not provide Developer ID signing or macOS notarization.
