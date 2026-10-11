#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
published=$(realpath "${1:?Pass the NativeAOT smoke publish directory}")
binary="$published/NetWiv.Wry.Smoke"
[[ -f "$binary" ]] || { echo "NativeAOT smoke executable is missing: $binary" >&2; exit 1; }
compiler=$(command -v rustc || true)
if [[ -z "$compiler" ]]; then compiler="${CARGO_HOME:-$HOME/.cargo}/bin/rustc"; fi
[[ -x "$compiler" ]] || { echo 'Rust 1.95.0 via rustup is required for ABI fixture compilation.' >&2; exit 1; }
# Use the native filesystem: WSL DrvFS may not permit executable mode changes.
output=$(mktemp -d "${TMPDIR:-/tmp}/netwiv-native-startup-XXXXXXXX")
library=libnetwiv_native.so
if [[ "$(uname -s)" == Darwin ]]; then library=libnetwiv_native.dylib; fi
for fixture in missing-library wrong-version missing-entry; do
  directory="$output/$fixture"
  mkdir -p "$directory"
  cp "$binary" "$directory/NetWiv.Wry.Smoke"
  chmod +x "$directory/NetWiv.Wry.Smoke"
  case "$fixture" in
    missing-library) expected='native library or an OS dependency is missing' ;;
    wrong-version) expected='native ABI 99 does not match managed ABI 2' ;;
    missing-entry) expected='incompatible ABI or architecture' ;;
  esac
  if [[ "$fixture" != missing-library ]]; then
    "$compiler" +1.95.0 --crate-type cdylib --edition 2021 "$root/tests/NetWiv.Wry.Smoke/abi-fixtures/$fixture.rs" -o "$directory/$library"
  fi
  "$directory/NetWiv.Wry.Smoke" --expect-startup-error "$expected"
done
echo "Startup fixtures preserved in $output"
