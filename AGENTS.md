# Repository working agreement

Read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming, review criteria,
real-device evidence and maintainer PR requirements. These apply to
agent-assisted work as well as manually written changes. They are internal
development criteria, not warranties, service commitments or an acceptance of
additional liability; see [license and support terms](README.md#license).

## Privacy before publication

- Do not implement evidence collection, environment inventories, diagnostic
  archives or validation-report retention/upload in product code, test helpers
  or CI/CD. Do not commit per-run reports, logs or ad-hoc evidence tools.
  Run functional checks and summarize their results and limits in the PR.
  Ordinary CI console output, data consumed by test assertions and distributable
  release artifacts are separate from evidence collection.
- Do not submit information that can identify the user or their personal
  environment in PRs, issues, commits, documentation, logs, screenshots or
  uploaded artifacts. Personal information includes indirect identifiers and
  combinations of environment details, not only names, passwords or secrets.
- Specific PC specifications and device, CPU, GPU or monitor model names are
  not required for validation evidence and must not be included by default.
  Use only the necessary execution type (physical, VM or headless), OS,
  architecture and relevant software versions; minimize even these details.
  Remove hostnames, account names, personal paths, network addresses, serial
  numbers and other identifying details before publication.
- If identifying information might be needed or its sensitivity is unclear,
  withhold it and discuss the exact information, purpose and disclosure scope
  with the user before submission. Permission to test, commit or open a PR is
  not permission to disclose personal environment information.
- Record the resulting agreement in the user's private local environment,
  outside the repository and uploaded artifacts, using a user-approved storage
  location. Record the agreed boundaries rather than copying sensitive values;
  do not put the private agreement in public project documentation. Consult it
  on later runs and seek new agreement before expanding disclosure.
- Review the actual outgoing PR body, complete diff and attached evidence for
  identifying information before publishing. Real-device evidence requirements
  do not override these privacy rules.

NetWiv is a .NET 8 or later WebView desktop framework with optional NativeAOT publishing with a NetWiv-owned desktop frontend API and privately reused TAURI® v2 implementations.

The product spelling is `NetWiv`. Public C# namespaces and NuGet IDs use
`NetWiv`, while CLI, native libraries, configuration files and npm identifiers
use lowercase `netwiv`. Upstream dependency identities remain unchanged inside the private facade.

## Design priorities

- Follow [the design principles](docs/design-principles.md): prioritize idiomatic .NET usability and stability while maintaining the public `@netwiv/api` desktop contract.
- Prioritize idiomatic .NET usability and stability when design goals conflict; consider complexity and long-term maintenance cost.
- Preserve the published NetWiv APIs where practical. C# APIs, tooling and internals may use independent designs.
- Deliberate deviations are allowed when compatibility imposes disproportionate costs, not only when language differences make it impossible. Document the affected API, platform/version, rationale, alternatives, impact, validation and migration path. Do not silently introduce breaking changes.
- Apply this policy to new or changed areas; it does not authorize wholesale rewrites or blanket relaxation of security, NativeAOT or validation requirements.
- Report practical application readiness separately from Tauri compatibility coverage. Deliberate deviations, unfinished behavior and untested items are not compatibility passes.

## Frontend contract and reference sources

- `packages/api` owns the desktop-only `@netwiv/api` public contract. Initially
  it reuses the pinned official package internally; progressively removing that
  frontend dependency is the long-term direction. Exclude mobile-only APIs,
  types and options from the facade. Replacing the native ecosystem/toolchain
  is outside this frontend work scope.
- Keep pinned upstream guests only as private implementation sources and internal
  comparison fixtures. Do not offer direct upstream imports as an application path
  or count facade-only behavior as an upstream compatibility pass.
- `compatibility/upstream-lock.json` pins every upstream version, commit and npm integrity value.
- Generated compatibility files come from `node compatibility/scripts/generate-upstream-manifests.mjs`; never edit them manually.
- `@netwiv/api` is the only supported frontend entry point, including `@netwiv/api/plugins/*`. Direct upstream core/plugin imports and `isTauri`/`isNetWiv` detection are retired. Preserve the internal facade and pinned upstream sources. Upstream comparison fixtures are internal references, not a second supported frontend contract. Never rewrite generated inventories to hide differences. Private `window.__TAURI_INTERNALS__` is outside the supported public API surface.
- With no app manifest, bundled local pages may call registered application commands by default and remote pages may not. Once any app manifest exists, every custom command requires a resolved capability permission; deny permissions win over allows.

## Milestone boundary

- Desktop baseline: Windows x64 with WebView2, Linux x64 on Ubuntu 24.04/WSLg with GTK3/WebKitGTK 4.1, and macOS 12+ arm64 with WKWebView/AppKit. Linux supports X11 and native Wayland; selected physical Wayland checks cover Ubuntu 22.04 GNOME, not physical Ubuntu 24.04 acceptance. macOS hosted NativeAOT checks passed on arm64; physical Apple Silicon has selected GUI evidence, while minimum-OS acceptance remains unverified. See [Linux](docs/linux.md) and [macOS support](docs/macos.md).
- macOS x64 is excluded from official support, CI and release packages. Retain source-build escape hatches only; do not schedule Intel acceptance or restore distribution without an explicit scope change.
- New frontend code, generated bindings and starters use `@netwiv/api`. Its current facade delegates to pinned official Tauri code; unchanged official guests remain the separate compatibility reference. See [frontend SDK](docs/frontend-api.md). Do not replace upstream imports in compatibility tests to make them facade tests.
- Desktop core includes app, core, DPI, events, image, menu, path, resources, tray, window, webview and webview-window behavior.
- The plugin host is in scope; official plugin implementations and Tauri v1 migration are separate milestones.
- Mobile targets, the updater runtime and expansion of distribution notarization are excluded from the project work scope. Do not list them as deferred work, later milestones or release blockers, or propose them again unless the user explicitly changes this scope. Keep their unsupported status visible in compatibility inventories; existing signing and packaging functionality is separate.
- v1 prioritizes desktop core. Use [the API status table](docs/api-status.md) for user-selected ancillary extensions rather than scheduling them automatically.
- `netwiv dev` and `netwiv build` default to the managed runtime; `--aot` opts into NativeAOT. Consumer publishing also supports `--framework-dependent`. Keep framework and release NativeAOT validation alongside managed consumer validation.
- The primary config is `src-netwiv/netwiv.conf.json`; `src-tauri/tauri.conf.json` is accepted as a compatibility input.

## Change discipline

- Keep platform code behind shared contracts. Do not add platform branches to compatibility data or the command model.
- NativeAOT paths must avoid dynamic assembly loading, `Reflection.Emit`, runtime-generated serializers and reflection-based plugin discovery.
- Mark incomplete runtime behavior as unsupported or skipped in conformance reports. Never make a stub return a passing result.
- Run focused tests plus the NativeAOT gate appropriate to the changed platform. State any unavailable host/tool dependency precisely.
- Preserve unrelated work because multiple agents may work in this checkout concurrently.

## Native backend and execution discipline

- The production direction is wry/tao/muda/tray-icon behind a versioned C ABI;
  C# owns the public SDK, IPC policy, source generation and CLI.
- Native Rust is allowed. Ordinary NuGet consumers must not need Cargo; native
  binaries are built explicitly by framework maintainers and delivered per RID.
- Batch Computer Use after implementation and automated validation checkpoints.
  Do not alternate individual edits with repeated manual GUI checks.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY and TAO are trademarks of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
