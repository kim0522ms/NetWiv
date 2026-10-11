import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const script = fileURLToPath(new URL('./run-linux-smoke-desktop.sh', import.meta.url))

function run(t, failures, appStatus) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'netwiv-desktop-cleanup-'))
  t.after(() => rmSync(root, { recursive: true, force: true }))
  const bin = path.join(root, 'bin')
  mkdirSync(bin)
  const tool = (name, body) => writeFileSync(path.join(bin, name), '#!/usr/bin/env bash\nset -eu\n' + body, { mode: 0o755 })
  tool('realpath', 'printf "%s\\n" "$1"\n')
  tool('timeout', 'shift; exec "$@"\n')
  tool('Xephyr', 'exec sleep 300\n')
  tool('xfwm4', 'exec sleep 300\n')
  tool('xfce4-panel', `if [[ "\${1:-}" == --quit ]]; then kill "$(cat "$FIXTURE/panel.pid")"; exit 0; fi
    echo $$ > "$FIXTURE/panel.pid"
    exec sleep 300
  `)
  tool('xdpyinfo', `if [[ ! -e "$FIXTURE/display-checked" ]]; then touch "$FIXTURE/display-checked"; exit 1; fi
    exit 0
  `)
  tool('xprop', 'echo "window id # 0x123"\n')
  tool('gdbus', 'if [[ -e "$FIXTURE/panel.pid" ]]; then echo "(true,)"; else echo "(false,)"; fi\n')
  // Execute the real nested session and both EXIT traps with isolated fake desktop tools.
  tool('dbus-run-session', 'shift; exec "$@"\n')
  tool('mktemp', 'mkdir "$FIXTURE/config"; printf "%s\\n" "$FIXTURE/config"\n')
  tool('rm', `count=0
    [[ ! -e "$FIXTURE/removals" ]] || count=$(cat "$FIXTURE/removals")
    count=$((count + 1)); echo "$count" > "$FIXTURE/removals"
    if (( count <= FAILURES )); then
      echo 'rm: cannot remove xfce-perchannel-xml: Directory not empty' >&2
      exit 1
    fi
    exec /bin/rm "$@"
  `)
  tool('smoke', 'echo "PASS smoke fixture"; exit "$APP_STATUS"\n')
  const result = spawnSync('bash', ['-x', script, path.join(bin, 'smoke'), 'x11'], {
    encoding: 'utf8', env: { ...process.env, PATH: bin + path.delimiter + process.env.PATH,
      FIXTURE: root, FAILURES: String(failures), APP_STATUS: String(appStatus), NETWIV_DESKTOP_LOG: path.join(root, 'logs') },
  })
  assert.ifError(result.error)
  assert.match(result.stdout, /PASS smoke fixture/)
  assert.doesNotMatch(result.stderr, /required forced termination/)
  return { result, configExists: existsSync(path.join(root, 'config')), attempts: Number(readFileSync(path.join(root, 'removals'), 'utf8')) }
}

for (const [label, failures, appStatus, expectedStatus, removed] of [
  ['successful cleanup', 0, 0, 0, true],
  ['late Xfconf write during cleanup', 1, 0, 0, true],
  ['application failure with late write', 1, 42, 42, true],
  ['persistent cleanup failure', 100, 0, 125, false],
  ['application failure with persistent cleanup failure', 100, 42, 42, false],
]) {
  test(`Linux desktop wrapper: ${label}`, { skip: process.platform === 'win32' }, t => {
    const f = run(t, failures, appStatus)
    assert.equal(f.result.status, expectedStatus, f.result.stderr)
    assert.equal(f.configExists, !removed)
    if (removed) assert.equal(f.attempts, failures + 1)
    else {
      assert.ok(f.attempts > 1 && f.attempts < 100, 'Cleanup retries must be bounded')
      assert.match(f.result.stderr, /Directory not empty/)
    }
  })
}
