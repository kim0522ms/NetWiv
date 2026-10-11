# Remote capability URL patterns

The Core ACL compiles remote URL selectors when the application is built. It
matches protocol, credentials, hostname, port, pathname, query and fragment
separately; hostname wildcards cannot consume a port. Invalid selectors fail
configuration instead of silently becoming inert or permissive rules.

The implementation follows the pinned `urlpattern 0.3.0` constructor/token/part
algorithms and the additional `tauri-utils 2.9.3` `RemoteUrlPattern` defaults:
empty query and fragment patterns become `*`, and an empty or `/` pathname
becomes `*`. Matching preserves escaped unreserved characters from the native
source URL: `%61` is distinct from `a`. C# `Uri.AbsolutePath` must not be used to
decide this boundary because it decodes that escape.

The comparison-only Rust executable in `compatibility/oracles/urlpattern`
reproduces those pinned defaults. It is not part of the product, CLI or ordinary
consumer build. To regenerate the embedded test evidence:

```sh
cargo build --locked --manifest-path compatibility/oracles/urlpattern/Cargo.toml --target-dir artifacts/urlpattern-oracle
node compatibility/oracles/urlpattern/generate-fixtures.mjs artifacts/urlpattern-oracle/debug/netwiv-urlpattern-oracle /path/to/urlpattern-0.3.0/src/testdata/urlpatterntestdata.json
```

Use the `.exe` suffix on Windows. The generated fixture records the oracle lock
and upstream data hashes. It includes targeted capability boundary cases and
constructor/path cases derived from the dependency's bundled tests. Expected
parsing validity and URL decisions come from the Rust executable, including
TAURI®'s differences from generic URLPattern defaults.

Managed matching uses the AOT-compatible non-backtracking regex interpreter.
Rust bracket union, intersection, subtraction and symmetric difference are
translated over the ASCII URL component alphabet after percent/IDNA encoding.
This avoids assigning .NET semantics to Rust character classes. Backreferences
and lookaround are not supported by the pinned Rust matcher either.

The current corpus is bounded evidence, not full acceptance of every Rust regex
or WHATWG URL input. Extended Unicode property aliases, regex flag combinations,
identifier Unicode version differences, all invalid host canonicalizations and native iframe
source attribution still require wider comparison. Unsupported regex syntax
fails configuration rather than falling back to a permissive match. This work
does not establish isolation-mode IPC or remote iframe grants.

Sources: [pinned Tauri ACL](https://github.com/tauri-apps/tauri/blob/7cd71369c00978a3783b6ae3e9972358abbe4ae6/crates/tauri-utils/src/acl/mod.rs),
[URL Pattern Standard](https://urlpattern.spec.whatwg.org/), and the locally
pinned `urlpattern 0.3.0` crate sources retained in Cargo's registry.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
