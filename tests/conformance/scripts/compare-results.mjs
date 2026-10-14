import { isDeepStrictEqual } from 'node:util';

export function validateResults(values, expectedCases, platform = process.platform) {
  if (!Array.isArray(expectedCases) || !expectedCases.length ||
      expectedCases.some(name => typeof name !== 'string' || !name) || new Set(expectedCases).size !== expectedCases.length)
    throw Error('Missing or invalid expected fixture inventory');
  if (!Array.isArray(values) || values.some(value => !value || !['case', 'complete'].includes(value.type)))
    throw Error('Unexpected conformance record');
  const cases = values.filter(value => value.type === 'case');
  if (!cases.length) throw Error('Run contains zero cases');
  const names = new Set();
  for (const value of cases) {
    if (!value.name || names.has(value.name)) throw Error('Duplicate or empty case name');
    names.add(value.name);
    if (value.status === 'fail') throw Error(`${value.name} failed: ${value.error ?? 'no error recorded'}`);
    if (value.status === 'skip') {
      const reason = value.reason;
      const desktop = platform === 'win32' ? 'windows' : platform === 'linux' ? 'linux' : platform === 'darwin' ? 'macos' : null;
      if (!desktop || !Array.isArray(reason?.platforms) || !reason.platforms.includes(desktop) ||
          !/^https:\/\/(v2\.tauri\.app|github\.com\/tauri-apps\/)/.test(reason.source ?? ''))
        throw Error(`${value.name} has an unverified availability skip`);
      if (platform === 'darwin' && (value.name !== 'app.website-data-store-sdk-lifecycle' ||
          JSON.stringify(reason?.platforms) !== '["macos"]' ||
          reason?.source !== 'https://github.com/tauri-apps/wry/blob/wry-v0.55.1/src/wkwebview/mod.rs' ||
          reason?.detail !== 'macOS below 14 cannot safely execute the pinned identifier-management selectors; creation falls back to the default store.'))
        throw Error(`${value.name} has an unverified availability skip`);
    } else if (value.status !== 'pass') throw Error(`${value.name} has invalid status`);
  }
  const completions = values.filter(value => value.type === 'complete');
  if (completions.length !== 1 || values.at(-1)?.type !== 'complete' || completions[0].count !== cases.length)
    throw Error('Missing, duplicate or invalid completion record');
  if (expectedCases.some(name => !names.has(name)) || [...names].some(name => !expectedCases.includes(name)))
    throw Error('Executable fixture set differs from source selection');
  return new Map(cases.map(value => [value.name, value]));
}

export function compareResults(reference, candidate, expectedCases, platform = process.platform) {
  const expected = validateResults(reference, expectedCases, platform);
  const actual = validateResults(candidate, expectedCases, platform);
  for (const [name, baseline] of expected) {
    const observed = actual.get(name);
    if (observed.status !== baseline.status) throw Error(`${name}: status differs`);
    if (!isDeepStrictEqual(baseline.status === 'pass' ? observed.value : observed.reason,
        baseline.status === 'pass' ? baseline.value : baseline.reason)) throw Error(`${name}: result differs`);
  }
  return { passed: [...expected.values()].filter(value => value.status === 'pass').length,
    skipped: [...expected.values()].filter(value => value.status === 'skip').length };
}
