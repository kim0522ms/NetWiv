# Bundle resources

`bundle.resources` copies files into the application's runtime resource directory.
Paths are relative to the directory containing `netwiv.conf.json` (or the accepted
`tauri.conf.json`), including when `--config` supplies an overlay. The frontend
distribution remains embedded separately. Unbundled builds place resources beside
the executable. A macOS `.app` places them in `Contents/Resources`, while the
executable and dylibs live in `Contents/MacOS`. Use the resource APIs below instead
of deriving a path from the executable directory; see [macOS bundles](macos.md#bundles-and-signing).

```json
{
  "bundle": {
    "resources": {
      "../data/defaults.json": "data/defaults.json",
      "../documents": "documents",
      "../translations/*.json": "translations"
    }
  }
}
```

A mapped file has an exact destination. A mapped directory preserves the tree
under that directory. A mapped glob flattens matched files into its destination.
The array form (`["../data"]`) preserves the source hierarchy, encoding each parent
component as `_up_` and an absolute root as `_root_`, as in the pinned TAURI® mapper.
Globs activate when the source contains `*`; `**` descends through directories.
A literal mapped filename ending in `/` is still an exact filename, matching
upstream's behavior; use a directory source or an explicit target filename.

From the NetWiv frontend SDK:

```ts
import { resolveResource } from '@netwiv/api/path';
const defaultsPath = await resolveResource('data/defaults.json');
```

In C#, use `await PathApi.ResolveResourceAsync(appHandle, "data/defaults.json")`
and read that path with `System.IO`. The JavaScript path API resolves names; it
does not grant filesystem read access. Frontend file reads require a filesystem
plugin and appropriate capabilities; see the [plugin guide](official-plugins.md).

`NetWiv.Build` stages resources for managed builds, managed/NativeAOT publishing and
`publish --no-build`. The CLI also refreshes resources before standalone bundling.
Development watchers include selected external resource paths and newly matched
glob files. Removed or remapped resources are removed from the corresponding
output; unrelated output files are retained. Staging ownership manifests live in
the backend's `obj/netwiv/resource-manifests`, outside the distributable payload.
Missing inputs and invalid globs fail builds with `DTOXBUILD004`. Staging rejects
destinations escaping the output or crossing a destination symbolic link.

## External executable binaries

`bundle.externalBin` packages explicitly selected helper executables. Entries are
extensionless paths relative to the configuration directory, for example
`["binaries/helper"]`. Supply the matching target-specific source:

| Target | Source for `binaries/helper` | Installed name |
| --- | --- | --- |
| Windows x64 | `binaries/helper-x86_64-pc-windows-msvc.exe` | `helper.exe` |
| Linux x64 | `binaries/helper-x86_64-unknown-linux-gnu` | `helper` |
| macOS arm64 | `binaries/helper-aarch64-apple-darwin` | `helper` |

The CLI stages these files beside the application executable in an unbundled
publish output and Windows/Linux packages. macOS bundles put them under
`Contents/MacOS` and sign them as executable code. Unix sources must already
have an executable permission bit. Target aliases such as `win-x64` use the
same canonical source suffix shown above. CLI development stages helpers before
starting the app, including after an ordinary source rebuild. Direct MSBuild
builds/publication do not stage them unless invoked through CLI development.

Absolute paths, traversal, malformed entries, source links and duplicate output
basenames fail. Windows reserves `uninstall.exe` for the installer. A helper cannot replace the main executable or a differing
existing payload file. Matching regular files are preserved without rewriting;
on Unix a matching existing file must already be executable. If a helper source
changes after `build --no-bundle`, rebuild the publish output before bundling it.
Sidecar execution also requires an explicit Shell capability; see
[the plugin guide](official-plugins.md).

Development preserves an unchanged existing helper. A changed helper that
collides with the previous output fails with instructions to remove the old
generated copy and restart; the CLI does not delete or overwrite it. Changes to
helper configuration or binary contents do not have dedicated hot reload support.
Helpers are not added to MSBuild `FileWrites`: an identical pre-existing file is
not assumed to belong to this build, so cleanup remains explicit.

The public `NetWiv.Build.Packaging.ExternalBinaryStager` build API validates
configuration with `ValidateConfiguration(JsonObject)`, resolves a copy plan with
`Resolve(ConfigDocument, BuildTarget, applicationFileName)`, and applies it with
`Stage(plan, outputDirectory)`. Each `ExternalBinary` record carries `Source`,
`DestinationName` and `SourceRoot`. Resolution performs no writes; staging checks
source links, destination collisions and Unix executable permissions again.
Windows and macOS basenames compare without case sensitivity, even when a source
filesystem distinguishes case. No source or differing output file is overwritten.

Source-linked integration: `node tests/frontend-bindings/resources.mjs` verifies
managed output, NativeAOT execution, resource changes/removal, overlay resolution
and a failing missing-input build on Windows/Linux. That script currently selects
only `win-x64` or `linux-x64`; it is not a macOS verification command. On macOS,
`dotnet run --project tests/NetWiv.Cli.Tests -c Release -- --filter macOS`
checks real app/DMG resource staging and signing with native tools. It does not verify
installer installation or GUI interaction. The original six mapper tests and
additional staging/watch regressions run with
`dotnet run --project tests/NetWiv.Cli.Tests -c Release -- --filter resource`.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
