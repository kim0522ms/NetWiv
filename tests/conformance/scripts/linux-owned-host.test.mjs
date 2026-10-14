import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

for (const missingChildren of [false, true]) {
test(`both host runners reap without ancestor help (procfs children ${missingChildren ? 'absent' : 'available'})`, { skip: process.platform !== 'linux' }, () => {
  // A nearer subreaper that deliberately does not wait for orphaned children
  // reproduces a non-reaping container init without privileged PID namespaces.
  // It checks for leaks before cleaning up its test-owned processes on failure.
  const supervisor = String.raw`
import ctypes, os, signal, subprocess, sys, time
from pathlib import Path
assert ctypes.CDLL(None).prctl(36, 1, 0, 0, 0) == 0
code = subprocess.call(sys.argv[1:])
leaked = False
while True:
    try: pid, _ = os.waitpid(-1, os.WNOHANG)
    except ChildProcessError: break
    leaked = True
    if pid: continue
    for entry in Path('/proc').iterdir():
        if not entry.name.isdecimal(): continue
        try:
            fields = (entry / 'stat').read_bytes().rpartition(b')')[2].split()
            if int(fields[1]) == os.getpid(): os.kill(int(entry.name), signal.SIGKILL)
        except (FileNotFoundError, ProcessLookupError, PermissionError): pass
    time.sleep(0.01)
if leaked: print('Owned descendants escaped to the ancestor reaper', file=sys.stderr)
sys.exit(1 if leaked else code)
`;
  const tests = [new URL('./run-host.test.mjs', import.meta.url),
    new URL('../../NetWiv.Isolation.Smoke/run.test.mjs', import.meta.url)].map(fileURLToPath);
  const env = { ...process.env };
  delete env.NODE_TEST_CONTEXT;
  const fixture = mkdtempSync(path.join(tmpdir(), 'netwiv-procfs-fixture-'));
  try {
    if (missingChildren) {
      // Python's audit hook blocks the same file for the supervisor and every
      // launcher, without privileged mounts or changing the host's procfs.
      writeFileSync(path.join(fixture, 'sitecustomize.py'), String.raw`
import errno, sys
def without_children(event, args):
    if event == 'open' and isinstance(args[0], str) and args[0].startswith('/proc/') and args[0].endswith('/children'):
        raise FileNotFoundError(errno.ENOENT, 'procfs children entry unavailable')
sys.addaudithook(without_children)
`);
      env.PYTHONPATH = fixture + (env.PYTHONPATH ? path.delimiter + env.PYTHONPATH : '');
      execFileSync('python3', ['-c', String.raw`
import os
try: open(f'/proc/self/task/{os.getpid()}/children')
except FileNotFoundError: pass
else: raise AssertionError('Missing procfs fixture was not installed')
`], { env });
    }
    const output = execFileSync('python3', ['-c', supervisor, process.execPath, '--test', ...tests],
      { encoding: 'utf8', timeout: 30000, env });
    assert.match(output, /# fail 0|ℹ fail 0/);
  } finally { rmSync(fixture, { recursive: true, force: true }); }
});
}
