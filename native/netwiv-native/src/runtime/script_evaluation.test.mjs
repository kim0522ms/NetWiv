import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { createContext, runInContext } from 'node:vm';

const directory = new URL('./', import.meta.url);
const bootstrap = readFileSync(new URL('script_evaluation.js', directory), 'utf8');
const rust = readFileSync(new URL('script_evaluation.rs', directory), 'utf8');
const generator = rust.match(/fn start_script[\s\S]*?\n}/)?.[0];
assert.ok(generator, 'native expression generator should remain in script_evaluation.rs');
let evaluationId = 0;

function createPage() {
  const listeners = new Map();
  let seed = 0;
  const context = createContext({
    crypto: { getRandomValues(bytes) { bytes.fill(++seed); return bytes; } },
    addEventListener(name, callback) { listeners.set(name, callback); },
    setTimeout
  });
  runInContext('globalThis.window = globalThis; window.top = window;', context);
  runInContext(bootstrap, context);
  return {
    context,
    listeners,
    nonce: () => bridgeCall({ context }, 'nonce')
  };
}

function bridgeCall(page, operation, nonce, key, value) {
  page.context.__netwivTestValue = value;
  try {
    return runInContext(
      `globalThis.__NETWIV_EVALUATE__(${JSON.stringify(operation)},${nonce === undefined ? 'undefined' : JSON.stringify(nonce)},${JSON.stringify(key)},globalThis.__netwivTestValue)`,
      page.context);
  } finally { delete page.context.__netwivTestValue; }
}

function poll(page, nonce, key) {
  return bridgeCall(page, 'poll', nonce, key);
}

