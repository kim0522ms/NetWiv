import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import net from 'node:net'
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
  tool('Xephyr', `mode=reset
    for argument in "$@"; do
      if [[ "$argument" == -noreset ]]; then mode=keep; fi
    done
    printf '%s\\n' "$mode" > "$FIXTURE/display-mode"
    exec sleep 300
  `)
  tool('xfwm4', 'exec sleep 300\n')
  tool('xfce4-panel', `if [[ "\${1:-}" == --quit ]]; then kill "$(cat "$FIXTURE/panel.pid")"; exit 0; fi
    echo $$ > "$FIXTURE/panel.pid"
    exec sleep 300
  `)
  // A readiness probe is the only client before the desktop starts. Without
  // -noreset, disconnecting that client resets Xephyr and loses the ready display.
  tool('xdpyinfo', `[[ -e "$FIXTURE/display-mode" && ! -e "$FIXTURE/display-reset" ]] || exit 1
    connections=0
    [[ ! -e "$FIXTURE/display-connections" ]] || connections=$(cat "$FIXTURE/display-connections")
    echo "$((connections + 1))" > "$FIXTURE/display-connections"
    if [[ "$(cat "$FIXTURE/display-mode")" == reset ]]; then touch "$FIXTURE/display-reset"; fi
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
  tool('smoke', `[[ "$GDK_BACKEND" == x11 && "$NETWIV_EXPECT_BACKEND" == x11 ]]
    echo "PASS smoke fixture"; exit "$APP_STATUS"\n`)
  const result = spawnSync('bash', ['-x', script, path.join(bin, 'smoke'), 'x11'], {
    encoding: 'utf8', env: { ...process.env, PATH: bin + path.delimiter + process.env.PATH,
      FIXTURE: root, FAILURES: String(failures), APP_STATUS: String(appStatus) },
  })
  assert.ifError(result.error)
  assert.match(result.stdout, /PASS smoke fixture/)
  assert.doesNotMatch(result.stderr, /required forced termination/)
  return { result, configExists: existsSync(path.join(root, 'config')), attempts: Number(readFileSync(path.join(root, 'removals'), 'utf8')),
    displayConnections: Number(readFileSync(path.join(root, 'display-connections'), 'utf8')) }
}

async function waylandFixture(t, { overrides = {}, appStatus = 0, busStatus = 0, absoluteSocket = false } = {}) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'netwiv-wayland-'))
  const bin = path.join(root, 'bin')
  mkdirSync(bin)
  const socket = path.join(root, 'wayland-fixture')
  const server = net.createServer()
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(socket, resolve) })
  t.after(async () => {
    await new Promise(resolve => server.close(resolve))
    rmSync(root, { recursive: true, force: true })
  })
  const tool = (name, body) => writeFileSync(path.join(bin, name), '#!/bin/bash\nset -eu\n' + body, { mode: 0o755 })
  tool('realpath', 'printf "%s\\n" "$1"\n')
  tool('timeout', 'shift; exec "$@"\n')
  tool('gdbus', `[[ "$DBUS_SESSION_BUS_ADDRESS" == "$FIXTURE_BUS" && "$*" == *org.freedesktop.DBus.GetId ]]
    exit "$BUS_STATUS"
  `)
  // Deliberately no Xephyr, Xfce, X11 or private D-Bus session tools in PATH.
  // This executable emits no smoke/backend marker: the wrapper also hosts Node
  // launchers and upstream reference applications.
  // macOS system Bash 3.2 needs parameter expansion to distinguish unset from empty.
  tool('application', `[[ "$GDK_BACKEND" == wayland && "$NETWIV_EXPECT_BACKEND" == wayland ]]
    [[ "$WAYLAND_DISPLAY" == "$FIXTURE_DISPLAY" && "\${XDG_RUNTIME_DIR:-}" == "$FIXTURE_RUNTIME" ]]
    [[ "$DBUS_SESSION_BUS_ADDRESS" == "$FIXTURE_BUS" && "$DISPLAY" == :fixture ]]
    [[ "$XDG_CONFIG_HOME" == fixture-config && "$XDG_SESSION_TYPE" == wayland ]]
    [[ -z "\${WEBKIT_DISABLE_COMPOSITING_MODE+x}" && -z "\${G_DEBUG+x}" && -z "\${LIBGL_ALWAYS_SOFTWARE+x}" ]]
    [[ "$#" == 2 && "$1" == 'argument with spaces' && "$2" == --fixture ]]
    exit "$APP_STATUS"
  `)
  const env = { ...process.env, PATH: bin, WAYLAND_DISPLAY: absoluteSocket ? socket : path.basename(socket),
    XDG_RUNTIME_DIR: root, XDG_SESSION_TYPE: 'wayland', DBUS_SESSION_BUS_ADDRESS: 'unix:abstract=fixture-session',
    XDG_CONFIG_HOME: 'fixture-config', DISPLAY: ':fixture', APP_STATUS: String(appStatus), BUS_STATUS: String(busStatus) }
  for (const name of ['WEBKIT_DISABLE_COMPOSITING_MODE', 'G_DEBUG', 'LIBGL_ALWAYS_SOFTWARE']) delete env[name]
  for (const [name, value] of Object.entries(overrides)) {
    if (value === null) delete env[name]
    else env[name] = value
  }
  Object.assign(env, { FIXTURE_DISPLAY: env.WAYLAND_DISPLAY ?? '', FIXTURE_RUNTIME: env.XDG_RUNTIME_DIR ?? '', FIXTURE_BUS: env.DBUS_SESSION_BUS_ADDRESS ?? '' })
  const result = spawnSync('/bin/bash', [script, path.join(bin, 'application'), 'wayland', 'argument with spaces', '--fixture'], {
    encoding: 'utf8', env,
  })
  assert.ifError(result.error)
  return result
}

for (const absoluteSocket of [false, true]) {
  test(`Wayland wrapper preserves the logged-in session with ${absoluteSocket ? 'absolute' : 'relative'} socket`,
    { skip: process.platform === 'win32' }, async t => {
      const result = await waylandFixture(t, { absoluteSocket })
      assert.equal(result.status, 0, result.stderr)
      assert.equal(result.stdout, '')
    })
}

test('Wayland wrapper propagates application failure', { skip: process.platform === 'win32' }, async t => {
  const result = await waylandFixture(t, { appStatus: 42 })
  assert.equal(result.status, 42, result.stderr)
})

test('Wayland wrapper accepts an absolute socket without XDG_RUNTIME_DIR', { skip: process.platform === 'win32' }, async t => {
  const result = await waylandFixture(t, { absoluteSocket: true, overrides: { XDG_RUNTIME_DIR: null } })
  assert.equal(result.status, 0, result.stderr)
})

for (const [label, options, diagnostic] of [
  ['missing display', { overrides: { WAYLAND_DISPLAY: null } }, /requires WAYLAND_DISPLAY/],
  ['missing runtime directory', { overrides: { XDG_RUNTIME_DIR: null } }, /requires XDG_RUNTIME_DIR/],
  ['missing socket', { overrides: { WAYLAND_DISPLAY: 'missing-socket' } }, /display socket is unavailable/],
  ['non-socket display', { overrides: { WAYLAND_DISPLAY: script } }, /display socket is unavailable/],
  ['missing session bus', { overrides: { DBUS_SESSION_BUS_ADDRESS: null } }, /requires the existing DBUS_SESSION_BUS_ADDRESS/],
  ['unreachable session bus', { busStatus: 1 }, /existing D-Bus session is unavailable/],
]) {
  test(`Wayland wrapper rejects ${label}`, { skip: process.platform === 'win32' }, async t => {
    const result = await waylandFixture(t, options)
    assert.equal(result.status, 1)
    assert.match(result.stderr, diagnostic)
    assert.equal(result.stdout, '')
  })
}

test('Linux desktop wrapper rejects unknown backends before probing desktop tools', { skip: process.platform === 'win32' }, () => {
  const result = spawnSync('/bin/bash', [script, '/unused', 'unknown'], { encoding: 'utf8', env: { PATH: '' } })
  assert.ifError(result.error)
  assert.equal(result.status, 2)
  assert.match(result.stderr, /usage:/)
})

test('Linux X11 wrapper keeps fresh displays ready after the last readiness client disconnects', { skip: process.platform === 'win32' }, t => {
  for (let session = 0; session < 2; session++) {
    const fixture = run(t, 0, 0)
    assert.equal(fixture.result.status, 0, fixture.result.stderr)
    assert.ok(fixture.displayConnections >= 2, 'Both readiness probes must reconnect before the desktop starts')
    assert.equal(fixture.configExists, false)
  }
})

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
