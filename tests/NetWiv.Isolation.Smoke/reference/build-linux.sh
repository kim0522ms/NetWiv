#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
# Tauri codegen sets file permissions. Use the Linux filesystem when the shared
# checkout is on a Windows drive; do not weaken upstream build behavior.
stage=$(mktemp -d "${TMPDIR:-/tmp}/netwiv-isolation-reference.XXXXXX")
mkdir -p "$stage/tests/NetWiv.Isolation.Smoke/reference/src" "$stage/tests/conformance/reference/src-tauri" "$stage/tests/NetWiv.Isolation.Smoke/reference/vendor"
cp "$root/tests/NetWiv.Isolation.Smoke/reference/"{Cargo.toml,Cargo.lock,build.rs,tauri.conf.json} "$stage/tests/NetWiv.Isolation.Smoke/reference/"
cp "$root/tests/NetWiv.Isolation.Smoke/reference/src/main.rs" "$stage/tests/NetWiv.Isolation.Smoke/reference/src/"
cp -R "$root/tests/NetWiv.Isolation.Smoke/"{frontend,isolation} "$stage/tests/NetWiv.Isolation.Smoke/"
cp -R "$root/tests/conformance/reference/src-tauri/icons" "$stage/tests/conformance/reference/src-tauri/"
cp -R "$root/tests/NetWiv.Isolation.Smoke/reference/vendor/@tauri-apps" "$stage/tests/NetWiv.Isolation.Smoke/reference/vendor/"
cargo +1.95.0 build --locked --release --manifest-path "$stage/tests/NetWiv.Isolation.Smoke/reference/Cargo.toml" --target-dir "${NETWIV_REFERENCE_TARGET:?Set NETWIV_REFERENCE_TARGET to a Linux build directory}"
printf 'Reference source staging directory: %s\n' "$stage"
