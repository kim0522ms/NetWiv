export function compare(reference, candidate) {
  function validate(cases, host) {
    if (!Array.isArray(cases) || cases.length !== 19) throw new Error(`${host}: expected 19 recorded cases`);
    const names = new Set();
    const counts = { public: 0, 'netwiv-security': 0 };
    for (const result of cases) {
      if (typeof result.name !== 'string' || !result.name || names.has(result.name)) throw new Error(`${host}: missing or duplicate case name`);
      if (!Object.hasOwn(counts, result.scope) || typeof result.passed !== 'boolean') throw new Error(`${host}: invalid case status or scope`);
      if (result.passed === false && typeof result.error !== 'string') throw new Error(`${host}: failed case is missing its error`);
      names.add(result.name); counts[result.scope]++;
    }
    if (counts.public !== 17 || counts['netwiv-security'] !== 2) throw new Error(`${host}: case scope counts changed`);
    return new Map(cases.map(result => [result.name, result]));
  }
  const expected = validate(reference, 'reference');
  const actual = validate(candidate, 'netwiv');
  const failures = [];
  for (const [name, result] of expected) {
    const other = actual.get(name);
    if (!other || other.scope !== result.scope) throw new Error(`Mismatched case: ${name}`);
    if (!other.passed || (result.scope === 'public' && !result.passed)) failures.push({ name, reference: result, netwiv: other });
  }
  return {
    passed: failures.length === 0,
    public: { total: 17, pairedPassed: reference.filter(result => result.scope === 'public' && result.passed && actual.get(result.name).passed).length },
    security: { total: 2, candidatePassed: candidate.filter(result => result.scope === 'netwiv-security' && result.passed).length,
      referenceObservations: reference.filter(result => result.scope === 'netwiv-security') },
    failures
  };
}
