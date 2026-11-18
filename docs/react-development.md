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
[frontend SDK distribution and migration](frontend-api.md#installation-and-migration).

`desktop:dev` restores the local tool and runs `netwiv dev -- --dev`. The CLI
executes `beforeDevCommand` (`npm run dev`), waits for Vite at
`http://127.0.0.1:5173/`, and starts the C# application with `dotnet watch`.
The initial frontend build exports commands before Vite starts. Subsequent C#
builds regenerate `src/generated/commands.js` and `commands.d.ts`; Vite observes
these changes. Backend changes restart the application and its managed state.
Use Ctrl+C in the starting terminal to stop the development session.

To enable managed Debug Hot Reload after restoring the local CLI, run:

```sh
dotnet tool run netwiv -- dev --hot-reload -- --dev
```

Supported C# edits, such as a command method body change, take effect on the
next call without restarting the app or reloading its WebView. Edits that
`dotnet watch` cannot apply in place trigger a rebuild and restart. Changes to
NetWiv configuration or permissions also restart the app.

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
checks a native WebView response, edits the C# command body, and verifies the
new response with the same application PID, WebView document and React state.
It then renames the C# method and command, checks regenerated JS/TS and a
response from a new application PID, and cancels the session. It
requires both application PIDs to stop and Vite's port to become available.
The original source files are restored and temporary IPC probe results are deleted.
Generated files are refreshed by the next normal build. The cancellation token
path is exercised directly; this is not a manual keyboard Ctrl+C test.

Linux runs need a display. Build the CLI test executable first and launch it
through `scripts/run-linux-smoke-desktop.sh` with `x11` or `wayland`; the native
app inherits that display. Do not run two nested desktop wrappers concurrently.

On macOS, run in a logged-in desktop session with the matching `osx-arm64`
Wry assets. Pass the consumer's physical path (use `pwd -P` from its root),
since the .NET watcher can miss managed edits through symlinked paths. Keep
the generated synchronous C# entry point on the process main thread. The
frontend-binding harness above currently hardcodes Windows/Linux RIDs; for
the development test on macOS, use a React starter from a matching local
package feed and run `npm ci` in the generated project first.
The separate release package-consumer harness checks managed React execution
and windowless NativeAOT consumers on macOS arm64. Source-linked
WKWebView smoke is another separate integration check; see
[macOS setup and validation](macos.md).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
