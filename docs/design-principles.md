# NetWiv design principles and TAURI® compatibility policy

These principles guide NetWiv APIs, development tools and runtime design.
NetWiv prioritizes .NET developer experience and stability while supporting
Tauri frontend compatibility.

## Core principles

NetWiv aims to be a WebView desktop framework that feels natural in .NET.
Tauri compatibility is an important support goal, but it does not dictate every
implementation decision. When goals conflict, prioritize .NET usability and
stability while considering complexity and long-term maintenance cost.

## Application by area

- **Frontend SDK:** Own the desktop public contract through `@netwiv/api`,
  maintained in `packages/api` in this repository. Its initial facade reuses
  the pinned official `@tauri-apps/api` internally; frontend independence is
  a long-term goal, not a claim about the initial package. Replace dependencies
  incrementally while preserving the published NetWiv contract or documenting
  a versioned migration.
- **Frontend compatibility:** Preserve existing Tauri public JavaScript APIs
  and observable behavior where practical through the unchanged official
  `@tauri-apps/api` compatibility path. Test that path separately from the
  NetWiv facade; the facade must not redefine what counts as Tauri compatibility.
- **C# SDK:** Provide corresponding concepts and functionality through idiomatic
  .NET APIs. Reproducing Rust syntax or expression patterns is not a goal.
- **Development tools:** Generators, NuGet, MSBuild, templates, solutions and IDE
  debugging may use independent designs suited to .NET development.
- **Internals:** Implementations may differ while preserving public contracts.
  Reproducing Tauri's internal structure is not required.

## When compatibility conflicts with an independent design

NetWiv may choose a different design when compatibility would impose
substantial complexity or maintenance cost, even when matching upstream is
technically possible. An unfinished implementation or a failing test alone does
not justify declaring an intentional difference.

Document deliberate differences in the relevant API or design reference:

- The observable difference from Tauri and affected platforms/versions.
- The rationale and compatible alternatives considered.
- The impact on existing applications and validation results.
- A migration path or replacement API for breaking changes.

Do not introduce breaking changes silently. Explain the impact and migration
steps, preserving public APIs and frontend compatibility where practical.

## Validation and reporting

Pinned upstream versions and comparison tests remain the basis for compatibility
assessment. Distinguish deliberate differences, unimplemented behavior,
unverified behavior and passing comparisons. Deliberate differences and unrun
cases do not count as Tauri compatibility passes. Do not rewrite generated
upstream inventories to hide differences or retroactively change test evidence.

Report practical application readiness separately from complete Tauri API
coverage. These principles do not relax security, NativeAOT or testing standards.

## Scope

v1 focuses on desktop core functionality. Mobile targets, the updater runtime
and expansion of distribution notarization are excluded from the project work
scope, not deferred milestones or release blockers. They must not be scheduled
unless the maintainer explicitly changes this scope. Unsupported APIs remain
visible in compatibility inventories. Existing CLI signing and packaging
support is separate. The [API status table](api-status.md) describes support and
limits for ancillary extensions.

Mobile-only APIs are excluded from the NetWiv frontend facade, including its
public types and options. Their presence in the upstream compatibility inventory
does not make them part of the NetWiv SDK contract. The frontend independence
work does not include replacing Wry, Tao, Muda, Tray-icon or the native ecosystem
toolchain. Package ownership does not require a separate repository.

These principles apply to new features and changes to existing functionality.
Changes to public APIs, supported platforms and distribution formats require
individual impact analysis and migration guidance.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
