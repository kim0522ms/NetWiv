# NetWiv starter templates

`netwiv new` creates an application from one of six starters. The first five
share a C# backend and first screen, written in their own framework. The
optional Hybrid starter shares one Razor UI between managed development,
optional experimental NativeAOT Hybrid and WebAssembly deployment.

| Frontend | `--template` | Tooling | Screen source |
| --- | --- | --- | --- |
| React + TypeScript | `react` (default) | Vite, npm | `src/App.tsx` |
| Vue + TypeScript | `vue` | Vite, npm | `src/App.vue` |
| Svelte + TypeScript | `svelte` | Vite, npm | `src/App.svelte` |
| Angular | `angular` | Angular CLI, npm | `src/app.ts` |
| Blazor WebAssembly | `blazor` | Selected .NET SDK (8, 9 or 10) | `frontend/Pages/Home.razor` |
| Blazor Hybrid / WebAssembly | `blazor-hybrid` | .NET SDK; platform WebView prerequisites on Windows x64, Linux x64 or macOS arm64 | `Shared/App.razor` |

The TypeScript starters accept `--manager npm|pnpm|yarn|bun|deno`; Blazor accepts
only `dotnet`. Default npm projects include lockfiles; other package managers
create their own lockfiles on first installation. No starter requires Rust or
Cargo.

Install the CLI and prepare `$feed` using the [quickstart](integration-quickstart.md),
then select a template:

```powershell
netwiv new MyVueApp --template vue --source $feed
netwiv new MyBlazorApp --template blazor --source $feed
netwiv new MyHybridApp -t blazor-hybrid --source $feed
```

Every starter generates a C# backend, `NetWiv.*` NuGet references, a `.sln`
solution, configuration and calls through `@netwiv/api`. TypeScript starters
install the portable `file:vendor/netwiv-api` package and generate typed command
bindings during the build. Blazor adds a `contracts/` project that the app and the
frontend share, and calls C# through `NetWivCommands`, a typed client each app build
generates from the `[Command]` methods (see
[typed C# client](frontend-generator.md#typed-c-client-for-net-frontends)). It bundles
the standalone NetWiv modules as static assets; `frontend/wwwroot/netwiv.js` forwards
the client's calls to them. See the [frontend SDK](frontend-api.md) and the
[shared asset guide](../templates/frontends/shared/README.md) for source and
license details.

The `blazor-hybrid` starter uses `NetWiv.Hosting`, a shared Razor library and
conditional desktop package references. Both `Debug` and `Release` default to
managed Hybrid. WebAssembly can publish the desktop backend with NativeAOT. Override with `--blazor-mode Hybrid|WebAssembly` in the CLI or
`-p:NetWivBlazorMode=Hybrid` / `-p:NetWivBlazorMode=WebAssembly` in MSBuild.
Hybrid NativeAOT remains experimental and requires `--aot --blazor-mode Hybrid
--experimental-blazor-aot` or the `NetWivBlazorExperimentalAot=true` MSBuild property,
plus explicit component, generated JSON and callback registration. See [Blazor Hybrid](blazor-hybrid.md) for its registration
requirements, AOT limitations and trusted-code boundaries.

## The starter screen

The original five starters' first screen calls the C# `greet` command, shows its answer and the call
count kept in C# state, and lists the files to edit next. It follows the system
light or dark appearance and uses only system fonts and inline assets, so it works
offline from the app's embedded files. It calls only the app's own command, so it
needs no capability. Opened in a plain browser tab through the dev server, a failed
call explains how to open it in the NetWiv window instead of failing silently.

Those five screens are generated from one source,
`scripts/generate-frontend-template-overlays.mjs`; change the design there and
regenerate rather than editing one framework by hand. The design is recorded in
[`templates/DESIGN.md`](../templates/DESIGN.md).

Their smoke tests start the app with `--smoke-test`, which opens the window with
`?smoke=1`. Each then submits its form once and reports
`PASS: <framework> form → generated C# → JSON` through the `smoke_done` command.

## Platforms and validation

The original five starters target Windows x64, Linux x64 and macOS arm64 through Wry.
macOS applications require a main-thread `App.Run()` and matching native RID
assets; see [macOS](macos.md).
Hybrid execution currently supports Windows x64 only; its shared WebAssembly
release keeps the ordinary desktop backend. Other platforms' Hybrid execution
is explicitly unsupported, and cross-platform acceptance of that starter is
unverified.

CI generates every starter on Linux and builds and type-checks the four
TypeScript frontends. The React starter also runs the GUI/IPC smoke test and the
package-consumer checks. The Blazor frontend is generated but not compiled in CI.
GUI acceptance on every platform is not complete for any starter.
The product Hybrid adapter has a separate Windows NativeAOT WebView2 CI check;
local acceptance also covers a CLI-generated NuGet Hybrid consumer.
Read [support and validation](implementation-status.md).

The separate `NetWiv.Templates` package for direct `dotnet new` use provides only
React; all six starters are included in `NetWiv.Cli` through `netwiv new`.

## Retired templates

Releases after 0.2.3 removed the JavaScript variants (`vanilla`, `vue`, `svelte`,
`react` as JavaScript, `solid`, `preact`), the separate `-ts` names, and the
Rust/WASM frontends (`yew`, `leptos`, `sycamore`, `dioxus`). The template names
now mean the TypeScript starters above. Existing projects are unaffected; they
are ordinary Vite, Angular or Blazor projects and keep building as they are.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
