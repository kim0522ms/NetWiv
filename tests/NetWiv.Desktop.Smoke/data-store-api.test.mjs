import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import test from 'node:test'

// Files extracted unchanged from the integrity-verified @tauri-apps/api@2.11.1
// tarball pinned by compatibility/upstream-lock.json. Licenses accompany them.
test('website data-store facade retains the pinned app and image implementations', () => {
  const vendor = new URL('../../templates/frontends/shared/vendor/@netwiv/api/upstream/', import.meta.url)
  for (const [file, expected] of [
    ['app.js', 'ee5d75251f3eae05159aad03c98c436c24cf903acfa6b4733f4d927fc988a77e'],
    ['image.js', 'b4aa38e4047e85d63b34f4614f07a2640e1d6eb7fcfaa4adb1eb2158a990fb67']
  ]) {
    assert.equal(createHash('sha256').update(readFileSync(new URL(file, vendor), 'utf8').replaceAll('\r\n', '\n')).digest('hex'), expected)
  }
})
