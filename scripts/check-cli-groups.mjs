import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';

const command = ['run', '--project', 'tests/NetWiv.Cli.Tests', '-c', 'Release', '--no-build', '--'];
const list = group => JSON.parse(execFileSync('dotnet', [...command, '--list', group], { encoding: 'utf8' }));
const all = list('all');
const contracts = list('contracts');
const lifecycle = list('lifecycle');
assert.ok(contracts.length > 0 && lifecycle.length > 0, 'Both CLI groups must execute tests');
const combined = [...contracts, ...lifecycle];
assert.equal(new Set(combined).size, combined.length, 'CLI groups must not overlap');
assert.deepEqual(combined.sort(), all.sort(), 'Every CLI test must belong to a CI group');
for (const option of ['--list', '--group']) {
  const invalid = spawnSync('dotnet', [...command, option, 'unknown'], { encoding: 'utf8' });
  assert.equal(invalid.error, undefined);
  assert.notEqual(invalid.status, 0, 'An unknown CLI group must fail');
}
console.log(`PASS CLI partition: ${contracts.length} contracts + ${lifecycle.length} lifecycle = ${all.length} tests`);
