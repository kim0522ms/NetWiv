# Pinned upstream business-test coverage

Regenerate the inventory with:

```sh
node compatibility/scripts/generate-upstream-manifests.mjs --tests-only
```

This maintainer command runs the pinned Rust AST scanner in
`compatibility/oracles/upstream-tests`. It verifies archive checksums and uses
runtime 2.11.5 for runtime/utils, CLI 2.11.4 for CLI/bundler, and tauri-build 2.6.3
for build/plugin code. It does not require Rust for ordinary NetWiv consumers.
`compatibility/generated/upstream-tests.json` is generated; do not edit it.

`compatibility/upstream-test-ports.json` is the reviewed mapping. Each mapped
entry pins the upstream file hash and names the C# test source and harness case.
Upstream changes invalidate that mapping. `ported` means matching source exists;
it does not mean every platform has executed it or the whole milestone is done.

The scanner reads Rust syntax, not text matches in comments or string literals.
It also enumerates `#[quickcheck]` tests and proptest function declarations. Remaining macros retain
review entries. Eight Handlebars Rust template files render under both
license-header branches using pinned Handlebars 6.3.0. All sixteen rendered
variants parse as Rust and contain no tests; source hashes and profiles are
recorded under `templateParsing`. Unknown template variables and malformed
rendered Rust still fail. There are no remaining source parse errors.
Integration tests and platform/application flows still require separate evidence.

## Coverage and interpretation

Use [`upstream-tests.json`](../compatibility/generated/upstream-tests.json) for
the complete scanned inventory and
[`upstream-test-ports.json`](../compatibility/upstream-test-ports.json) for reviewed
mappings. These are the source of truth for counts and test identities. Excluded
and unreviewed entries remain in the inventory and are not counted as passes.

Ports cover areas such as state ownership, events, file scopes, HTML processing,
IPC parsing, permissions, capability files, paths, URL patterns, version parsing,
resource staging and signing. Run the named C# harness case from each mapping
on the relevant target. Runtime execution and desktop interaction require
separate evidence from source-level mapping.

Some contracts require explicit adaptation:

- State cleanup maps Rust `Drop` to `IDisposable`; rejected arguments remain
  caller-owned because C# does not consume arguments. See [C# ownership](csharp-api.md).
- Runner defaults map Cargo to dotnet/MSBuild. Configuration parsing, argument
  ordering and child-process exit behavior still require tests.
- Callback and event property ports use deterministic generated inputs. Finite
  input coverage does not establish equivalence for all strings or schedules.
- Native tray encoders and color parsers can be tested without proving actual
  desktop tray interaction. GUI validation remains separate.

No unreviewed test is assumed to be language-only. Tests of state ownership,
concurrency, event ordering, ACL resolution, serialization, config parsing,
resources, signing or bundling are business behavior even when their original
implementation uses Rust features. Only a reviewed rationale can exclude a
language-specification test or a platform outside the supported desktop baseline.

The four reviewed exclusions are `app::is_send_sync`, `error::error_is_send_sync`,
`webview::webview_is_send_sync` and `window::window_is_send_sync`. Each body only
instantiates `assert_send<T: Send>() {}` and `assert_sync<T: Sync>() {}` from the
pinned `test_utils` module. C# has no corresponding compiler traits. This does
not exclude runtime concurrency, UI affinity, callback lifetime or error-delivery
tests. Those observable behaviors remain part of desktop acceptance.
The coverage-parity gate remains open.
