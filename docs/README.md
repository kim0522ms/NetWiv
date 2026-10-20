# NetWiv documentation

Guides and references for building applications with NetWiv and extending the
framework. Public documentation is maintained in English.

Official website: [netwiv.github.io](https://netwiv.github.io/).

## Build an application

| Guide | Topics |
| --- | --- |
| [Deployment](deployment.md) | Self-contained .NET, framework-dependent and optional NativeAOT publishing |
| [Quickstart](integration-quickstart.md) | Prepare packages, create a React app, run it and publish |
| [Release bundles](release-packages.md) | Draft Releases, downloadable package feeds and checksums without NuGet.org publication |
| [.NET compatibility](dotnet-compatibility.md) | .NET 8 baseline, SDK/runtime 9 and 10, packages and validation limits |
| [Linux](linux.md) | GTK/WebKit prerequisites, X11 and native Wayland, compositor and plugin limits |
| [macOS](macos.md) | Mac prerequisites, main-thread startup, app/DMG bundles and signing limits |
| [Project creation](project-creation.md) | CLI options, solution structure and development/build workflows |
| [Frontend templates](template-coverage.md) | Six choices and their toolchain requirements |
| [React development](react-development.md) | Development server, C# restarts and binding updates |
| [Blazor Hybrid](blazor-hybrid.md) | Optional managed Windows development host, shared Razor UI and WebAssembly deployment |

## APIs and security

| Reference | Topics |
| --- | --- |
| [C# SDK](csharp-api.md) | Commands, state, events, ownership and lifecycle |
| [Generic Host](hosting.md) | Dependency injection, configuration, options, logging and hosted services |
| [Frontend bindings](frontend-generator.md) | JS/TS generation and serialization support |
| [Desktop frontend SDK](frontend-api.md) | Single core/plugin entry point, desktop exclusions and migration from Tauri imports |
| [Menus](menu-csharp-api.md) | Icons, menu items and About metadata |
| [Plugin configuration](plugin-configuration.md) | Settings, registration and capability permissions |
| [Desktop plugins](official-plugins.md) | Installation, contracts and feature-specific limits |
| [Bundle resources](bundle-resources.md) | File staging and runtime paths |
| [Custom URI protocols](uri-scheme-protocols.md) | Synchronous and asynchronous protocol handlers |
| [Isolation and IPC](isolation.md) | Isolation pattern, transports and validation |
| [Remote URL patterns](remote-url-pattern.md) | URL matching for remote capabilities |
| [Runners, versions and hooks](cli-runner-and-version.md) | CLI configuration, version conversion and execution order |

## Support and framework development

| Reference | Topics |
| --- | --- |
| [Design principles](design-principles.md) | .NET usability and the NetWiv frontend contract |
| [Support and validation](implementation-status.md) | Platforms, distribution and unverified areas |
| [API status](api-status.md) | Implementation and comparison coverage for the pinned public API inventory |
| [Platform behavior](pinned-upstream-behavior.md) | Upstream constraints and deliberate C# differences |
| [Native architecture](native-architecture.md) | Responsibilities of .NET and Rust |
| [C ABI](native-abi.md) | Memory, threads, callbacks and shutdown |
| [Framework development](development.md) | Source builds, tests and package creation |
| [Upstream tests](upstream-business-tests.md) | Test inventory generation, ports and exclusion criteria |

Implementation and completed validation are different states. Read each feature's
platform limits together with its status; unverified behavior is not a
compatibility pass.

## Documentation conventions

Public documents explain usage, contracts, design rationale and limits without
requiring earlier conversations or a personal development environment. Examples
identify required inputs and working directories. Validation instructions refer
to repository tests and commands. Do not commit personal paths, work journals,
per-run reports or execution logs, or add evidence collection to code or CI/CD.
Unimplemented and unverified behavior must remain visible in public documents.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
