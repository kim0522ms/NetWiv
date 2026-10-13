import { createHash } from 'node:crypto'
import { execFileSync, spawn } from 'node:child_process'
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { selectCases } from './select-cases.mjs'
import { startOriginServers } from './origin-server.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const args = process.argv.slice(2)
const separator = args.indexOf('--')
if (separator < 0) throw Error('usage: run-host.mjs <reference|netwiv> <managed|nativeaot|native> <output.jsonl> [--cases name[,name...]] -- <executable> [args]')
const [host, mode, output, ...options] = args.slice(0, separator)
const [executable, ...commandArgs] = args.slice(separator + 1)
if (!['reference', 'netwiv'].includes(host) || !['managed', 'nativeaot', 'native'].includes(mode) || !output || !executable)
  throw Error('A valid host, mode, output and executable are required')
const sha = data => createHash('sha256').update(data).digest('hex')
const fingerprint = async paths => {
  const hash = createHash('sha256')
  for (const name of [...new Set(paths)].sort()) {
    hash.update(name.replaceAll('\\', '/')); hash.update('\0')
    const bytes = await readFile(path.join(root, name))
    // Source fingerprints are portable across Git CRLF checkout policies.
    hash.update(bytes.includes(0) ? bytes : Buffer.from(bytes.toString('utf8').replaceAll('\r\n', '\n')))
    hash.update('\0')
  }
  return hash.digest('hex')
}
const listSourcePaths = () => execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], {cwd: root})
  .toString('utf8').split('\0').filter(Boolean)
const sourcePaths = listSourcePaths()
const suitePaths = sourcePaths.filter(name => name.startsWith('tests/conformance/frontend/'))
const suiteSource = await readFile(path.join(root, 'tests/conformance/frontend/suite.js'), 'utf8')
const suiteCases = [...suiteSource.matchAll(/^await test\(['"]([^'"]+)['"]/gm)].map(match => match[1])
if (!suiteCases.length || new Set(suiteCases).size !== suiteCases.length ||
    [...suiteSource.matchAll(/^await test\(/gm)].length !== suiteCases.length)
  throw Error('Conformance suite must declare unique literal fixture names')
const expectedCases = selectCases(suiteCases, options)
const metadata = {
  type: 'run', host, mode, platform: process.platform, architecture: process.arch,
  expectedCases, suiteCases,
  selection: expectedCases.length === suiteCases.length ? 'full' : 'focused',
  suiteSha256: await fingerprint(suitePaths),
  lockSha256: sha((await readFile(path.join(root, 'compatibility/upstream-lock.json'), 'utf8')).replaceAll('\r\n', '\n')),
  sourceSha256: await fingerprint(sourcePaths),
  display: process.env.GDK_BACKEND || (process.env.WAYLAND_DISPLAY ? 'wayland' : process.env.DISPLAY ? 'x11' : 'native'),
  executableSha256: sha(await readFile(executable))
}
const resultPath = path.resolve(output)
await mkdir(path.dirname(resultPath), {recursive: true})
// Hosts own/truncate this output during execution; provenance is attached after exit.
await writeFile(resultPath, '')
const origins = expectedCases.includes('security.frame-origins') ? await startOriginServers(path.join(root, 'tests/conformance/dist')) : null
const child = spawn(executable, commandArgs, {stdio: 'inherit', env: {
  ...process.env, NETWIV_CONFORMANCE_RESULT: resultPath,
  NETWIV_CONFORMANCE_CASES: options.length ? JSON.stringify(expectedCases) : 'null',
  NETWIV_CONFORMANCE_ROOT: path.join(root, 'tests/conformance')
}})
const result = await new Promise((resolve, reject) => {
  child.once('error', reject)
  child.once('exit', (code, signal) => resolve({code, signal}))
}).finally(() => origins?.close())
const content = await readFile(resultPath, 'utf8').catch(error => {
  if (error.code === 'ENOENT') return ''
  throw error
})
const changed = metadata.sourceSha256 !== await fingerprint(listSourcePaths())
const error = changed ? 'Source files changed during the run; rebuild and rerun the pair from a stable checkout' :
  result.code === 0 ? null : `Process exited with ${result.code ?? result.signal}`
const failure = error ? JSON.stringify({type:'harness-error', error}) + '\n' : ''
await writeFile(resultPath, JSON.stringify(metadata) + '\n' + content.replace(/^\uFEFF/, '') + failure)
process.exitCode = error ? 1 : 0
