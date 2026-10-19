import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { compileFunction } from 'node:vm';

const guestFile = fileURLToPath(new URL('./index.html', import.meta.url));
const html = readFileSync(guestFile, 'utf8');
const script = html.match(/<script type="module">([\s\S]*?)<\/script>/)[1];
const run = compileFunction('return (async () => {' +
  script.replace("await import('./core.js')", 'await loadCore()') + '\n})();',
  ['document', 'loadCore', 'localStorage'], { filename: guestFile });

async function fixture({ phase = 'read', echo = 'sandbox-ipc', reject = '', storage = new Map() } = {}) {
  const status = {}, completions = [];
  const fail = () => { throw Error('private fixture details'); };
  await run({ getElementById: () => status }, async () => ({
    async invoke(command, payload) {
      if (command === reject) fail();
      if (command === 'echo') return echo;
      if (command === 'phase') return phase;
      if (command === 'storage_value') return 'current-run';
      assert.equal(command, 'complete');
      completions.push(payload);
    }
  }), {
    setItem(key, value) { if (reject === 'setItem') fail(); storage.set(key, value); },
    getItem(key) { if (reject === 'getItem') fail(); return storage.get(key) ?? null; },
    removeItem(key) { if (reject === 'removeItem') fail(); storage.delete(key); }
  });
  return { status, completions };
}

test('write checks its value and read checks persistence before removing the fixture', async () => {
  const storage = new Map();
  const written = await fixture({ phase: 'write', storage });
  assert.deepEqual(written.completions, [{ success: true }]);
  assert.equal(storage.get('netwiv-sandbox-fixture'), 'current-run');
  assert.equal(storage.get('netwiv-sandbox-sentinel'), 'retained');
  const read = await fixture({ storage });
  assert.deepEqual(read.completions, [{ success: true }]);
  assert.equal(storage.has('netwiv-sandbox-fixture'), false);
  assert.equal(storage.get('netwiv-sandbox-sentinel'), 'retained');
});

for (const [label, options, stage] of [
  ['echo rejection', { reject: 'echo' }, 'ipc-echo'],
  ['echo mismatch', { echo: 'wrong' }, 'ipc-echo'],
  ['phase rejection', { reject: 'phase' }, 'ipc-phase'],
  ['fixture rejection', { reject: 'storage_value' }, 'ipc-fixture'],
  ['write exception', { phase: 'write', reject: 'setItem' }, 'storage-write'],
  ['read exception', { reject: 'getItem' }, 'storage-read'],
  ['missing persisted value', {}, 'storage-mismatch'],
  ['previous run value', { storage: new Map([['netwiv-sandbox-fixture', 'persisted']]) }, 'storage-mismatch'],
  ['missing sentinel', { storage: new Map([['netwiv-sandbox-fixture', 'current-run']]) }, 'storage-sentinel'],
  ['cleanup exception', { reject: 'removeItem', storage: new Map([
    ['netwiv-sandbox-fixture', 'current-run'], ['netwiv-sandbox-sentinel', 'retained']
  ]) }, 'storage-remove'],
]) {
  test(`guest distinguishes ${label} without reporting private error details`, async () => {
    const result = await fixture(options);
    assert.deepEqual(result.completions, [{ success: false, stage }]);
    assert.equal(result.status.textContent, 'FAIL ' + stage);
  });
}
