# TAURI® compatibility data

Tauri is the comparison baseline, not a requirement to copy every implementation
decision. Follow the [design principles](../docs/design-principles.md) when
choosing deliberate NetWiv differences. Record them separately from matching,
unimplemented and untested behavior; they must not be counted as compatibility
passes or hidden by editing generated upstream inventories.

`upstream-lock.json` is the sole version input for the `tauri-v2.11` profile. Run:

```sh
node compatibility/scripts/generate-upstream-manifests.mjs
```

The generator downloads immutable upstream inputs, verifies both npm SHA-512 integrity values and the CLI schema SHA-256, then writes `generated/*.json` and the exact CLI `config.schema.json`. Generated files must not be edited by hand.

An upstream upgrade is one reviewable lock-file change followed by regeneration and the differential conformance suite. A matching major or minor version alone is not evidence of compatibility.

## Per-API evidence

The [API status table](../docs/api-status.md) is a reviewed implementation
snapshot with platform limits and extension candidates. Its implementation
statuses are separate from the fixture coverage report below.

`implementation-evidence.json` maps reviewed implementations to specific frontend
fixtures. Generate the full declaration/member inventory with:

```sh
node compatibility/scripts/report-api-coverage.mjs artifacts/compatibility/api-report.json
```

To attach validated differential results, append reference and NetWiv JSONL paths:

```sh
node compatibility/scripts/report-api-coverage.mjs artifacts/compatibility/api-report.json tests/conformance/results/reference-windows.jsonl tests/conformance/results/wry-windows.jsonl
```

The report runs the strict comparator first, preserves run provenance, and
separates source evidence, matched fixtures and GUI status. It includes types,
constructors, properties and methods; its count is not a count of runtime
commands. Root barrel re-exports are excluded to avoid duplicating modules.
Unreviewed entries remain visible even if an implementation exists. Matched
fixtures establish only the behaviors and inputs described in those fixtures,
not complete compatibility of the associated API. Reports describe their
recorded build/run snapshot, not later untested source edits.

Platform evidence is independent. Adding macOS runtime paths does not turn
Windows/Linux fixture mappings or source inventories into macOS passes. Record
macOS reference/candidate runs with the same architecture, pinned guest and
source snapshot; see [macOS conformance instructions](../tests/conformance/README.md#macos-runs).
The current Apple Silicon NativeAOT/GUI smoke is integration evidence, and Intel
and complete macOS differential acceptance remain open. Keep generated upstream
manifests unchanged when documenting these limits.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
