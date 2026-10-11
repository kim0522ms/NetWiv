import { readFile } from 'node:fs/promises'

const [referencePath, candidatePath] = process.argv.slice(2)
if (!referencePath || !candidatePath) throw new Error('usage: compare-results.mjs <reference.jsonl> <candidate.jsonl>')
const load = async file => {
  const text = await readFile(file, 'utf8')
  const lines = text.split(/\r?\n/).filter(Boolean)
  const values = lines.map((line, index) => {
    try { return JSON.parse(line) }
    catch (error) { throw new Error(`${file}:${index + 1}: malformed JSON: ${error.message}`) }
  })
  const unexpected = values.find(value => !['run', 'case', 'complete'].includes(value.type))
  if (unexpected) throw new Error(`${file}: unexpected ${unexpected.type ?? '<missing>'} record`)
  const metadata = values.filter(value => value.type === 'run')
  if (metadata.length !== 1 || values[0]?.type !== 'run') throw new Error(`${file}: expected one initial run metadata record`)
  for (const key of ['suiteSha256', 'lockSha256', 'sourceSha256', 'executableSha256'])
    if (!/^[a-f0-9]{64}$/.test(metadata[0][key] ?? '')) throw new Error(`${file}: invalid ${key} provenance`)
  if (!['win32', 'linux'].includes(metadata[0].platform) || metadata[0].architecture !== 'x64')
    throw new Error(`${file}: unsupported execution platform`)
  const allowedDisplays = metadata[0].platform === 'win32' ? ['native','windows'] : ['x11','wayland']
  if (!allowedDisplays.includes(metadata[0].display)) throw new Error(`${file}: missing or invalid display backend`)
  if (!(metadata[0].host === 'reference' ? ['native'] : ['managed','nativeaot']).includes(metadata[0].mode))
    throw new Error(`${file}: invalid execution mode`)
  const cases = values.filter(value => value.type === 'case')
  if (cases.length === 0) throw new Error(`${file}: run contains zero cases`)
  const names = new Set()
  for (const value of cases) {
    if (!value.name || names.has(value.name)) throw new Error(`${file}: duplicate or empty case name: ${value.name ?? '<empty>'}`)
    names.add(value.name)
    if (value.status === 'fail') throw new Error(`${file}: ${value.name} failed: ${value.error ?? 'no error recorded'}`)
    if (value.status === 'skip') {
      const reason = value.reason
      const platform = metadata[0].platform === 'win32' ? 'windows' : 'linux'
      if (!reason || !Array.isArray(reason.platforms) || !reason.platforms.includes(platform) || !/^https:\/\/(v2\.tauri\.app|github\.com\/tauri-apps\/)/.test(reason.source ?? '')) {
        throw new Error(`${file}: ${value.name} has an unverified skip`)
      }
    } else if (value.status !== 'pass') throw new Error(`${file}: ${value.name} has invalid status ${value.status}`)
  }
  const completions = values.filter(value => value.type === 'complete')
  if (completions.length !== 1) throw new Error(`${file}: expected one completion record, got ${completions.length}`)
  if (completions[0].count !== cases.length) throw new Error(`${file}: completion count ${completions[0].count} does not match ${cases.length} cases`)
  if (values.at(-1)?.type !== 'complete') throw new Error(`${file}: completion record is not last`)
  const expected = metadata[0].expectedCases
  if (!Array.isArray(expected) || expected.length === 0 || expected.some(name => typeof name !== 'string' || !name) ||
      new Set(expected).size !== expected.length) throw new Error(`${file}: missing or invalid expected fixture inventory`)
  const suite = metadata[0].suiteCases
  if (suite !== undefined || metadata[0].selection !== undefined) {
    if (!Array.isArray(suite) || !suite.length || suite.some(name => typeof name !== 'string' || !name) ||
        new Set(suite).size !== suite.length || expected.some(name => !suite.includes(name)))
      throw new Error(`${file}: invalid full suite inventory`)
    if (metadata[0].selection !== (expected.length === suite.length ? 'full' : 'focused'))
      throw new Error(`${file}: case selection does not match full suite inventory`)
  }
  const missing = expected.filter(name => !names.has(name))
  const unexpectedCases = [...names].filter(name => !expected.includes(name))
  if (missing.length || unexpectedCases.length)
    throw new Error(`${file}: executable fixture set differs from source; missing: ${missing.join(', ')}; unexpected: ${unexpectedCases.join(', ')}`)
  return values
}
const [reference, candidate] = await Promise.all([load(referencePath), load(candidatePath)])
if (reference[0].host !== 'reference' || candidate[0].host !== 'netwiv') throw new Error('Expected reference and netwiv host identities')
for (const key of ['suiteSha256', 'lockSha256', 'sourceSha256', 'platform', 'architecture', 'display'])
  if (reference[0][key] !== candidate[0][key]) throw new Error(`Run provenance differs: ${key}`)
if (JSON.stringify([...reference[0].expectedCases].sort()) !== JSON.stringify([...candidate[0].expectedCases].sort()))
  throw new Error('Run provenance differs: expectedCases')
for (const key of ['suiteCases', 'selection'])
  if (JSON.stringify(reference[0][key]) !== JSON.stringify(candidate[0][key]))
    throw new Error(`Run provenance differs: ${key}`)
const byName = values => new Map(values.filter(value => value.type === 'case').map(value => [value.name, value]))
const expected = byName(reference)
const actual = byName(candidate)
const failures = []
for (const [name, baseline] of expected) {
  const observed = actual.get(name)
  if (!observed) failures.push(`${name}: missing candidate result`)
  else if (observed.status !== baseline.status) failures.push(`${name}: expected ${baseline.status}, got ${observed.status}`)
  else if (observed.status === 'pass' && JSON.stringify(observed.value) !== JSON.stringify(baseline.value)) failures.push(`${name}: value differs`)
  else if (observed.status === 'skip' && JSON.stringify(observed.reason) !== JSON.stringify(baseline.reason)) failures.push(`${name}: skip evidence differs`)
}
for (const name of actual.keys()) if (!expected.has(name)) failures.push(`${name}: candidate-only result`)
if (failures.length) {
  console.error(failures.join('\n'))
  process.exitCode = 1
} else console.log(`matched ${expected.size} reference cases${reference[0].selection === 'focused' ? ` (focused run of ${reference[0].suiteCases.length}; full gate not run)` : ''}`)
