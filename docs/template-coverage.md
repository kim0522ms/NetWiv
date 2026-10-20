# NetWiv starter templates

`netwiv new` creates an application from one of five starters. Each one has the
same C# backend and the same first screen, written in its own framework.

| Frontend | `--template` | Tooling | Screen source |
| --- | --- | --- | --- |
| React + TypeScript | `react` (default) | Vite, npm | `src/App.tsx` |
| Vue + TypeScript | `vue` | Vite, npm | `src/App.vue` |
| Svelte + TypeScript | `svelte` | Vite, npm | `src/App.svelte` |
| Angular | `angular` | Angular CLI, npm | `src/app.ts` |
| Blazor WebAssembly | `blazor` | Selected .NET SDK (8, 9 or 10) | `frontend/Pages/Home.razor` |

The TypeScript starters accept `--manager npm|pnpm|yarn|bun|deno`; Blazor accepts
only `dotnet`. Default npm projects include lockfiles; other package managers
create their own lockfiles on first installation. No starter requires Rust or
Cargo.

Install the CLI and prepare `$feed` using the [quickstart](integration-quickstart.md),
then select a template:

```powershell
netwiv new MyVueApp --template vue --source $feed
netwiv new MyBlazorApp --template blazor --source $feed
```

Every starter generates a C# backend, `NetWiv.*` NuGet references, a `.sln`
solution, configuration and calls through `@netwiv/api`. TypeScript starters
install the portable `file:vendor/netwiv-api` package and generate typed command
bindings during the build. Blazor bundles the standalone NetWiv modules as static
assets and calls the C# `greet` command through a JavaScript module,
`frontend/wwwroot/netwiv.js`. See the [frontend SDK](frontend-api.md) and the
[shared asset guide](../templates/frontends/shared/README.md) for source and
license details.

## The starter screen

The first screen calls the C# `greet` command, shows its answer and the call
count kept in C# state, and lists the files to edit next. It follows the system
light or dark appearance and uses only system fonts and inline assets, so it works
offline from the app's embedded files. It calls only the app's own command, so it
needs no capability. Opened in a plain browser tab through the dev server, a failed
call explains how to open it in the NetWiv window instead of failing silently.

All five screens are generated from one source,
`scripts/generate-frontend-template-overlays.mjs`; change the design there and
regenerate rather than editing one framework by hand. The design is recorded in
[`templates/DESIGN.md`](../templates/DESIGN.md).

Smoke tests start the app with `--smoke-test`, which opens the window with
`?smoke=1`. Every starter then submits its form once and reports
`PASS: <framework> form → generated C# → JSON` through the `smoke_done` command.

## Platforms and validation

The same starters target Windows x64, Linux x64 and macOS arm64 through Wry.
macOS applications require a main-thread `App.Run()` and matching native RID
assets; see [macOS](macos.md).

CI generates every starter on Linux and builds and type-checks the four
TypeScript frontends. The React starter also runs the GUI/IPC smoke test and the
package-consumer checks. The Blazor frontend is generated but not compiled in CI.
GUI acceptance on every platform is not complete for any starter.
Read [support and validation](implementation-status.md).

The separate `NetWiv.Templates` package for direct `dotnet new` use provides only
React; all five starters are included in `NetWiv.Cli` through `netwiv new`.

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
