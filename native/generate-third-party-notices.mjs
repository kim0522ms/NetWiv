import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs'
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { verifyPackageSource } from './license-provenance.mjs'

const nativeRoot = dirname(fileURLToPath(import.meta.url))
const manifest = join(nativeRoot, 'Cargo.toml')
const lockPath = join(nativeRoot, 'Cargo.lock')
const outputPath = join(nativeRoot, 'THIRD-PARTY-NOTICES.md')
const licenseRoot = join(nativeRoot, 'third-party-licenses')
const textRoot = join(licenseRoot, 'texts')
const overrideRoot = join(nativeRoot, 'license-overrides')
const overrides = JSON.parse(readFileSync(join(overrideRoot, 'manifest.json'), 'utf8'))

const metadata = JSON.parse(
  execFileSync('cargo', ['metadata', '--locked', '--format-version', '1', '--manifest-path', manifest], {
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  }),
)
const packages = metadata.packages
  .filter((pkg) => pkg.source?.startsWith('registry+'))
  .sort((left, right) => left.name.localeCompare(right.name) || left.version.localeCompare(right.version))

const lock = readFileSync(lockPath)
const lockSha256 = createHash('sha256').update(lock).digest('hex')
const escapeCell = (value) => String(value).replaceAll('|', '\\|').replaceAll('\n', ' ')
const source = (pkg) => pkg.repository ?? `https://crates.io/crates/${encodeURIComponent(pkg.name)}/${pkg.version}`
const candidateName = /^(?:licen[cs]e|copying|notice|unlicense|copyright)(?:[._-].*)?$/i

rmSync(licenseRoot, { force: true, recursive: true })
mkdirSync(textRoot, { recursive: true })

function collectFiles(root, pkg) {
  const candidates = new Set()
  const addTree = (path) => {
    if (statSync(path).isDirectory()) {
      for (const entry of readdirSync(path).sort()) addTree(join(path, entry))
    } else {
      candidates.add(path)
    }
  }
  for (const entry of readdirSync(root).sort()) {
    if (candidateName.test(entry)) addTree(join(root, entry))
  }
  if (pkg.license_file) {
    const explicit = resolve(root, pkg.license_file)
    const relativeExplicit = relative(root, explicit)
    if (
      !isAbsolute(relativeExplicit) &&
      relativeExplicit !== '..' &&
      !relativeExplicit.startsWith(`..${sep}`)
    ) {
      try {
        addTree(explicit)
      } catch {
        // The manifest records the missing declared file below.
      }
    }
  }
  return [...candidates]
    .sort()
    .map((path) => {
      const bytes = readFileSync(path)
      const sha256 = createHash('sha256').update(bytes).digest('hex')
      const destination = join(textRoot, `${sha256}.txt`)
      try {
        statSync(destination)
      } catch {
        writeFileSync(destination, bytes)
      }
      return {
        path: relative(root, path).split(sep).join('/'),
        sha256,
        bytes: bytes.length,
        origin: 'crate-package',
      }
    })
}

