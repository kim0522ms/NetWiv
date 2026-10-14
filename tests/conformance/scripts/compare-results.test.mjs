import assert from 'node:assert/strict';
import test from 'node:test';
import { compareResults, validateResults } from './compare-results.mjs';

const passing = (name = 'a', value = 42) => [{type:'case', name, status:'pass', value}, {type:'complete', count:1}];
const reason = {platforms:['macos'], source:'https://github.com/tauri-apps/wry/blob/wry-v0.55.1/src/wkwebview/mod.rs',
  detail:'macOS below 14 cannot safely execute the pinned identifier-management selectors; creation falls back to the default store.'};
const skipped = () => [{type:'case', name:'app.website-data-store-sdk-lifecycle', status:'skip', reason}, {type:'complete', count:1}];

test('compares complete matching functional results in memory', () => {
  assert.deepEqual(compareResults(passing(), passing(), ['a']), {passed:1, skipped:0});
  assert.deepEqual(compareResults(passing('a', {one:1, two:2}), passing('a', {two:2, one:1}), ['a']), {passed:1, skipped:0});
});

test('rejects matching failures rather than counting them as parity', () => {
  const values = [{type:'case', name:'a', status:'fail', error:'intentional'}, {type:'complete', count:1}];
  assert.throws(() => compareResults(values, values, ['a']), /failed/);
});

test('rejects zero, missing and unexpected cases including stale executable inventories', () => {
  assert.throws(() => validateResults([{type:'complete', count:0}], ['a']), /zero cases/);
  assert.throws(() => compareResults(passing(), passing(), ['a', 'b']), /differs from source selection/);
  assert.throws(() => compareResults(passing(), passing('b'), ['a']), /differs from source selection/);
});

test('rejects duplicate cases and invalid or incomplete completion records', () => {
  for (const values of [[...passing().slice(0,1), ...passing()], passing().slice(0,1),
    [...passing(), {type:'complete', count:1}], [passing()[1], passing()[0]],
    [passing()[0], {type:'complete', count:2}]])
    assert.throws(() => validateResults(values, ['a']), /Duplicate|completion/);
});

test('rejects malformed records, unknown types, statuses and invalid expected inventories', () => {
  for (const values of [null, [null], [{type:'run'}], [{type:'harness-error'}],
    [{type:'case', name:'a', status:'unknown'}, {type:'complete', count:1}]])
    assert.throws(() => validateResults(values, ['a']), /Unexpected|invalid status/);
  for (const names of [null, [], [''], ['a','a'], [42]])
    assert.throws(() => validateResults(passing(), names), /inventory/);
});

test('rejects unequal public results', () => {
  assert.throws(() => compareResults(passing(), passing('a', 43), ['a']), /result differs/);
});

test('macOS skips require the exact pinned pre-14 availability reason and remain skips', () => {
  const names = ['app.website-data-store-sdk-lifecycle'];
  assert.deepEqual(compareResults(skipped(), skipped(), names, 'darwin'), {passed:0, skipped:1});
  for (const platform of ['win32', 'linux'])
    assert.throws(() => compareResults(skipped(), skipped(), names, platform), /unverified availability/);
  for (const patch of [{name:'app.other'}, {reason:{...reason, source:'https://github.com/tauri-apps/wry/blob/main/src/wkwebview/mod.rs'}},
    {reason:{...reason, detail:'not available'}}, {reason:{...reason, platforms:['macos','linux']}}]) {
    const values = skipped(); Object.assign(values[0], patch);
    assert.throws(() => validateResults(values, [values[0].name], 'darwin'), /unverified availability/);
  }
  assert.throws(() => compareResults(skipped(), passing(names[0]), names, 'darwin'), /status differs/);
});

test('preserves source-backed Windows and Linux platform skips and rejects wrong or missing sources', () => {
  for (const [platform, os] of [['linux','linux'], ['win32','windows']]) {
    const values = [{type:'case', name:'security.frame-origins', status:'skip',
      reason:{platforms:[os], source:'https://github.com/tauri-apps/wry/blob/wry-v0.55.1/src/lib.rs', detail:'Documented platform limitation'}},
      {type:'complete', count:1}];
    assert.deepEqual(compareResults(values, values, ['security.frame-origins'], platform), {passed:0, skipped:1});
    assert.throws(() => validateResults(values, ['security.frame-origins'], platform === 'linux' ? 'win32' : 'linux'), /unverified/);
    const missing = structuredClone(values); delete missing[0].reason.source;
    assert.throws(() => validateResults(missing, ['security.frame-origins'], platform), /unverified/);
  }
});
