import assert from 'node:assert/strict';
import test from 'node:test';
import { validateReviewedPort } from './upstream-test-inventory.mjs';

const hash = 'a'.repeat(64);
const port = () => ({ upstreamSourceSha256: hash, classification: 'business-logic', status: 'ported',
  note: 'Fixture assertions preserved.', netwivEvidence: [{file:'tests/test.cs',symbol:'Test',testCase:'upstream test'}] });
test('requires source-bound executable port evidence', () => {
  assert.doesNotThrow(() => validateReviewedPort(port(), hash, 'test'));
  assert.throws(() => validateReviewedPort(port(), 'b'.repeat(64), 'test'), /Re-review/);
  for (const evidence of [undefined, [], [{}], [{file:'a',symbol:'b'}]])
    assert.throws(() => validateReviewedPort({...port(),netwivEvidence:evidence}, hash, 'test'), /evidence/);
});
test('cannot invent a pass or exclude/defer in-scope business behavior', () => {
  for (const status of ['passed', 'complete', '', 'excluded', 'deferred'])
    assert.throws(() => validateReviewedPort({...port(),status}, hash, 'test'));
  assert.throws(() => validateReviewedPort({...port(),classification:'irrelevant'}, hash, 'test'), /classification/);
});
test('reviewed language exclusions and platform deferrals require a rationale', () => {
  for (const [classification,status] of [['language-specification','excluded'],['outside-desktop-milestone','deferred']]) {
    const value = {...port(), classification, status, netwivEvidence:[]};
    assert.doesNotThrow(() => validateReviewedPort(value, hash, 'test'));
    for (const note of [undefined, '', ' ']) assert.throws(() => validateReviewedPort({...value,note}, hash, 'test'), /rationale/);
  }
});