function repositoryFiles(root, pkg) {
  const packageLicense = overrides.packageLicenseTexts?.[`${pkg.name}@${pkg.version}`]
  if (packageLicense) {
    return packageLicense.map((entry) => {
      const bytes = readFileSync(join(overrideRoot, 'texts', `${entry.sha256}.txt`))
      if (createHash('sha256').update(bytes).digest('hex') !== entry.sha256 || bytes.length !== entry.bytes) {
        throw new Error(`license URL override integrity mismatch for ${pkg.name}@${pkg.version}`)
      }
      writeFileSync(join(textRoot, `${entry.sha256}.txt`), bytes)
      return { ...entry, origin: 'license-url' }
    })
  }
  const vcsPath = join(root, '.cargo_vcs_info.json')
  let revision
  let sourceProof
  if (existsSync(vcsPath)) {
    revision = JSON.parse(readFileSync(vcsPath, 'utf8')).git?.sha1
  } else {
    const entry = overrides.packageSources?.[`${pkg.name}@${pkg.version}`]
    if (!entry) return []
    const bytes = readFileSync(join(overrideRoot, entry.path))
    if (createHash('sha256').update(bytes).digest('hex') !== entry.sha256) {
      throw new Error(`package source proof integrity mismatch for ${pkg.name}@${pkg.version}`)
    }
    const proof = JSON.parse(bytes)
    if (proof.name !== pkg.name || proof.version !== pkg.version || proof.repository !== pkg.repository) {
      throw new Error(`package source proof identity mismatch for ${pkg.name}@${pkg.version}`)
    }
    verifyPackageSource(root, proof)
    revision = proof.revision
    sourceProof = entry
  }
  const repository =
    pkg.repository ?? overrides.repositoryOverrides[`${pkg.name}@${pkg.version}`]
  const entries = overrides.sources[`${repository}@${revision}`]
  if (!repository || !revision || !entries) return []
  return entries.map((entry) => {
    const sourcePath = join(overrideRoot, 'texts', `${entry.sha256}.txt`)
    const bytes = readFileSync(sourcePath)
    const sha256 = createHash('sha256').update(bytes).digest('hex')
    if (sha256 !== entry.sha256 || bytes.length !== entry.bytes) {
      throw new Error(`license override integrity mismatch for ${repository}@${revision}/${entry.path}`)
    }
    const destination = join(textRoot, `${sha256}.txt`)
    if (!existsSync(destination)) writeFileSync(destination, bytes)
    return {
      path: entry.path,
      sha256,
      bytes: bytes.length,
      origin: entry.url ? 'license-url' : 'repository',
      repository,
      revision,
      ...(entry.url ? { url: entry.url } : {}),
      ...(sourceProof ? { sourceProof } : {}),
    }
  })
}

const inventory = packages.map((pkg) => {
  const root = dirname(pkg.manifest_path)
  const packageFiles = collectFiles(root, pkg)
  const files = packageFiles.length === 0 ? repositoryFiles(root, pkg) : packageFiles
  return {
    name: pkg.name,
    version: pkg.version,
    license: pkg.license,
    licenseFile: pkg.license_file,
    source: pkg.source,
    repository: pkg.repository,
    files,
    status:
      files.length === 0
        ? 'missing-license-text'
        : packageFiles.length === 0
          ? overrides.packageLicenseTexts?.[`${pkg.name}@${pkg.version}`]
            ? 'collected-from-license-url'
            : 'collected-from-pinned-repository'
          : 'collected-from-crate-package',
  }
})
const textCount = readdirSync(textRoot).length
writeFileSync(
  join(licenseRoot, 'manifest.json'),
  `${JSON.stringify(
    {
      formatVersion: 1,
      cargoLockSha256: lockSha256,
      packageCount: inventory.length,
      uniqueTextCount: textCount,
      packages: inventory,
    },
    null,
    2,
  )}\n`,
)

const lines = [
  '# Native third-party notices',
  '',
  '`netwiv-native` is licensed under Apache-2.0. The dependencies below retain their',
  'respective licenses. This inventory covers every registry package resolved in the pinned',
  '`native/Cargo.lock`, including target-specific and development dependencies.',
  'Verbatim license and NOTICE candidates are stored by SHA-256 under',
  '`native/third-party-licenses/texts`; `manifest.json` maps each package and original filename',
  'to those bytes and explicitly marks packages for which no candidate was found.',
  '',
  `Cargo.lock SHA-256: \`${lockSha256}\``,
  '',
  'Regenerate this file with `node native/generate-third-party-notices.mjs` after changing',
  'the native dependency graph.',
  '',
  '| Package | Version | License | Source | Collected files |',
  '| --- | --- | --- | --- | ---: |',
  ...packages.map(
    (pkg, index) =>
      `| ${escapeCell(pkg.name)} | ${escapeCell(pkg.version)} | ${escapeCell(pkg.license)} | ${escapeCell(source(pkg))} | ${inventory[index].files.length || 'MISSING'} |`,
  ),
  '',
]

writeFileSync(outputPath, lines.join('\n'))
console.log(
  `Wrote ${packages.length} resolved packages and ${textCount} unique license texts to ${outputPath}`,
)