async function waitForResult(page, nonce, key) {
  const deadline = Date.now() + 1000;
  while (Date.now() < deadline) {
    const encoded = poll(page, nonce, key);
    if (encoded !== null && encoded !== '{"kind":"cancelled"}') return JSON.parse(encoded);
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  assert.fail('script evaluation bridge did not settle');
}

async function evaluate(page, script, key = `__netwiv_eval_test_${++evaluationId}`) {
  const nonce = page.nonce();
  assert.equal(bridgeCall(page, 'prepare', nonce, key), 'started');
  let value;
  try {
    value = runInContext(script, page.context);
  } catch (error) {
    bridgeCall(page, 'reject', nonce, key, error);
    return waitForResult(page, nonce, key);
  }
  bridgeCall(page, 'resolve', nonce, key, value);
  return waitForResult(page, nonce, key);
}

function monkeypatchCapturedIntrinsics(page) {
  runInContext(`
    const fail = () => { throw new Error('patched intrinsic was used'); };
    JSON.stringify = fail;
    Promise.resolve = fail;
    Promise.prototype.then = fail;
    Map.prototype.get = fail;
    Map.prototype.set = fail;
    Map.prototype.delete = fail;
    Map.prototype.clear = fail;
    Object.defineProperty(Map.prototype, 'size', { get: fail });
  `, page.context);
}

test('bootstrap bridge serializes JSON values, undefined, Promise results and BigInt errors', async () => {
  const page = createPage();
  assert.match(page.nonce(), /^[0-9a-f]{64}$/);
  for (const [script, expected] of [
    ['null', null],
    ["'text value'", 'text value'],
    ['true', true],
    ["[1, 'two', null]", [1, 'two', null]],
    ['({ answer: 42, values: [true, null] })', { answer: 42, values: [true, null] }],
    ["new Promise(resolve => setTimeout(() => resolve('awaited'), 5))", 'awaited'],
    ['undefined', null]
  ]) {
    assert.deepEqual(await evaluate(page, script), { kind: 'result', value: expected }, script);
  }

  const bigInt = await evaluate(page, '1n');
  assert.equal(bigInt.kind, 'error');
  assert.equal(bigInt.name, 'TypeError');
  assert.match(bigInt.message, /BigInt/i);
});

test('captured intrinsics survive page monkeypatches made after bootstrap', async () => {
  const page = createPage();
  monkeypatchCapturedIntrinsics(page);
  assert.deepEqual(await evaluate(page, "new Promise(resolve => setTimeout(() => resolve({ ok: true }), 5))"), {
    kind: 'result', value: { ok: true }
  });
  const literal = "__KEY__ __NONCE__ __SCRIPT__ $& $` $' \\";
  assert.deepEqual(await evaluate(page, JSON.stringify(literal)), { kind: 'result', value: literal });
});

test('bridge captures synchronous throws, Promise rejections, and serialization errors', async () => {
  const page = createPage();
  for (const [script, name, message] of [
    ["(() => { throw new RangeError('sync marker'); })()", 'RangeError', 'sync marker'],
    ["Promise.reject(new TypeError('reject marker'))", 'TypeError', 'reject marker'],
    ['(() => { const value = {}; value.self = value; return value; })()', 'TypeError', 'circular']
  ]) {
    const result = await evaluate(page, script, `__netwiv_eval_${name}_${++evaluationId}`);
    assert.equal(result.kind, 'error');
    assert.equal(result.name, name);
    assert.match(result.message, new RegExp(message, 'i'));
    assert.equal(typeof result.stack, 'string');
    assert.notEqual(result.stack, '');
  }
});

test('syntax errors occur before preparation and the native expression source avoids eval', () => {
  const page = createPage();
  const nonce = page.nonce();
  const key = '__netwiv_eval_syntax';
  assert.doesNotMatch(generator, /\beval\s*\(/i);
  assert.doesNotMatch(generator, /\b(?:const|let|var)\s+(?:bridge|started)\b/);
  assert.doesNotMatch(bootstrap, /\beval\s*\(/i);
  assert.throws(() => runInContext('let = ?', page.context), error => error.name === 'SyntaxError');
  assert.equal(poll(page, nonce, key), '{"kind":"cancelled"}');
});

test('expressions can use global names that the native wrapper must not shadow', async () => {
  const page = createPage();
  assert.deepEqual(await evaluate(page, 'globalThis.bridge = 42'), { kind: 'result', value: 42 });
  assert.deepEqual(await evaluate(page, 'bridge'), { kind: 'result', value: 42 });
});

test('pagehide rotates the nonce and retires outstanding bridge entries', () => {
  const page = createPage();
  const oldNonce = page.nonce();
  const key = '__netwiv_eval_stale';
  assert.equal(bridgeCall(page, 'prepare', oldNonce, key), 'started');
  page.listeners.get('pagehide')();
  assert.notEqual(page.nonce(), oldNonce);
  assert.equal(poll(page, oldNonce, key), '{"kind":"cancelled"}');
  assert.equal(bridgeCall(page, 'cancel', oldNonce, key), '{"kind":"cancelled"}');
});

test('delayed Promise results remain pending across repeated polls', async () => {
  const page = createPage();
  const nonce = page.nonce();
  const key = '__netwiv_eval_delayed';
  assert.equal(bridgeCall(page, 'prepare', nonce, key), 'started');
  const value = runInContext("new Promise(resolve => setTimeout(() => resolve('delayed'), 40))", page.context);
  bridgeCall(page, 'resolve', nonce, key, value);
  for (let attempt = 0; attempt < 3; attempt++) {
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(poll(page, nonce, key), null);
  }
  assert.deepEqual(await waitForResult(page, nonce, key), { kind: 'result', value: 'delayed' });
});

test('bridge rejects unknown operations, invalid arguments, duplicate keys, and entries over its cap', () => {
  const page = createPage();
  const nonce = page.nonce();
  const cancelled = '{"kind":"cancelled"}';
  assert.equal(bridgeCall(page, 'unknown', nonce, 'unknown', '1'), cancelled);
  assert.equal(bridgeCall(page, 'prepare', nonce, 1, '1'), cancelled);
  assert.equal(bridgeCall(page, 'prepare', nonce, 'key', '1'), 'started');
  assert.equal(bridgeCall(page, 'prepare', nonce, 'key', '2'), cancelled);
  assert.equal(poll(page, nonce, 'key'), null);
  assert.equal(bridgeCall(page, 'cancel', nonce, 'key'), null);

  for (let index = 0; index < 64; index++)
    assert.equal(bridgeCall(page, 'prepare', nonce, `pending-${index}`), 'started');
  assert.equal(bridgeCall(page, 'prepare', nonce, 'over-cap'), cancelled);
  page.listeners.get('pagehide')();
  assert.notEqual(page.nonce(), nonce);
  assert.equal(bridgeCall(page, 'prepare', nonce, 'old-nonce'), cancelled);
});
