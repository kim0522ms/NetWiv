import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const [rid, directory] = process.argv.slice(2)
if (!['win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'].includes(rid) || !directory)
  throw new Error('usage: node scripts/prepare-native-dependencies.mjs <win-x64|linux-x64|osx-arm64|osx-x64> <output-directory>')

const output = path.resolve(directory)
const lock = JSON.parse(await readFile(path.join(root, 'native/dependency-lock.json'), 'utf8')).libsodiumSource
const hash = bytes => createHash('sha256').update(bytes).digest('hex')
await mkdir(output, { recursive: true })

async function download(url, filename, expected) {
  const destination = path.join(output, filename)
  const existing = await readFile(destination).catch(error => {
    if (error.code !== 'ENOENT') throw error
  })
  if (existing && hash(existing) === expected) return destination
  const response = await fetch(url, { signal: AbortSignal.timeout(120_000) })
  if (!response.ok) throw new Error(`Download failed (${response.status}): ${url}`)
  const bytes = Buffer.from(await response.arrayBuffer())
  if (hash(bytes) !== expected) throw new Error(`SHA-256 mismatch: ${url}`)
  await writeFile(`${destination}.download`, bytes)
  await rename(`${destination}.download`, destination)
  return destination
}

// Use the source bundled in the checksum-pinned crate, not the mutable LATEST URL.
// The crate's build script subsequently verifies the original minisign signature.
const container = lock.sourceContainer
const cargoLock = await readFile(path.join(root, 'native/Cargo.lock'), 'utf8')
const sodiumPackage = cargoLock.split('[[package]]').find(section => section.includes('name = "libsodium-sys-stable"'))
if (!sodiumPackage?.includes(`checksum = "${container.sha256}"`))
  throw new Error('The libsodium source container does not match Cargo.lock')
const archive = await download(container.url, 'libsodium-sys-stable.crate', container.sha256)
// GNU tar (including Git Bash) treats a Windows drive colon as a remote host.
execFileSync('tar', ['-xzf', path.basename(archive), '--strip-components=1',
  `${container.prefix}/LATEST.tar.gz`, `${container.prefix}/LATEST.tar.gz.minisig`], { cwd: output, stdio: 'inherit' })
for (const [filename, expected] of [
  ['LATEST.tar.gz', lock.linuxArchiveSha256],
  ['LATEST.tar.gz.minisig', lock.linuxSignatureSha256],
]) {
  if (hash(await readFile(path.join(output, filename))) !== expected)
    throw new Error(`SHA-256 mismatch: ${filename}`)
}

if (rid === 'win-x64') {
  // Only rename the signed release files to the names expected by the crate.
  // Their bytes and signatures remain unchanged.
  await download(lock.windowsArchiveUrl, 'libsodium-1.0.22-stable-msvc.zip', lock.windowsArchiveSha256)
  await download(`${lock.windowsArchiveUrl}.minisig`, 'libsodium-1.0.22-stable-msvc.zip.minisig', lock.windowsSignatureSha256)
}
console.log(`Verified libsodium inputs for ${rid}: ${output}`)
