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

## Per-API implementation and fixture mapping

The [API status table](../docs/api-status.md) is a reviewed implementation
snapshot with platform limits and extension candidates.
`implementation-evidence.json` maps implementations to specific frontend
fixtures. These source mappings are not execution results or compatibility
passes. The generated API inventory includes declarations, constructors,
properties and methods; its count is not a count of runtime commands. Root
barrel re-exports are excluded when counting unique identifiers.

Run the [paired conformance checks](../tests/conformance/README.md) to compare
the pinned guest's observable results. The runner evaluates functional
assertions in memory and does not retain validation reports or collect source,
executable or environment fingerprints. Summarize the checks actually run and
their limits in the PR. A matched fixture covers only its tested behaviors and
inputs, not the entire associated API.

Platform verification is independent. Source mappings and Windows/Linux
results do not establish macOS conformance. Hosted arm64 NativeAOT smokes
check integration; complete macOS arm64 differential acceptance
remains unverified. Keep generated upstream manifests unchanged when documenting
these limits.

These inventories describe the pinned upstream API, not the public export list
of `@netwiv/api`. New applications use the [NetWiv desktop frontend SDK](../docs/frontend-api.md);
its mobile exclusions do not remove unsupported entries from upstream coverage.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.
WRY is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
