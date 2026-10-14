import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

test('both host runners reap descendants without help from an ancestor reaper', { skip: process.platform !== 'linux' }, () => {
  // A nearer subreaper that deliberately does not wait for orphaned children
  // reproduces a non-reaping container init without privileged PID namespaces.
  // It checks for leaks before cleaning up its test-owned processes on failure.
  const supervisor = String.raw`
import ctypes, os, signal, subprocess, sys, time
from pathlib import Path
assert ctypes.CDLL(None).prctl(36, 1, 0, 0, 0) == 0
code = subprocess.call(sys.argv[1:])
children = Path(f'/proc/self/task/{os.getpid()}/children')
leaked = bool(children.read_text().split())
while children.read_text().split():
    for child in children.read_text().split():
        try: os.kill(int(child), signal.SIGKILL)
        except ProcessLookupError: pass
    while True:
        try: pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError: break
        if pid == 0: break
    time.sleep(0.01)
if leaked: print('Owned descendants escaped to the ancestor reaper', file=sys.stderr)
sys.exit(1 if leaked else code)
`;
  const tests = [new URL('./run-host.test.mjs', import.meta.url),
    new URL('../../NetWiv.Isolation.Smoke/run.test.mjs', import.meta.url)].map(fileURLToPath);
  const env = { ...process.env };
  delete env.NODE_TEST_CONTEXT;
  const output = execFileSync('python3', ['-c', supervisor, process.execPath, '--test', ...tests],
    { encoding: 'utf8', timeout: 30000, env });
  assert.match(output, /# fail 0|ℹ fail 0/);
});
