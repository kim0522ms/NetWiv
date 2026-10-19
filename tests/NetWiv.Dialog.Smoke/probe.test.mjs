import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const source = readFileSync(new URL('./wwwroot/probe.js', import.meta.url), 'utf8').replace(/^import .*\n/, '');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const run = new AsyncFunction('invoke', 'document', source);

async function fixture({ firstSelection = null, firstError = null, failRead = false } = {}) {
  const elements = new Map(['status', 'next', 'finish', 'root', 'instructions', 'visual'].map(id => [id, { checked: false }]));
  const acknowledgments = [], failures = [], verified = [];
  let calls = 0, reads = 0;
  const invoke = async (command, payload) => {
    if (command === 'probe_ready') return { interactive: true, root: '/fixture' };
    if (command === 'probe_interactive_ready') return null;
    if (command === 'plugin:dialog|open') {
      if (calls++ === 0) { if (firstError) throw firstError; return firstSelection; }
      return '/fixture/alpha.txt';
    }
    if (command === 'probe_verify') { verified.push(payload.step); return payload.selected === '/fixture/alpha.txt' ? null : 'selection-shape'; }
    if (command === 'plugin:fs|read_text_file') {
      if (payload.path.includes('/nested/')) throw Error('not granted');
      if (failRead && reads++ === 0) throw Error('private fixture error');
      return 'synthetic dialog sample';
    }
    if (command === 'probe_ack') { acknowledgments.push(payload.step); return null; }
    if (command === 'probe_failure') { failures.push(payload.stage); return null; }
    assert.fail('Unexpected guest command');
  };
  await run(invoke, { querySelector: selector => elements.get(selector.slice(1)) });
  return { elements, acknowledgments, failures, verified, click: () => elements.get('next').onclick() };
}

for (const [label, options, stage] of [
  ['user cancel', {}, 'selection-shape'],
  ['native capability rejection', { firstError: 'Command plugin:dialog|open not allowed by ACL' }, 'native-acl'],
  ['private native error', { firstError: 'private fixture error' }, 'native-invoke'],
  ['Fs read after successful selection verification', { firstSelection: '/fixture/alpha.txt', failRead: true }, 'fs-read'],
]) {
  test(`dialog guest permits correction after ${label} without advancing the host`, async () => {
    const value = await fixture(options);
    await value.click();
    assert.deepEqual(value.acknowledgments, []);
    assert.deepEqual(value.failures, [stage]);
    assert.equal(value.elements.get('next').disabled, false);
    assert.doesNotMatch(value.elements.get('status').textContent, /private fixture error|\/fixture/);
    await value.click();
    assert.deepEqual(value.acknowledgments, [0]);
    assert.equal(value.verified.at(-1), 0);
    assert.match(value.elements.get('instructions').textContent, /BOTH alpha/);
  });
}
