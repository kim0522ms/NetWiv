import test from 'node:test';
import assert from 'node:assert/strict';
import { sample, newState, composition, shortcut, missingChecks } from './wwwroot/checks.mjs';

const key = { isTrusted: true, ctrlKey: true, shiftKey: true, code: 'KeyK' };
const confirmed = [true, true, true, true];
const passing = () => ({ ...newState(), menu: 1, tray: 1, keyboard: 1, starts: 1, ends: 1,
  input: 1, resized: 1, blurred: true, refocused: true, maximized: true, restored: true, dragCalls: 1 });

test('focused shortcut rejects synthetic, repeated, composing and unrelated input', () => {
  const state = newState();
  for (const patch of [{ isTrusted: false }, { repeat: true }, { isComposing: true },
    { ctrlKey: false }, { shiftKey: false }, { altKey: true }, { metaKey: true }, { code: 'KeyJ' }]) {
    assert.equal(shortcut(state, { ...key, ...patch }, true), false);
  }
  assert.equal(shortcut(state, key, false), false);
  assert.equal(state.keyboard, 0);
  assert.equal(shortcut(state, key, true), true);
  assert.equal(state.keyboard, 1);
});

test('composition requires trusted start and matching end', () => {
  const state = newState();
  composition(state, 'start', false);
  composition(state, 'end', true);
  assert.equal(state.starts, 0);
  assert.equal(state.ends, 0);
  composition(state, 'start', true);
  composition(state, 'end', false);
  assert.equal(state.composing, true);
  composition(state, 'end', true);
  assert.equal(state.composing, false);
  assert.equal(state.ends, 1);
});

test('fixed text and manual confirmations cannot replace actual input callbacks', () => {
  assert.ok(missingChecks(newState(), sample, confirmed).length > 1);
  const state = passing();
  assert.deepEqual(missingChecks(state, sample, confirmed), []);
  assert.ok(missingChecks(state, 'different text', confirmed).includes('IME composition and exact sample'));
  assert.ok(missingChecks({ ...state, composing: true }, sample, confirmed).includes('IME composition and exact sample'));
  assert.ok(missingChecks({ ...state, input: 0 }, sample, confirmed).includes('IME composition and exact sample'));
  for (const confirmations of [[], [true], [true, true, false, true]])
    assert.ok(missingChecks(state, sample, confirmations).includes('all manual visual confirmations'));
});

test('each native interaction and state observation is required independently', () => {
  for (const field of ['menu', 'tray', 'keyboard', 'starts', 'ends', 'input', 'resized',
    'blurred', 'refocused', 'maximized', 'restored', 'dragCalls']) {
    assert.ok(missingChecks({ ...passing(), [field]: 0 }, sample, confirmed).length > 0, field);
  }
});
