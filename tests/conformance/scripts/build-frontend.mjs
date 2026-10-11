import { createHash } from 'node:crypto'
import { gunzipSync } from 'node:zlib'
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const suite = path.resolve(here, '..')
const repo = path.resolve(suite, '..', '..')
const lock = JSON.parse(await readFile(path.join(repo, 'compatibility', 'upstream-lock.json'), 'utf8'))
const response = await fetch(lock.api.tarball)
if (!response.ok) throw new Error(`failed to fetch ${lock.api.tarball}: ${response.status}`)
const archive = Buffer.from(await response.arrayBuffer())
const [algorithm, digest] = lock.api.integrity.split('-', 2)
const actual = createHash(algorithm).update(archive).digest('base64')
if (actual !== digest) throw new Error('official API tarball integrity mismatch')

const tar = gunzipSync(archive)
const dist = path.join(suite, 'dist')
const vendor = path.join(dist, 'vendor', '@tauri-apps', 'api')
await rm(dist, { recursive: true, force: true })
await mkdir(vendor, { recursive: true })
for (let offset = 0; offset + 512 <= tar.length;) {
  const header = tar.subarray(offset, offset + 512)
  if (header.every(value => value === 0)) break
  const field = (start, length) => header.subarray(start, start + length).toString('utf8').replace(/\0.*$/s, '')
  const name = `${field(345, 155)}${field(345, 155) ? '/' : ''}${field(0, 100)}`
  const size = Number.parseInt(field(124, 12).trim() || '0', 8)
  const type = field(156, 1)
  offset += 512
  if ((type === '' || type === '0') && name.startsWith('package/')) {
    const relative = name.slice('package/'.length)
    if (relative && !relative.includes('..')) {
      const destination = path.join(vendor, relative)
      await mkdir(path.dirname(destination), { recursive: true })
      await writeFile(destination, tar.subarray(offset, offset + size))
    }
  }
  offset += Math.ceil(size / 512) * 512
}
await cp(path.join(suite, 'frontend'), dist, { recursive: true })
console.log(`prepared unchanged ${lock.api.name}@${lock.api.version} in ${dist}`)
