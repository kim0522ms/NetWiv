# Repository working agreement

Read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming, contributor
responsibility, real-device evidence and PR requirements. These apply to
agent-assisted work as well as manually written contributions.

## Privacy before publication

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

NetWiv is a .NET 8 or later WebView desktop framework with optional NativeAOT publishing with TAURI® v2 compatibility as an important support goal.

The product spelling is `NetWiv`. Public C# namespaces and NuGet IDs use
`NetWiv`, while CLI, native libraries, configuration files and npm identifiers
use lowercase `netwiv`. The unchanged official `@tauri-apps/api` retains its
upstream name.

## Design priorities

- Follow [the design principles](docs/design-principles.md): prioritize idiomatic .NET usability and stability while supporting Tauri frontend compatibility.
- Prioritize idiomatic .NET usability and stability when design goals conflict; consider complexity and long-term maintenance cost.
- Preserve existing public APIs and frontend compatibility where practical. C# APIs, tooling and internals may use independent designs.
- Deliberate deviations are allowed when compatibility imposes disproportionate costs, not only when language differences make it impossible. Document the affected API, platform/version, rationale, alternatives, impact, validation and migration path. Do not silently introduce breaking changes.
- Apply this policy to new or changed areas; it does not authorize wholesale rewrites or blanket relaxation of security, NativeAOT or validation requirements.
- Report practical application readiness separately from Tauri compatibility coverage. Deliberate deviations, unfinished behavior and untested items are not compatibility passes.

## Compatibility source of truth

- `compatibility/upstream-lock.json` pins every upstream version, commit and npm integrity value.
- Generated compatibility files come from `node compatibility/scripts/generate-upstream-manifests.mjs`; never edit them manually.
- Tauri compatibility claims require unchanged `@tauri-apps/api@2.11.1` guest code and matching observable behavior for the claimed scope. Document intentional NetWiv differences separately; do not rewrite generated upstream data to hide them. Direct use of private `window.__TAURI_INTERNALS__` is outside the public guarantee.
- With no app manifest, bundled local pages may call registered application commands by default and remote pages may not. Once any app manifest exists, every custom command requires a resolved capability permission; deny permissions win over allows.

## Milestone boundary

- Desktop baseline: Windows x64 with WebView2, Linux x64 on Ubuntu 24.04/WSLg with GTK3/WebKitGTK 4.1, and macOS 12+ arm64/x64 with WKWebView/AppKit. Apple Silicon has local NativeAOT/GUI evidence; Intel and minimum-OS execution remain unverified. See [macOS support](docs/macos.md).
- Desktop core includes app, core, DPI, events, image, menu, path, resources, tray, window, webview and webview-window behavior.
- The plugin host is in scope; official plugin implementations, mobile targets and Tauri v1 migration are later milestones.
- v1 prioritizes desktop core. The updater runtime remains Not Implemented and is not a v1 completion blocker; existing CLI signer support is separate. Use [the API status table](docs/api-status.md) for user-selected ancillary extensions rather than scheduling them automatically.
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
