#!/usr/bin/env bash
set -euo pipefail
repository="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
case "$(uname -s):$(uname -m)" in
  Linux:x86_64) rid=linux-x64; triple=x86_64-unknown-linux-gnu; library=libnetwiv_native.so ;;
  Darwin:arm64) rid=osx-arm64; triple=aarch64-apple-darwin; library=libnetwiv_native.dylib ;;
  Darwin:x86_64) rid=osx-x64; triple=x86_64-apple-darwin; library=libnetwiv_native.dylib ;;
  *) echo 'Build on Linux x86_64 or macOS arm64/x86_64.' >&2; exit 1 ;;
esac
if [[ "$rid" == osx-* ]]; then
  export MACOSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-12.0}"
fi
target_directory="${NETWIV_CARGO_TARGET_DIR:-$repository/artifacts/native-target/$rid}"
destination="$repository/artifacts/native/$rid"
cargo +1.95.0 build --release --locked \
  --manifest-path "$repository/native/netwiv-native/Cargo.toml" \
  --target "$triple" --target-dir "$target_directory"
mkdir -p -- "$destination"
cp -- "$target_directory/$triple/release/$library" "$destination/"
# The release profile strips symbols; the C ABI exports must survive it.
expected=$(grep -oE 'netwiv_[a-z0-9_]+\(' "$repository/native/netwiv-native/include/netwiv.h" | tr -d '(' | sort -u)
if [[ "$rid" == osx-* ]]; then
  exported=$(nm -gU "$destination/$library" | awk '$3 ~ /^_netwiv_/ { print substr($3, 2) }' | sort -u)
else
  exported=$(nm -D --defined-only "$destination/$library" | awk '$3 ~ /^netwiv_/ { print $3 }' | sort -u)
fi
if [[ "$exported" != "$expected" ]]; then
  echo "$library exports do not match netwiv.h:" >&2
  diff <(echo "$expected") <(echo "$exported") >&2 || true
  exit 1
fi
if [[ "$rid" == osx-* ]]; then
  install_name_tool -id "@rpath/$library" "$destination/$library"
  codesign --force --sign - "$destination/$library"
  shasum -a 256 "$destination/$library"
else
  sha256sum "$destination/$library"
fi
