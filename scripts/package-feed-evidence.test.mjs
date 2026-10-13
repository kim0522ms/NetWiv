import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../', import.meta.url));

function runEvidence(setup = '') {
  return spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
    $ErrorActionPreference = 'Stop'
    ${setup}
    . './scripts/test-package-feed-evidence.ps1'
    if (Test-Path variable:\\LASTEXITCODE) { exit $LASTEXITCODE }
  `], { cwd: root, encoding: 'utf8', timeout: 30_000 });
}

test('package evidence regression succeeds with the Actions PowerShell exit-code wrapper', () => {
  const result = runEvidence();
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.match(result.stdout, /PASS package consumer failure:/);
});

test('package evidence regression still propagates unexpected failures', () => {
  const result = runEvidence("function Copy-Item { throw 'Injected archive copy failure' }");
  assert.ifError(result.error);
  assert.notEqual(result.status, 0);
  assert.match(result.stdout + result.stderr, /Injected archive copy failure/);
  assert.doesNotMatch(result.stdout, /PASS package consumer failure:/);
});
