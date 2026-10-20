import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { appendFile, mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { setTimeout } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'

if (process.platform !== 'win32' || process.arch !== 'x64')
  throw new Error('The pinned NSIS tool supports Windows x64 hosts')

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = path.join(root, 'artifacts/tools/nsis-3.11')
const filename = 'nsis-3.11.zip'
const url = `https://downloads.sourceforge.net/project/nsis/NSIS%203/3.11/${filename}`
// SHA-256 published by the official NSIS release file page:
// https://sourceforge.net/projects/nsis/files/NSIS%203/3.11/nsis-3.11.zip/download
const expected = 'c7d27f780ddb6cffb4730138cd1591e841f4b7edb155856901cdf5f214394fa1'
let bytes
for (let attempt = 1; attempt <= 3; attempt++) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(120_000) })
    if (!response.ok) {
      await response.body?.cancel()
      throw new Error(`Download failed (${response.status}): ${url}`, { cause: { status: response.status } })
    }
    bytes = Buffer.from(await response.arrayBuffer())
    break
  } catch (error) {
    const status = error.cause?.status
    const transient = status === 408 || status === 429 || (status >= 500 && status <= 599) ||
      error.name === 'TimeoutError' || error.name === 'AbortError' ||
      ['UND_ERR_CONNECT_TIMEOUT', 'UND_ERR_HEADERS_TIMEOUT', 'UND_ERR_BODY_TIMEOUT',
        'UND_ERR_SOCKET', 'ECONNRESET', 'ETIMEDOUT', 'EAI_AGAIN'].includes(error.cause?.code)
    if (!transient || attempt === 3) throw error
    console.warn(`NSIS download attempt ${attempt}/3 failed temporarily; retrying`)
    await setTimeout(attempt * 1000)
  }
}
if (createHash('sha256').update(bytes).digest('hex') !== expected)
  throw new Error(`SHA-256 mismatch: ${filename}`)
await mkdir(output, { recursive: true })
await writeFile(path.join(output, filename), bytes)
execFileSync('pwsh', ['-NoProfile', '-Command',
  `$ErrorActionPreference = 'Stop'; Expand-Archive -LiteralPath '${filename}' -DestinationPath . -Force`],
  { cwd: output, stdio: 'inherit' })
const directory = path.join(output, 'nsis-3.11')
const version = execFileSync(path.join(directory, 'makensis.exe'), ['/VERSION'], { encoding: 'utf8' }).trim()
if (version !== 'v3.11') throw new Error(`NSIS version mismatch: expected v3.11, got ${version}`)
// Subsequent GitHub Actions steps use the verified portable compiler.
if (process.env.GITHUB_PATH) await appendFile(process.env.GITHUB_PATH, `${directory}\n`)
console.log('Verified NSIS v3.11')
