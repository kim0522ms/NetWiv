import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, readFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const script = path.join(path.dirname(fileURLToPath(import.meta.url)), 'compare-results.mjs')
const directory = mkdtempSync(path.join(tmpdir(), 'netwiv-compare-'))
const write = (name, values) => {
  const file = path.join(directory, name)
  const metadata = {type:'run', host:name.includes('ref') ? 'reference' : 'netwiv', mode:name.includes('ref') ? 'native' : 'nativeaot', platform:'win32', display:'native', architecture:'x64', suiteSha256:'a'.repeat(64), lockSha256:'b'.repeat(64), sourceSha256:'c'.repeat(64), executableSha256:'e'.repeat(64)}
  metadata.expectedCases = Array.isArray(values) ? [...new Set(values.filter(value => value.type === 'case').map(value => value.name))] : []
  writeFileSync(file, Array.isArray(values) ? `${[metadata, ...values].map(JSON.stringify).join('\n')}\n` : values)
  return file
}
const run = (reference, candidate) => execFileSync(process.execPath, [script, reference, candidate], { encoding: 'utf8', stdio: 'pipe' })
const rejects = (reference, candidate, pattern) => assert.throws(() => run(reference, candidate), pattern)

test('accepts complete matching passing runs', () => {
  const values = [{ type: 'case', name: 'invoke.json', status: 'pass', value: 42 }, { type: 'complete', count: 1 }]
  assert.match(run(write('pass-ref.jsonl', values), write('pass-netwiv.jsonl', values)), /matched 1 reference cases/)
})

test('focused comparisons cannot report the full suite as passed', () => {
  const values = [{ type: 'case', name: 'a', status: 'pass', value: 42 }, { type: 'complete', count: 1 }]
  const files = ['focused-ref.jsonl', 'focused-netwiv.jsonl'].map(name => write(name, values))
  const rewrite = (file, selection, suiteCases) => {
    const rows = readFileSync(file, 'utf8').trim().split('\n').map(JSON.parse)
    Object.assign(rows[0], { selection, suiteCases })
    writeFileSync(file, rows.map(JSON.stringify).join('\n'))
  }
  for (const file of files) rewrite(file, 'focused', ['a', 'b'])
  assert.match(run(...files), /matched 1 reference cases \(focused run of 2; full gate not run\)/)
  rewrite(files[1], 'full', ['a', 'b'])
  rejects(...files, /case selection does not match/)
  rewrite(files[1], 'focused', ['a', 'c'])
  rejects(...files, /provenance differs: suiteCases/)
  rewrite(files[1], 'focused', ['b', 'c'])
  rejects(...files, /invalid full suite inventory/)
})

test('rejects missing or mismatched source provenance', () => {
  const valid = write('provenance-ref.jsonl', [{type:'case',name:'a',status:'pass',value:1},{type:'complete',count:1}])
  rejects(valid, write('no-metadata.jsonl', '{"type":"case","name":"a","status":"pass","value":1}\n{"type":"complete","count":1}\n'), /run metadata/)
  const other = write('different-netwiv.jsonl', [{type:'case',name:'a',status:'pass',value:1},{type:'complete',count:1}])
  const data = [
    {type:'run',host:'netwiv',mode:'nativeaot',platform:'win32',display:'native',architecture:'x64',suiteSha256:'d'.repeat(64),lockSha256:'b'.repeat(64),sourceSha256:'c'.repeat(64),executableSha256:'e'.repeat(64),expectedCases:['a']},
    {type:'case',name:'a',status:'pass',value:1},{type:'complete',count:1}
  ]
  writeFileSync(other, data.map(JSON.stringify).join('\n'))
  rejects(valid, other, /provenance differs: suiteSha256/)
})

test('rejects matching failures', () => {
  const values = [{ type: 'case', name: 'invoke.json', status: 'fail', error: 'same bug' }, { type: 'complete', count: 1 }]
  rejects(write('fail-ref.jsonl', values), write('fail-netwiv.jsonl', values), /failed/)
})

