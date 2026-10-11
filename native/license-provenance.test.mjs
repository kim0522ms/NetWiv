import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve, sep } from 'node:path'
import test from 'node:test'
import { verifyPackageSource } from './license-provenance.mjs'

test('native notices cover the pinned graph and preserve every collected text and source proof', () => {
  const native = new URL('./', import.meta.url)
  const inventory = JSON.parse(readFileSync(new URL('third-party-licenses/manifest.json', native)))
  const hash = bytes => createHash('sha256').update(bytes).digest('hex')
  assert.equal(inventory.cargoLockSha256, hash(readFileSync(new URL('Cargo.lock', native))))
  assert.equal(inventory.packageCount, inventory.packages.length)
  assert.equal(inventory.uniqueTextCount, readdirSync(new URL('third-party-licenses/texts/', native)).length)
  for (const pkg of inventory.packages) {
    assert.ok(pkg.files.length > 0, `Missing notices for ${pkg.name}@${pkg.version}`)
    for (const file of pkg.files) {
      const bytes = readFileSync(new URL(`third-party-licenses/texts/${file.sha256}.txt`, native))
      assert.equal(hash(bytes), file.sha256)
      assert.equal(bytes.length, file.bytes)
      if (file.sourceProof) {
        const proof = readFileSync(new URL(`license-overrides/${file.sourceProof.path}`, native))
        assert.equal(hash(proof), file.sourceProof.sha256)
        assert.equal(JSON.parse(proof).revision, file.revision)
      }
    }
  }
})

test('bundled libsodium license matches the pinned source notice', () => {
  const native = new URL('./', import.meta.url)
  const lock = JSON.parse(readFileSync(new URL('dependency-lock.json', native)))
  const license = readFileSync(new URL('LIBSODIUM-LICENSE.txt', native))
  assert.equal(createHash('sha256').update(license).digest('hex'), lock.libsodiumSource.licenseSha256)
  assert.match(license.toString(), /ISC License/)
})

test('legacy source proof rejects changed, missing, extra and duplicate packaged files', () => {
  const root = mkdtempSync(join(tmpdir(), 'netwiv-license-proof-'))
  const files = [{ path: 'Cargo.toml.orig', content: 'original manifest\n' },
    { path: 'src/lib.rs', content: '// original source\n' }]
  const proof = { files: files.map(({ path, content }) => ({ path,
    sha1: createHash('sha1').update(`blob ${Buffer.byteLength(content)}\0`).update(content).digest('hex') })) }
  try {
    mkdirSync(join(root, 'src'))
    for (const file of files) writeFileSync(join(root, file.path), file.content)
    writeFileSync(join(root, 'Cargo.toml'), 'Cargo-generated manifest')
    writeFileSync(join(root, '.cargo-ok'), '')
    verifyPackageSource(root, proof)
    writeFileSync(join(root, 'src/lib.rs'), '// changed source\n')
    assert.throws(() => verifyPackageSource(root, proof), /source mismatch: src\/lib.rs/)
    writeFileSync(join(root, 'src/lib.rs'), files[1].content)
    writeFileSync(join(root, 'src/extra.rs'), 'unrecorded')
    assert.throws(() => verifyPackageSource(root, proof), /source mismatch: src\/extra.rs/)
    rmSync(join(root, 'src/extra.rs'))
    assert.throws(() => verifyPackageSource(root, { files: [...proof.files, proof.files[0]] }), /duplicate/)
    rmSync(join(root, 'src/lib.rs'))
    assert.throws(() => verifyPackageSource(root, proof), /missing files/)
  } finally {
    assert.ok(resolve(root).startsWith(resolve(tmpdir()) + sep))
    rmSync(root, { recursive: true, force: true })
  }
})
