import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync, spawnSync } from 'node:child_process'
import { cpSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs'
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
        if (process.env.TEST_EXEC_FAIL === '1' ||
            (process.env.TEST_EXEC_FAIL === 'version' && args[0] === '/VERSION'))
          throw new Error('fixture tool failed')
        if (args[0] === '/VERSION') return process.env.TEST_VERSION || 'v3.11\\n'
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

test('NSIS prepares the pinned compiler and exports its directory', t => {
  const f = fixture(t, 'prepare-nsis.mjs')
  const source = readFileSync(path.join(root, 'scripts/prepare-nsis.mjs'), 'utf8')
  const bytes = 'verified NSIS archive fixture'
  // Substitute only the archive checksum; network and subprocess fixtures exercise the script itself.
  const pinnedHash = 'c7d27f780ddb6cffb4730138cd1591e841f4b7edb155856901cdf5f214394fa1'
  assert.ok(source.includes(`const expected = '${pinnedHash}'`))
  f.write('scripts/prepare-nsis.mjs', source.replace(pinnedHash, hash(bytes)))
  f.write('download.bin', bytes)
  const pathFile = path.join(f.directory, 'github-path')
  const result = f.run([], { TEST_PLATFORM: 'win32', TEST_ARCH: 'x64', GITHUB_PATH: pathFile })
  assert.equal(result.status, 0, result.stderr)
  assert.match(result.stdout, /Verified NSIS v3\.11/)
  assert.equal(readFileSync(path.join(f.directory, 'downloads.log'), 'utf8').trim(),
    'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.11/nsis-3.11.zip')
  const commands = readFileSync(path.join(f.directory, 'commands.jsonl'), 'utf8').trim().split('\n').map(JSON.parse)
  assert.equal(commands[0][0], 'pwsh')
  assert.match(commands[0][1].at(-1), /ErrorActionPreference = 'Stop'; Expand-Archive/)
  assert.deepEqual(commands.at(-1)[1], ['/VERSION'])
  assert.equal(readFileSync(pathFile, 'utf8'), `${path.join(realpathSync(f.directory), 'artifacts/tools/nsis-3.11/nsis-3.11')}\n`)
})

for (const [label, env, expected, commandCount] of [
  ['unsupported host', { TEST_PLATFORM: 'linux' }, /supports Windows x64/, 0],
  ['unsupported architecture', { TEST_ARCH: 'arm64' }, /supports Windows x64/, 0],
  ['download failure', { TEST_STATUS: '503' }, /Download failed \(503\)/, 0],
  ['checksum mismatch', { TEST_TAMPER: '1' }, /SHA-256 mismatch/, 0],
  ['extraction failure', { TEST_EXEC_FAIL: '1' }, /fixture tool failed/, 1],
  ['compiler failure', { TEST_EXEC_FAIL: 'version' }, /fixture tool failed/, 2],
  ['wrong compiler version', { TEST_VERSION: 'v3.12' }, /NSIS version mismatch/, 2],
]) {
  test(`NSIS rejects ${label} without exporting its directory`, t => {
    const f = fixture(t, 'prepare-nsis.mjs')
    const source = readFileSync(path.join(root, 'scripts/prepare-nsis.mjs'), 'utf8')
    const bytes = 'verified NSIS archive fixture'
    f.write('scripts/prepare-nsis.mjs', source.replace(/(const expected = ')[a-f0-9]+(')/, `$1${hash(bytes)}$2`))
    f.write('download.bin', env.TEST_TAMPER ? 'corrupt archive' : bytes)
    const pathFile = path.join(f.directory, 'github-path')
    const result = f.run([], { TEST_PLATFORM: 'win32', TEST_ARCH: 'x64', GITHUB_PATH: pathFile, ...env })
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, expected)
    assert.throws(() => readFileSync(pathFile), { code: 'ENOENT' })
    const commands = commandCount ? readFileSync(path.join(f.directory, 'commands.jsonl'), 'utf8').trim().split('\n') : []
    if (!commandCount) assert.throws(() => readFileSync(path.join(f.directory, 'commands.jsonl')), { code: 'ENOENT' })
    assert.equal(commands.length, commandCount)
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
