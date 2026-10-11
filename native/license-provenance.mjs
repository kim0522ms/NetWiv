import { createHash } from 'node:crypto'
import { readFileSync, readdirSync } from 'node:fs'
import { join, relative } from 'node:path'

// Older Cargo packages lack .cargo_vcs_info.json. A repository revision is only
// usable when every original packaged file matches its recorded Git blob.
export function verifyPackageSource(root, proof) {
  const expected = new Map(proof.files.map((file) => [file.path, file.sha1]))
  if (expected.size === 0 || expected.size !== proof.files.length) {
    throw new Error('package source proof contains no files or duplicate paths')
  }
  let count = 0
  for (const file of readdirSync(root, { recursive: true, withFileTypes: true })) {
    if (file.isDirectory()) continue
    const full = join(file.parentPath, file.name)
    const path = relative(root, full).replaceAll('\\', '/')
    // Cargo normalizes Cargo.toml; Cargo.toml.orig is the repository manifest.
    if (path === 'Cargo.toml' || path === '.cargo-ok') continue
    if (!file.isFile()) throw new Error(`package source is not a regular file: ${path}`)
    const bytes = readFileSync(full)
    const sha1 = createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex')
    if (sha1 !== expected.get(path)) throw new Error(`package source mismatch: ${path}`)
    count++
  }
  if (count !== expected.size) throw new Error('package source proof has missing files')
}
