# NetWiv frontend templates

`netwiv new --list` provides 18 frontend choices corresponding to the official
`create-tauri-app` **4.7.3** selector. The reference is
[`create-tauri-app-v4.7.3`](https://github.com/tauri-apps/create-tauri-app/tree/create-tauri-app-v4.7.3)
(`e19fd21a802b995cae8d15e54b89772308af7efa`). TAURI®'s `tauri init` and the frontend
selector `create-tauri-app` are separate tools. NetWiv provides these workflows
through `netwiv init` and `netwiv new`.

| Frontend | `--template` | Default tooling |
| --- | --- | --- |
| Vanilla JS / TS | `vanilla`, `vanilla-ts` | npm |
| Vue JS / TS | `vue`, `vue-ts` | npm |
| Svelte JS / TS | `svelte`, `svelte-ts` | npm |
| React JS / TS | `react`, `react-ts` | npm |
| Solid JS / TS | `solid`, `solid-ts` | npm |
| Angular TS | `angular` | npm |
| Preact JS / TS | `preact`, `preact-ts` | npm |
| Yew, Leptos, Sycamore, Dioxus | `yew`, `leptos`, `sycamore`, `dioxus` | Cargo + Trunk |
| Blazor WebAssembly | `blazor` | Selected .NET SDK (8, 9 or 10) |

JS/TS templates accept `--manager npm|pnpm|yarn|bun|deno`. Rust/WASM accepts only
`cargo`, and Blazor only `dotnet`. Default npm templates include lockfiles;
other package managers create their own lockfiles on first installation.

Install the CLI and prepare `$feed` using the [quickstart](integration-quickstart.md),
then select a template:

```powershell
netwiv new MyVueApp --template vue-ts --source $feed
netwiv new MyBlazorApp --template blazor --source $feed
netwiv new MyYewApp --template yew --source $feed
```

Every choice generates a C# backend, `NetWiv.*` NuGet references, a `.sln`
solution, configuration and calls through `@netwiv/api`. JS/TS templates install
the portable `file:vendor/netwiv-api` package and generate command bindings during
the build. Blazor and Rust/WASM bundle a standalone NetWiv core facade plus its
pinned official API dependencies as static assets, invoking the C# `greet`
command through JS interop. See the [frontend SDK](frontend-api.md) and
[shared asset guide](../templates/frontends/shared/README.md)
for source and license details.

NetWiv matches the frontend **choice set**, but does not reproduce every
upstream tool or version. Svelte uses Vite and Svelte instead of SvelteKit;
Dioxus uses Trunk instead of `dx`. Angular uses 21.2, compatible with Node 24.14,
rather than the reference selector's Angular 22. Ordinary JS/TS and Blazor .NET
backends do not require Cargo. Rust/WASM frontends require the
`wasm32-unknown-unknown` target and Trunk.

The same template choices target Windows x64, Linux x64 and macOS arm64/x64
through Wry. macOS applications require a main-thread `App.Run()` and matching
native RID assets; see [macOS](macos.md). Availability of a template does not mean
its frontend/toolchain has completed macOS package-only or GUI acceptance.

Scaffolding, frontend builds, NativeAOT execution and GUI interaction are separate
validation stages. Actual GUI validation is not complete for every frontend and
package-manager combination. Read [support and validation](implementation-status.md).
The separate `NetWiv.Templates` package for direct `dotnet new` use provides only
React; all 18 choices are included in `NetWiv.Cli` through `netwiv new`.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
