#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
wrapper=${1:-"$root/scripts/run-linux-smoke-desktop.sh"}
# Exercise the production cleanup function without starting a desktop session.
source <(sed -n '/^stop_children() {$/,/^}$/p' "$wrapper")
declare -F stop_children >/dev/null
sleep 30 & selected=$!
sleep 30 & unrelated=$!
trap 'kill "$selected" "$unrelated" 2>/dev/null || true; wait "$selected" "$unrelated" 2>/dev/null || true' EXIT
started=$(date +%s%N)
stop_children "$selected"
elapsed=$(( ($(date +%s%N) - started) / 1000000 ))
if (( elapsed >= 2000 )); then
  echo "Cleanup waited for unrelated background jobs: ${elapsed}ms" >&2
  exit 1
fi
kill -0 "$unrelated"
stop_children "$unrelated"
echo "PASS selected child cleanup (${elapsed}ms) preserves unrelated jobs"

# A panel that needs its quit protocol must get that request before SIGTERM.
source <(sed -n '/^stop_panel() {$/,/^}$/p' "$wrapper")
mock=$(mktemp -d)
export NETWIV_PANEL_TEST_READY="$mock/ready"
export NETWIV_PANEL_TEST_SIGNALS="$mock/signals"
cat >"$mock/xfce4-panel" <<'SH'
#!/usr/bin/env bash
[[ "$1" == --quit ]] || exit 1
printf 'quit\n' >>"$NETWIV_PANEL_TEST_SIGNALS"
kill -USR1 "$NETWIV_PANEL_TEST_PID"
SH
chmod +x "$mock/xfce4-panel"
export PATH="$mock:$PATH"
bash -c 'trap "exit 0" USR1; trap '\''printf "term\n" >>"$NETWIV_PANEL_TEST_SIGNALS"'\'' TERM; touch "$NETWIV_PANEL_TEST_READY"; while :; do sleep 0.05; done' & selected=$!
export NETWIV_PANEL_TEST_PID=$selected
sleep 30 & unrelated=$!
trap 'kill -KILL "$selected" "$unrelated" 2>/dev/null || true; wait "$selected" "$unrelated" 2>/dev/null || true; rm -rf -- "$mock"' EXIT
for ((attempt=0; attempt<100; attempt++)); do
  [[ -f "$NETWIV_PANEL_TEST_READY" ]] && break
  sleep 0.01
done
[[ -f "$NETWIV_PANEL_TEST_READY" ]]
stop_panel "$selected"
[[ "$(cat "$NETWIV_PANEL_TEST_SIGNALS")" == quit ]]
kill -0 "$unrelated"
stop_children "$unrelated"
echo 'PASS panel quit precedes termination and preserves unrelated jobs'
