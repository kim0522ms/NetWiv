import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync, spawnSync } from 'node:child_process'
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const hash = bytes => createHash('sha256').update(bytes).digest('hex')

function fixture(t, script) {
  const directory = mkdtempSync(path.join(os.tmpdir(), 'netwiv-ci-tools-'))
  t.after(() => rmSync(directory, { recursive: true, force: true }))
  mkdirSync(path.join(directory, 'scripts'))
  cpSync(path.join(root, 'scripts', script), path.join(directory, 'scripts', script))
  writeFileSync(path.join(directory, 'preload.mjs'), `
    import cp from 'node:child_process'
    import { appendFileSync, readFileSync } from 'node:fs'
    import { syncBuiltinESMExports } from 'node:module'
    if (process.env.TEST_PLATFORM) {
      Object.defineProperty(process, 'platform', { value: process.env.TEST_PLATFORM })
      Object.defineProperty(process, 'arch', { value: process.env.TEST_ARCH })
      cp.execFileSync = (command, args) => {
        appendFileSync('commands.jsonl', JSON.stringify([command, args]) + '\\n')
        if (process.env.TEST_EXEC_FAIL) throw new Error('fixture tool failed')
      }
      syncBuiltinESMExports()
    }
    globalThis.fetch = async url => {
      appendFileSync('downloads.log', url + '\\n')
      return new Response(readFileSync('download.bin'), { status: Number(process.env.TEST_STATUS || 200) })
    }
  `)
  return {
    directory,
    write(name, bytes) { mkdirSync(path.dirname(path.join(directory, name)), { recursive: true }); writeFileSync(path.join(directory, name), bytes) },
    run(args = [], env = {}) {
      return spawnSync(process.execPath, ['--import', './preload.mjs', `scripts/${script}`, ...args], {
        cwd: directory, encoding: 'utf8', env: { ...process.env, ...env },
      })
    },
  }
}

for (const [platform, arch] of [['darwin', 'arm64'], ['darwin', 'x64'], ['win32', 'x64'], ['linux', 'x64']]) {
  test(`minisign prepares ${platform}/${arch} and exports its path`, t => {
    const f = fixture(t, 'prepare-test-tools.mjs')
    const bytes = 'verified archive fixture'
    f.write('download.bin', bytes)
    f.write('packages/cli/native-tools.json', JSON.stringify({ minisign: {
      archives: { win32: hash(bytes), linux: hash(bytes) }, sources: { darwin: hash(bytes) },
    } }))
    const envFile = path.join(f.directory, 'github-env')
    const result = f.run([], { TEST_PLATFORM: platform, TEST_ARCH: arch, GITHUB_ENV: envFile })
    assert.equal(result.status, 0, result.stderr)
    const commands = readFileSync(path.join(f.directory, 'commands.jsonl'), 'utf8').trim().split('\n').map(JSON.parse)
    assert.deepEqual(commands.at(-1)[1], ['-v'])
    assert.match(readFileSync(envFile, 'utf8'), /NETWIV_MINISIGN_PATH=.*minisign/)
    if (platform === 'darwin') {
      assert.ok(commands.some(([command, args]) => command === 'cmake' && args.includes('--build')))
      assert.match(readFileSync(path.join(f.directory, 'downloads.log'), 'utf8'), /minisign-0\.12\.tar\.gz/)
    }
  })
}

for (const [label, env, expected] of [
  ['unsupported host', { TEST_PLATFORM: 'linux', TEST_ARCH: 'arm64' }, /support/],
  ['download failure', { TEST_STATUS: '503' }, /Download failed \(503\)/],
  ['checksum mismatch', {}, /SHA-256 mismatch/],
  ['build failure', { TEST_EXEC_FAIL: '1' }, /fixture tool failed/],
]) {
  test(`minisign rejects ${label}`, t => {
    const f = fixture(t, 'prepare-test-tools.mjs')
    f.write('download.bin', 'archive')
    f.write('packages/cli/native-tools.json', JSON.stringify({ minisign: {
      sources: { darwin: label === 'build failure' ? hash('archive') : 'bad' },
    } }))
    const result = f.run([], { TEST_PLATFORM: 'darwin', TEST_ARCH: 'arm64', ...env })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, expected)
  })
}

for (const rid of ['osx-arm64', 'osx-x64', 'linux-x64', 'win-x64']) {
  test(`libsodium prepares and reuses verified inputs for ${rid}`, t => {
    const f = fixture(t, 'prepare-native-dependencies.mjs')
    f.write('source/LATEST.tar.gz', 'source fixture')
    f.write('source/LATEST.tar.gz.minisig', 'signature fixture')
    execFileSync('tar', ['-czf', 'download.bin', 'source/LATEST.tar.gz', 'source/LATEST.tar.gz.minisig'], { cwd: f.directory })
    const bytes = readFileSync(path.join(f.directory, 'download.bin'))
    const lock = { sourceContainer: { url: 'https://fixture.test/source', prefix: 'source', sha256: hash(bytes) },
      linuxArchiveSha256: hash('source fixture'), linuxSignatureSha256: hash('signature fixture'),
      windowsArchiveUrl: 'https://fixture.test/windows', windowsArchiveSha256: hash(bytes), windowsSignatureSha256: hash(bytes) }
    f.write('native/dependency-lock.json', JSON.stringify({ libsodiumSource: lock }))
    f.write('native/Cargo.lock', `[[package]]\nname = "libsodium-sys-stable"\nchecksum = "${hash(bytes)}"\n`)
    for (let attempt = 0; attempt < 2; attempt++) {
      const result = f.run([rid, 'output'])
      assert.equal(result.status, 0, result.stderr)
      assert.match(result.stdout, new RegExp(`Verified libsodium inputs for ${rid}`))
    }
    assert.equal(readFileSync(path.join(f.directory, 'downloads.log'), 'utf8').trim().split('\n').length, rid === 'win-x64' ? 3 : 1)
    if (rid === 'osx-arm64') {
      const archive = path.join(f.directory, 'output/libsodium-sys-stable.crate')
      rmSync(archive)
      assert.match(f.run([rid, 'output'], { TEST_STATUS: '503' }).stderr, /Download failed \(503\)/)
      f.write('download.bin', 'tampered')
      assert.match(f.run([rid, 'output']).stderr, /SHA-256 mismatch: https:/)
      f.write('download.bin', bytes)
      mkdirSync(archive)
      assert.match(f.run([rid, 'output']).stderr, /EISDIR/)
      rmSync(archive, { recursive: true })
    }
    // A corrupt extracted payload and a divergent Cargo lock must fail closed.
    lock.linuxArchiveSha256 = 'bad'
    f.write('native/dependency-lock.json', JSON.stringify({ libsodiumSource: lock }))
    assert.match(f.run([rid, 'output']).stderr, /SHA-256 mismatch: LATEST.tar.gz/)
    f.write('native/Cargo.lock', '')
    assert.match(f.run([rid, 'output']).stderr, /does not match Cargo.lock/)
  })
}

test('libsodium rejects invalid arguments before preparing files', t => {
  const f = fixture(t, 'prepare-native-dependencies.mjs')
  for (const args of [[], ['osx-arm64'], ['osx-ppc', 'output']]) {
    const result = f.run(args)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /usage:/)
  }
})
