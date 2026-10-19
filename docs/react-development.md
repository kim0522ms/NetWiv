# React development and generated commands

Create a React project using the [package quickstart](integration-quickstart.md).
From its generated project directory, run:

```sh
npm ci
npm run desktop:dev
```

The project's NuGet sources must contain matching NetWiv SDK and NetWiv.Cli
packages. The generated `.config/dotnet-tools.json` pins the local CLI to the
selected SDK version. Once the project exists, development uses that local tool;
Cargo is not required for the React/C# application.

`npm ci` installs the starter's vendored `@netwiv/api`. Generated commands import
`@netwiv/api/core`; use the same SDK for other desktop frontend calls. See
[frontend SDK distribution and migration](frontend-api.md#local-package-distribution).

`desktop:dev` restores the local tool and runs `netwiv dev -- --dev`. The CLI
executes `beforeDevCommand` (`npm run dev`), waits for Vite at
`http://127.0.0.1:5173/`, and starts the C# application with `dotnet watch`.
The initial frontend build exports commands before Vite starts. Subsequent C#
builds regenerate `src/generated/commands.js` and `commands.d.ts`; Vite observes
these changes. Backend changes restart the application and its managed state.
Use Ctrl+C in the starting terminal to stop the development session.

For separate terminals, run `npm run dev` and `npm run desktop:dev:backend`.
Those processes are then owned by their respective terminals.

`src-netwiv/netwiv.conf.json` contains hooks and frontend paths. The C# sample loads
the embedded effective configuration, so CLI overlays and capabilities reach the
runtime. Its Release build rejects `--dev`. The template preserves the C#
preprocessor guard using the official template engine's
[conditional processing controls](https://github.com/dotnet/templating/wiki/Conditional-processing-and-comment-syntax#ignore-conditions-expressions-in-language-files).

## Automated verification

`tests/frontend-bindings/run.mjs` generates a fresh package-only consumer and
checks the CLI version, TypeScript bindings and managed/NativeAOT React IPC
using an installed frontend SDK tarball.
After that succeeds, run the real development integration against its consumer:

```sh
dotnet run --project tests/NetWiv.Cli.Tests -c Release --disable-build-servers -- --frontend-consumer /absolute/path/to/consumer
```

This test temporarily adds C#/React probes, starts the CLI development path,
checks a native WebView response, renames the C# command, checks regenerated
JS/TS and a response from a new application PID, then cancels the session. It
requires both application PIDs to stop and Vite's port to become available.
The original source files are restored and temporary IPC probe results are deleted.
Generated files are refreshed by the next normal build. The cancellation token
path is exercised directly; this is not a manual keyboard Ctrl+C test.

Linux runs need a display. Build the CLI test executable first and launch it
through `scripts/run-linux-smoke-desktop.sh` with `x11` or `wayland`; the native
app inherits that display. Do not run two nested desktop wrappers concurrently.

On macOS, run in a logged-in desktop session with the matching `osx-arm64` or
`osx-x64` Wry assets. Keep the generated synchronous C# entry point on the process
main thread. The frontend-binding harness above currently hardcodes Windows/Linux
RIDs, so its development restart scenario does not establish macOS acceptance.
The separate release package-consumer harness checks managed React execution
and windowless NativeAOT consumers on both macOS architectures. Source-linked
WKWebView smoke is another separate integration check; see
[macOS setup and validation](macos.md).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