test('rejects matching stale binaries even when their counts and source hashes agree', () => {
  const values = [{type:'case', name:'old', status:'pass', value:1}, {type:'complete', count:1}]
  const files = ['stale-ref.jsonl', 'stale-netwiv.jsonl'].map(name => write(name, values))
  for (const file of files) {
    const lines = readFileSync(file, 'utf8').trim().split('\n').map(JSON.parse)
    lines[0].expectedCases = ['old', 'new']
    writeFileSync(file, lines.map(JSON.stringify).join('\n') + '\n')
  }
  rejects(...files, /fixture set differs from source; missing: new/)
})

test('rejects zero-case and truncated runs', () => {
  const empty = write('empty.jsonl', [{ type: 'complete', count: 0 }])
  const valid = write('valid.jsonl', [{ type: 'case', name: 'a', status: 'pass', value: 1 }, { type: 'complete', count: 1 }])
  rejects(empty, valid, /zero cases/)
  rejects(write('truncated.jsonl', [{ type: 'case', name: 'a', status: 'pass', value: 1 }]), valid, /completion record/)
})

test('rejects duplicate names and malformed JSON', () => {
  const duplicate = write('duplicate.jsonl', [
    { type: 'case', name: 'a', status: 'pass', value: 1 },
    { type: 'case', name: 'a', status: 'pass', value: 1 },
    { type: 'complete', count: 2 }
  ])
  const valid = write('valid-2.jsonl', [{ type: 'case', name: 'a', status: 'pass', value: 1 }, { type: 'complete', count: 1 }])
  rejects(duplicate, valid, /duplicate/)
  rejects(write('malformed.jsonl', '{broken\n'), valid, /malformed JSON/)
})

test('accepts only source-backed platform skips', () => {
  const verified = [
    { type: 'case', name: 'platform.fixture', status: 'skip', reason: { platforms: ['windows'], source: 'https://github.com/tauri-apps/tauri/blob/pinned/path' } },
    { type: 'complete', count: 1 }
  ]
  assert.doesNotThrow(() => run(write('skip-ref.jsonl', verified), write('skip-netwiv.jsonl', verified)))
  const unverified = [{ type: 'case', name: 'tray.event', status: 'skip', reason: 'not implemented' }, { type: 'complete', count: 1 }]
  rejects(write('bad-skip.jsonl', unverified), write('skip-netwiv-2.jsonl', verified), /unverified skip/)
  const wrongPlatform = structuredClone(verified)
  wrongPlatform[0].reason.platforms = ['linux']
  rejects(write('wrong-platform-ref.jsonl',wrongPlatform),write('wrong-platform-netwiv.jsonl',wrongPlatform),/unverified skip/)
})

test('rejects mixed Linux display backends and missing artifact identity', () => {
  const values = [{type:'case',name:'a',status:'pass',value:1},{type:'complete',count:1}]
  const ref = write('display-ref.jsonl',values)
  const candidate = write('display-netwiv.jsonl',values)
  const change = (file, action) => {
    const rows = readFileSync(file,'utf8').trim().split('\n').map(JSON.parse)
    action(rows[0]); writeFileSync(file,rows.map(JSON.stringify).join('\n'))
  }
  change(ref, row=>{row.platform='linux';row.display='x11'})
  change(candidate,row=>{row.platform='linux';row.display='wayland'})
  rejects(ref,candidate,/provenance differs: display/)
  change(candidate,row=>{row.display='x11';delete row.executableSha256})
  rejects(ref,candidate,/invalid executableSha256 provenance/)
})

test('rejects harness errors and unknown records', () => {
  const valid = write('valid-3.jsonl', [{ type: 'case', name: 'a', status: 'pass', value: 1 }, { type: 'complete', count: 1 }])
  const harnessError = write('harness-error.jsonl', [
    { type: 'case', name: 'a', status: 'pass', value: 1 },
    { type: 'harness-error', error: 'timed out' },
    { type: 'complete', count: 1 }
  ])
  rejects(harnessError, valid, /unexpected harness-error record/)
  rejects(write('unknown.jsonl', [{ type: 'wat' }, { type: 'case', name: 'a', status: 'pass', value: 1 }, { type: 'complete', count: 1 }]), valid, /unexpected wat record/)
})
