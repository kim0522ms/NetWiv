import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compare } from './compare.mjs';
const cases = () => Array.from({ length: 19 }, (_, index) => ({ name: `case ${index}`, scope: index < 17 ? 'public' : 'netwiv-security', passed: true }));
test('a public failure on both sides never counts as conformance', () => {
  const results = cases(); results[0] = { ...results[0], passed: false, error: 'same failure' };
  assert.equal(compare(results, results).passed, false);
});
test('missing duplicate mismatched and wrongly classified cases are rejected', () => {
  assert.throws(() => compare([], []));
  for (const patch of [{ name: 'case 1' }, { name: 'unexpected' }, { scope: 'netwiv-security' }, { passed: null }]) {
    const results = cases(); results[0] = { ...results[0], ...patch };
    assert.throws(() => compare(cases(), results));
  }
});
test('private upstream behavior stays visible without counting as a public pass', () => {
  const reference = cases(); reference[17] = { ...reference[17], passed: false, error: 'private callback was delivered' };
  const report = compare(reference, cases());
  assert.equal(report.passed, true);
  assert.equal(report.public.pairedPassed, 17);
  assert.equal(report.security.referenceObservations[0].passed, false);
  assert.equal(compare(cases(), reference).passed, false);
});
