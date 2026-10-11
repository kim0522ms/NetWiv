<div align="center">

# NetWiv

**Web frontends. C# backends. Native desktop apps.**

A .NET 8+ desktop framework built on OS WebViews, with optional NativeAOT publishing.

![.NET 8+](https://img.shields.io/badge/.NET-8%2B-512BD4?style=flat-square)
![NativeAOT](https://img.shields.io/badge/NativeAOT-0078D4?style=flat-square)
![Windows, Linux and macOS](https://img.shields.io/badge/Windows%20%7C%20Linux%20%7C%20macOS-desktop-334155?style=flat-square)

[Quick start](#quick-start) · [Architecture](#architecture) · [Documentation](docs/README.md) · [Status](#current-status) · [Development](#development)

</div>

---

Write application commands, state, security policy and plugins in **C#**, then
connect them to your web frontend through generated JavaScript and TypeScript
bindings. NetWiv uses **wry, tao, muda and tray-icon** behind a native adapter
for OS WebViews, windows, menus and tray integration.

NetWiv is meant for teams whose backend is already .NET, and for developers who
want to write their desktop backend in C#. It is not a replacement for the
TAURI® framework or the Rust language; it offers another backend language on
top of the TAURI frontend ecosystem.

NetWiv is developed and maintained using AI coding agents, including **Codex**
and **Claude Code**. The project began with the maintainer's personal interest
in loop harnesses that put different frontier models in adversarial roles,
challenging one another's implementations and assumptions through repeated
review and revision.

> [!IMPORTANT]
> NetWiv is still immature and under active development. If you would like to
> try it, we recommend starting with a toy project. Packages are currently
> supplied as local feeds; [Release bundles](docs/release-packages.md) are prepared
> as drafts for maintainer review, without publishing to NuGet.org. Full desktop API, security and release acceptance
> are still in progress; full TAURI framework compatibility is not claimed.

## At a glance

| | What you work with |
| :--- | :--- |
| **C# application layer** | A .NET SDK for commands, shared state, permissions and plugins. |
| **Frontend choice** | 18 template choices, including React, Vue, Svelte and Blazor. |
| **Generated bindings** | JavaScript calls and TypeScript declarations generated from C# commands. |
| **Flexible deployment** | Self-contained .NET by default, installed-runtime deployment or optional NativeAOT. [Deployment modes](docs/deployment.md). |
| **TAURI frontend support** | Compatibility work targets unchanged `@tauri-apps/api@2.11.1` and TAURI framework 2.11.5 behavior. |

Ordinary JS/TS and Blazor application development does not require Cargo.
Framework maintainers build the native adapter separately; Rust/WASM frontend
templates require their own Rust toolchain.

NetWiv prioritizes idiomatic .NET usability and stability. TAURI frontend compatibility
remains an important support goal, with deliberate differences documented and
breaking changes accompanied by impact and migration notes. Read the
[design principles](docs/design-principles.md).

## Relationship to the TAURI framework

**Inspired by the [TAURI framework](https://github.com/tauri-apps/tauri), built on its ecosystem.** NetWiv borrows heavily from
the TAURI approach to desktop apps and reuses many of the components that power
it: **[wry](https://github.com/tauri-apps/wry)** for WebViews,
**[tao](https://github.com/tauri-apps/tao)** for windows and the event loop,
**[muda](https://github.com/tauri-apps/muda)** for menus,
**[tray-icon](https://github.com/tauri-apps/tray-icon)** for system tray integration,
and **[tauri-utils](https://github.com/tauri-apps/tauri/tree/dev/crates/tauri-utils)**
for shared configuration types. It also targets the unchanged official
**[@tauri-apps/api](https://github.com/tauri-apps/tauri/tree/dev/packages/api)**
frontend package. NetWiv builds on this upstream work while implementing its
application SDK, IPC policy and development tooling in .NET. Some source is
ported or adapted from the TAURI framework; NetWiv is an independent modified
implementation, not an official TAURI core runtime. See the
[runtime identity differences](docs/implementation-status.md#runtime-identity-differences).
Credit belongs to
the TAURI framework contributors and the maintainers of these libraries; their original
licenses and notices are retained. See the [pinned upstream sources](compatibility/README.md)
and [native dependencies](native/netwiv-native/Cargo.toml).

### Reporting issues

**Report NetWiv issues to [NetWiv](https://github.com/kim0522ms/NetWiv/issues), not to the TAURI project.**
NetWiv deliberately reproduces the TAURI frontend API and many of its error
messages so that existing frontend code keeps working. A matching error message
therefore does not mean the problem is in the TAURI framework. If you suspect an upstream
issue, reproduce it in an app built with the official TAURI framework before reporting it there.

## Architecture

The runtime stack connects your web UI to C# application code and the native
desktop through a versioned C ABI.

```mermaid
flowchart TB
    subgraph frontend["Web frontend"]
        UI["Your UI · React / Vue / Svelte / Blazor / …"]
        Guest["Generated JS/TS bindings · @tauri-apps/api"]
        UI <--> Guest
    end

    subgraph managed[".NET 8+ · managed or NativeAOT"]
        Core["NetWiv.Core<br/>IPC · permissions · events · resources"]
        App["Your C# commands · state · plugins"]
        Adapter["NetWiv.Platform.Desktop"]
        Core <--> App
        Core <--> Adapter
    end

    subgraph native["Native Rust adapter"]
        Runtime["netwiv-native<br/>wry · tao · muda · tray-icon · tauri-utils"]
    end

    subgraph platform["Operating system"]
        Windows["Windows x64<br/>WebView2"]
        Linux["Linux x64<br/>GTK3 · WebKitGTK 4.1"]
        Mac["macOS arm64 / x64<br/>WKWebView · AppKit"]
    end

    Guest <-->|IPC / events| Core
    Adapter <-->|Versioned C ABI| Runtime
    Runtime --> Windows
    Runtime --> Linux
    Runtime --> Mac

    classDef web fill:#eff6ff,stroke:#2563eb,color:#1e3a8a
    classDef dotnet fill:#f5f3ff,stroke:#7c3aed,color:#4c1d95
    classDef rust fill:#fff7ed,stroke:#ea580c,color:#7c2d12
    classDef os fill:#f8fafc,stroke:#64748b,color:#0f172a
    class UI,Guest web
    class Core,App,Adapter dotnet
    class Runtime rust
    class Windows,Linux,Mac os
```

IPC arrows show logical communication; the native WebView adapter carries the
messages between JavaScript and .NET. C# owns command validation and permission
policy; the native adapter owns the OS event loop, windows, WebViews, menus and
tray objects. At build time, the CLI, source generators and MSBuild produce
bindings, embed frontend assets and publish the application.

## Quick start

Start with the [local package quickstart](docs/integration-quickstart.md) for
the current feed and prerequisites. The example below uses PowerShell and
a supplied package feed; replace both the feed path and `PACKAGE_VERSION`
with actual values before running it. A fresh clone contains no prebuilt feed;
maintainers can [create one from source](docs/development.md#create-a-local-package-feed).

This backport targets `net8.0` and can be consumed by .NET 8, 9 and 10 applications.
The existing `0.1.0-beta.1` bundle still requires .NET 10; use packages built from
this branch for .NET 8/9. See [SDK compatibility](docs/dotnet-compatibility.md)
for SDK selection, runtime behavior and validation limits.

**Windows prerequisites:** .NET 8 or later SDK, Node.js and WebView2 Runtime.
NativeAOT release builds also need Visual Studio C++ Build Tools and Windows SDK.
On macOS 12+, use .NET 8 or later and Node.js; WKWebView is provided by the OS.
NativeAOT builds additionally require Xcode Command Line Tools. Follow the [macOS guide](docs/macos.md) and the
[zsh quickstart](docs/integration-quickstart.md#create-the-project).

```powershell
$feed = 'C:\path\to\packages'
$version = 'PACKAGE_VERSION'

dotnet tool install --global NetWiv.Cli --version $version --add-source $feed
netwiv new MyApp --template react --source $feed
Set-Location MyApp

npm ci
npm run desktop:dev
```

If the global tool is already installed, use `update` instead of `install`.
The generated project includes matching SDK references, frontend bindings and a
local CLI manifest. `desktop:dev` handles binding generation, Vite and the app
process, including frontend updates and C# backend restarts.

Explore the [template matrix](docs/template-coverage.md) or the
[project creation options](docs/project-creation.md#creation-options).

## Current status

Desktop targets are **Windows x64**, **Linux x64 on Ubuntu 24.04 / WSLg**, and
**macOS arm64/x64**. See the [macOS guide](docs/macos.md) for setup and limits.

| Platform | WebView stack | Selected integration checks |
| :--- | :--- | :--- |
| Windows x64 | WebView2 | React + C# NativeAOT app; NSIS/MSI installation, execution and removal. |
| Linux x64 | GTK3 + WebKitGTK 4.1 | React + C# NativeAOT app running under X11. |
| macOS arm64 | WKWebView + AppKit | NativeAOT IPC, window/menu/tray and isolation smoke; local app/DMG packaging. Intel execution and trusted distribution remain unverified. |

These are focused application checks. Real-certificate signing, WebView2-absent
installation and full upstream conformance remain acceptance gaps. Linux X11
checks do not establish Wayland coverage, and not every optional plugin's
native behavior is validated. See the
[support and validation guide](docs/implementation-status.md)
for coverage and limits, and the [per-API status table](docs/api-status.md) for
implemented, planned and unimplemented behavior.

## Documentation

Browse the [documentation index](docs/README.md) for getting-started guides,
API references, and framework development documentation.

| Start here | Reference |
| :--- | :--- |
| **Build your first app** | [Package quickstart](docs/integration-quickstart.md) · [Templates](docs/template-coverage.md) |
| **Connect C# and the frontend** | [C# API](docs/csharp-api.md) · [Generated bindings](docs/frontend-generator.md) |
| **Add plugins** | [Plugin configuration](docs/plugin-configuration.md) · [Plugin status](docs/official-plugins.md) |
| **Understand support** | [API status](docs/api-status.md) · [Support and validation](docs/implementation-status.md) |
| **Understand the design** | [Design principles](docs/design-principles.md) |
| **Work on the framework** | [Native ABI](docs/native-abi.md) · [Native architecture](docs/native-architecture.md) · [Version pins](compatibility/README.md) · [Conformance](tests/conformance/README.md) |

## Development

See the [framework development guide](docs/development.md) for prerequisites
and package creation. Build and run managed checks with .NET 8 or later:

```sh
dotnet build NetWiv.sln -c Release
dotnet run --project tests/NetWiv.Core.Tests -c Release --no-build
dotnet run --project tests/NetWiv.Cli.Tests -c Release --no-build
dotnet test tests/NetWiv.Generators.Tests -c Release --no-build
dotnet test tests/NetWiv.Api.Tests -c Release --no-build
node --test tests/conformance/scripts/compare-results.test.mjs
```

<details>
<summary><strong>Native adapter builds and desktop smoke checks</strong></summary>

Framework maintainers build the Rust adapter explicitly using
`scripts/build-native.ps1` on Windows or `bash scripts/build-native.sh` on Linux/macOS.
Rust 1.95.0, the platform C/C++ linker and the OS WebView development dependencies
are required for that source build. Linux uses GTK3 and WebKitGTK 4.1.

The integrated native sample is `tests/NetWiv.Desktop.Smoke`. Publish it with:

```sh
dotnet publish tests/NetWiv.Desktop.Smoke -c Release -r win-x64
# On Linux, use -r linux-x64; on macOS, use -r osx-arm64 or osx-x64.
```

Run the published executable to test two windows, three webviews, official JS
invoke/Channel, menus, images, tray resources and shutdown. Set
`NETWIV_SMOKE_HOLD_MS=120000` to keep its integrated UI available for inspection.
A successful build alone is not a successful GUI test.

The missing-library, missing-export and wrong-ABI startup tests use deliberately
incompatible fixture libraries in isolated artifact directories:

```powershell
./scripts/test-native-startup.ps1 -PublishedDirectory artifacts/wry-smoke/win-x64
```

On Linux/macOS run `bash scripts/test-native-startup.sh /absolute/smoke/publish`.
These maintainer tests require the pinned Rust compiler and never launch a GUI.

NuGet delivery includes RID-specific native libraries. Ordinary application
restore/build/publish will not invoke Cargo or download libraries at runtime.
Packing the framework requires native artifacts for all four supported RIDs to exist.

</details>

## License

NetWiv is licensed under [Apache-2.0](LICENSE-APACHE).
Copyright © 2026 kim0522ms and NetWiv contributors.

The TAURI framework and other third-party components retain their original licenses and
copyright notices. See [NOTICE](NOTICE), [third-party notices](THIRD-PARTY-NOTICES.md)
and the preserved [TAURI MIT license](licenses/Tauri-MIT.txt).

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
