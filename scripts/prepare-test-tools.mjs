import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { appendFile, mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const windows = process.platform === 'win32'
const macos = process.platform === 'darwin'
if (!(macos ? ['arm64', 'x64'].includes(process.arch) : windows || process.platform === 'linux') ||
    (!macos && process.arch !== 'x64'))
  throw new Error('The pinned test tools support Windows/Linux x64 and macOS arm64/x64 hosts')

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = path.join(root, 'artifacts/tools/minisign-0.12')
const filename = macos ? 'minisign-0.12.tar.gz'
  : windows ? 'minisign-0.12-win64.zip' : 'minisign-0.12-linux.tar.gz'
// The official macOS binary archive is arm64-only. Build the pinned source on both Macs.
// These release archives were verified against the upstream minisign signature.
const lock = JSON.parse(await readFile(path.join(root, 'packages/cli/native-tools.json'), 'utf8')).minisign
const expected = (macos ? lock.sources.darwin : lock.archives[process.platform]).toLowerCase()
const url = `https://github.com/jedisct1/minisign/releases/download/0.12/${filename}`
const response = await fetch(url, { signal: AbortSignal.timeout(120_000) })
if (!response.ok) throw new Error(`Download failed (${response.status}): ${url}`)
const bytes = Buffer.from(await response.arrayBuffer())
if (createHash('sha256').update(bytes).digest('hex') !== expected)
  throw new Error(`SHA-256 mismatch: ${filename}`)
await mkdir(output, { recursive: true })
await writeFile(path.join(output, filename), bytes)
if (macos) {
  const source = path.join(output, 'source')
  await mkdir(source, { recursive: true })
  execFileSync('tar', ['-xzf', path.join(output, filename), '-C', source], { stdio: 'inherit' })
  execFileSync('cmake', ['-S', source, '-B', path.join(output, 'build'),
    '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_POLICY_VERSION_MINIMUM=3.10'], { stdio: 'inherit' })
  execFileSync('cmake', ['--build', path.join(output, 'build'), '--config', 'Release'], { stdio: 'inherit' })
} else if (windows) {
  execFileSync('pwsh', ['-NoProfile', '-Command',
    `Expand-Archive -LiteralPath '${filename}' -DestinationPath . -Force`], { cwd: output, stdio: 'inherit' })
} else {
  execFileSync('tar', ['-xzf', filename], { cwd: output, stdio: 'inherit' })
}
const executable = macos ? path.join(output, 'build/minisign') : path.join(output, windows ? 'minisign-win64' : 'minisign-linux',
  'x86_64', windows ? 'minisign.exe' : 'minisign')
execFileSync(executable, ['-v'], { stdio: 'inherit' })
// Subsequent GitHub Actions steps (including the CLI harness) use this exact tool.
if (process.env.GITHUB_ENV) await appendFile(process.env.GITHUB_ENV, `NETWIV_MINISIGN_PATH=${executable}\n`)
console.log(`NETWIV_MINISIGN_PATH=${executable}`)
