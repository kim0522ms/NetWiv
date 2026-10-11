# Pinned license sources

The notice generator first uses license and notice files in each downloaded
Cargo package. For packages which omit those files, `manifest.json` records
verbatim text SHA-256 and byte length, normally from the repository revision in
the package's `.cargo_vcs_info.json`. The generator runs without network access
once Cargo's locked dependency graph is available locally.

`stronghold-utils@1.0.0` has no Cargo VCS revision or included license text.
Its manifest declares Apache-2.0, so `packageLicenseTexts` records the exact
canonical Apache text and URL without claiming a pinned repository file.

`selectors@0.36.1` identifies MPL-2.0 in its manifest and source headers, but its
pinned repository does not contain a standalone license file. Its override
records Mozilla's canonical license URL, rather than claiming a repository file
exists. The exact downloaded text is retained and hashed.

The two `winapi-*-pc-windows-gnu@0.4.0` packages predate Cargo VCS metadata.
Every original packaged file (1,390 i686 and 1,419 x86_64 files) was compared
against Git blobs at winapi-rs revision
`9497609ef44cc9bcd16cd2411c0ee6ccaf5483aa`. The full recursive Git tree was
retrieved from:

https://api.github.com/repos/retep998/winapi-rs/git/trees/9497609ef44cc9bcd16cd2411c0ee6ccaf5483aa?recursive=1

`source-proofs/` preserves each package path, corresponding repository path and
matching Git blob SHA-1. `Cargo.toml.orig` maps to the repository's `Cargo.toml`;
Cargo's normalized manifest and extraction marker are excluded. This establishes
content equivalence to that revision, not an otherwise unknown publication
commit. The source proofs have SHA-256 pins, are checked against every original
package file on regeneration, and accompany the NuGet notices.

Run `node --test native/license-provenance.test.mjs` to verify the current
inventory and rejection of changed, extra, duplicate or missing package files.
