# Runner and application version compatibility

`build.runner` accepts a program string or an object:

```json
{
  "build": {
    "runner": {
      "cmd": "dotnet",
      "cwd": "..",
      "args": ["--verbosity", "minimal"]
    }
  }
}
```

The default command is `dotnet`, the explicit MSBuild counterpart of Tauri's
default `cargo`. An explicit `--runner` replaces the entire configured runner,
including cwd/args. Relative cwd resolves from the configuration directory,
corresponding to Tauri's backend directory. Optional cwd/args may be absent or
null; cmd is required in an object. Unknown object fields follow upstream's
permissive RunnerConfig parser; malformed known fields fail configuration.

Runner arguments follow the command verb and precede generated arguments.
`build` and AOT dev use `publish`; managed dev uses `run`. Watched managed dev
uses `watch --no-hot-reload --project <project> run`, followed by runner arguments. Strings with
spaces remain single arguments. Configuration reload re-resolves the runner,
and CLI overrides retain precedence. The managed/AOT development child and
build runner exit codes are propagated. Argument/cwd tests launch a real child
through all three CLI paths, including the NativeAOT CLI on Windows/Linux. The macOS CLI suite also exercises
runner, hook, NativeAOT application build and development lifecycle paths; this
is separate from publishing the CLI executable itself as NativeAOT.

`bundle.windows.wix.version` overrides the application version. Otherwise the
pinned Tauri conversion uses numeric build metadata first, then numeric
prerelease, as the optional fourth component: `1.2.3-4` and `1.2.3+4` both become
`1.2.3.4`. Non-numeric or oversized fourth components fail instead of being
silently removed. The limits are 255 for major/minor and 65535 for patch/build.
A semantic beta such as `1.2.3-beta.1` therefore needs an explicit numeric WiX
version. These are the pinned MSI rules; this does not redefine NSIS versions.

Executable resource versions follow the separate `tauri-build` contract: numeric
build metadata supplies the fourth component, while prerelease and nonnumeric
or oversized metadata do not. Thus `1.2.3+42` becomes `1.2.3.42`, but
`1.2.3-beta.1` becomes `1.2.3.0`. All four upstream resource-version tests are
ported alongside the two MSI tests and twelve RunnerConfig tests.

The application `version` may be a semantic version or a path to a JSON file
containing a string `version`, resolved from the configuration directory. The
CLI watches that file; direct MSBuild builds also resolve it before assembly
metadata generation and add it to watch inputs. NativeAOT embeds the resolved
version without a runtime file read. The `native-version.mjs` integration checks
the app metadata and file-version attribute on Windows/Linux, and the actual Windows
PE version fields. Run it from the repository root with
`node tests/frontend-bindings/native-version.mjs`. Installer installation
acceptance remains a separate check. That script selects only Windows/Linux RIDs;
it does not validate macOS metadata. macOS packaging writes the application
version to `CFBundleShortVersionString`, and `bundle.macOS.bundleVersion` (or the
application version when omitted) to `CFBundleVersion`. These are plist strings,
not the Windows MSI/resource-version conversions above.

## Configuration patches and hooks

Platform configuration uses RFC 7396 merge patch. Repeated `--config` inputs
are composed in order while retaining null deletion instructions, then applied
once to the base/platform result. This follows the pinned CLI's `merge_patches`;
it is different from applying each CLI input directly to the configuration.
The composed patch, including null values, is forwarded as `TAURI_CONFIG` to
hooks and build/development runners. Configuration restart refreshes it in both
managed and AOT development; effective configuration remains the MSBuild input.

String or object `beforeBuildCommand` and `beforeBundleCommand` hooks always
finish before publication/bundling continues. `beforeDevCommand` runs in the
background unless its object sets `wait: true`. A background hook that succeeds
does not stop development; one that fails terminates the development runner and
returns the hook's exit code. Cancellation still returns 130 through the CLI.
Default hook cwd is the frontend/project directory, and an explicit relative cwd
resolves from the backend/config directory. Windows shell scripts preserve cmd
quoting, including executable and argument paths containing spaces.

The regression harness launches real hooks and runners, checks composed patch
data, success/failure, ordering, cwd and process cleanup. CLI NativeAOT execution
is distinguished from the deliberately short probe runner: the hook tests do
not pretend the probe itself is a published application.

On macOS, configuration overlays use `macos` in their filenames
(`netwiv.macos.conf.json` and accepted Tauri/JSON5/TOML equivalents), while
capability platform filters use `"macOS"`. Hooks receive
`TAURI_ENV_PLATFORM=darwin`, `TAURI_ENV_FAMILY=unix` and
`TAURI_ENV_ARCH=aarch64` or `x86_64`. The CLI accepts both Apple target triples
and .NET RIDs. Standalone `bundle` must use the same target spelling as the
preceding `build` because that spelling selects the output directory.
See the [macOS workflow](macos.md#application-workflow).
