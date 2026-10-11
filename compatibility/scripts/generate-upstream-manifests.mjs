import { createHash } from 'node:crypto'
import { gunzipSync } from 'node:zlib'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { generateUpstreamTestInventory } from './upstream-test-inventory.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.resolve(here, '..')
const lock = JSON.parse(await readFile(path.join(root, 'upstream-lock.json'), 'utf8'))
const generated = path.join(root, 'generated')
await mkdir(generated, { recursive: true })

if (process.argv.includes('--tests-only')) {
  await generateUpstreamTestInventory({ lock, root, generated, download, untarGzip })
  process.exit(0)
}

async function download(url) {
  const response = await fetch(url, { redirect: 'follow' })
  if (!response.ok) throw new Error(`download failed (${response.status}): ${url}`)
  return Buffer.from(await response.arrayBuffer())
}

function verifySRI(bytes, expected) {
  const [algorithm, digest] = expected.split('-', 2)
  const actual = createHash(algorithm).update(bytes).digest('base64')
  if (actual !== digest) throw new Error(`integrity mismatch: expected ${expected}, got ${algorithm}-${actual}`)
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex')
}

function untarGzip(bytes) {
  const tar = gunzipSync(bytes)
  const files = new Map()
  for (let offset = 0; offset + 512 <= tar.length;) {
    const header = tar.subarray(offset, offset + 512)
    if (header.every(value => value === 0)) break
    const field = (start, length) => header.subarray(start, start + length).toString('utf8').replace(/\0.*$/s, '')
    const name = `${field(345, 155)}${field(345, 155) ? '/' : ''}${field(0, 100)}`
    const size = Number.parseInt(field(124, 12).trim() || '0', 8)
    const type = field(156, 1)
    offset += 512
    if (type === '' || type === '0') files.set(name, Buffer.from(tar.subarray(offset, offset + size)))
    offset += Math.ceil(size / 512) * 512
  }
  return files
}

function requireFile(files, suffix) {
  const entry = [...files.entries()].find(([name]) => name.endsWith(suffix))
  if (!entry) throw new Error(`archive does not contain ${suffix}`)
  return entry[1]
}

function balancedBody(text, declaration) {
  const start = text.indexOf(declaration)
  if (start < 0) return null
  const open = text.indexOf('{', start)
  let depth = 0
  for (let index = open; index < text.length; index++) {
    if (text[index] === '{') depth++
    if (text[index] === '}' && --depth === 0) return text.slice(open + 1, index)
  }
  return null
}

function apiManifest(files, ts) {
  const publicRootModules = ['.', 'app', 'core', 'dpi', 'event', 'image', 'menu', 'mocks', 'path', 'tray', 'webview', 'webviewWindow', 'window']
  const publicEntryFiles = new Map(publicRootModules.map(module => [`/package/${module === '.' ? 'index' : module}.d.ts`, module]))
  const virtualFiles = new Map()
  for (const [name, bytes] of files) {
    const match = /^package\/(.+)\.d\.ts$/.exec(name)
    if (match) virtualFiles.set(`/package/${match[1]}.d.ts`, bytes.toString('utf8'))
  }
  const options = { noLib: true, skipLibCheck: true, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler, types: [] }
  const host = ts.createCompilerHost(options)
  const normalizePath = file => file.replace(/\\/g, '/')
  const virtualDirectories = new Set(['/'])
  for (const file of virtualFiles.keys()) {
    for (let directory = path.posix.dirname(file); directory !== '/'; directory = path.posix.dirname(directory)) virtualDirectories.add(directory)
  }
  virtualDirectories.add('/package')
  host.getCurrentDirectory = () => '/package'
  host.fileExists = file => virtualFiles.has(normalizePath(file))
  host.readFile = file => virtualFiles.get(normalizePath(file))
  host.directoryExists = directory => virtualDirectories.has(normalizePath(directory))
  host.getDirectories = directory => {
    const prefix = `${normalizePath(directory).replace(/\/$/, '')}/`
    return [...virtualDirectories].filter(value => value.startsWith(prefix) && !value.slice(prefix.length).includes('/'))
  }
  host.realpath = normalizePath
  host.getSourceFile = (file, languageVersion) => {
    const text = host.readFile(file)
    return text === undefined ? undefined : ts.createSourceFile(file, text, languageVersion, true, ts.ScriptKind.TS)
  }
  const program = ts.createProgram([...virtualFiles.keys()], options, host)
  const unresolved = ts.getPreEmitDiagnostics(program).filter(diagnostic => diagnostic.code === 2307)
  if (unresolved.length) throw new Error(`TypeScript could not resolve pinned API declarations: ${unresolved.map(value => value.messageText).join('; ')}`)
  const checker = program.getTypeChecker()
  const declarations = []
  const signature = node => node.getText(node.getSourceFile()).replace(/\s+/g, ' ').trim()
  const visibilityAllows = symbol => !(symbol.getDeclarations() ?? []).some(node =>
    node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.PrivateKeyword || modifier.kind === ts.SyntaxKind.ProtectedKeyword) ||
    ts.getJSDocTags(node).some(tag => tag.tagName.text === 'ignore'))
  const properties = type => checker.getPropertiesOfType(type).filter(symbol => !symbol.getName().startsWith('#') && visibilityAllows(symbol)).map(symbol => ({
    name: symbol.getName(),
    signatures: (symbol.getDeclarations() ?? []).map(signature).sort()
  })).sort((a, b) => a.name.localeCompare(b.name))
  for (const sourceFile of program.getSourceFiles()) {
    const module = publicEntryFiles.get(sourceFile.fileName)
    if (!module) continue
    const moduleSymbol = checker.getSymbolAtLocation(sourceFile)
    if (!moduleSymbol) continue
    for (const exported of checker.getExportsOfModule(moduleSymbol)) {
      if (exported.getName() === 'default') continue
      const symbol = exported.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(exported) : exported
      const nodes = symbol.getDeclarations() ?? []
      const packageNodes = nodes.filter(value => value.getSourceFile().fileName.startsWith('/package/'))
      const node = packageNodes.find(value => ts.isClassDeclaration(value)) ?? packageNodes[0]
      if (!node) continue
      let kind = ts.SyntaxKind[node.kind]
      if (ts.isVariableDeclaration(node)) kind = 'const'
      else if (kind.endsWith('Declaration')) kind = kind.slice(0, -'Declaration'.length).toLowerCase()
      const entry = {
        module,
        name: exported.getName(),
        kind,
        declaredIn: node.getSourceFile().fileName.slice('/package/'.length)
      }
      if (symbol.flags & (ts.SymbolFlags.Class | ts.SymbolFlags.Interface)) {
        const declaredType = checker.getDeclaredTypeOfSymbol(symbol)
        entry.instanceMembers = properties(declaredType)
        if (symbol.flags & ts.SymbolFlags.Class) {
          const staticType = checker.getTypeOfSymbolAtLocation(symbol, node)
          entry.staticMembers = properties(staticType).filter(member => member.name !== 'prototype')
          entry.constructors = checker.getSignaturesOfType(staticType, ts.SignatureKind.Construct).map(value => checker.signatureToString(value)).sort()
        }
      } else entry.signatures = nodes.filter(value => value.getSourceFile().fileName.startsWith('/package/')).map(signature).sort()
      declarations.push(entry)
    }
  }
  declarations.sort((a, b) => `${a.module}/${a.name}/${a.declaredIn}`.localeCompare(`${b.module}/${b.name}/${b.declaredIn}`))
  return {
    generatedFrom: `${lock.api.name}@${lock.api.version}`,
    integrity: lock.api.integrity,
    publicRootModules,
    declarations
  }
}

function cliManifest(files) {
  const lib = requireFile(files, '/crates/tauri-cli/src/lib.rs').toString('utf8')
  const body = balancedBody(lib, 'enum Commands')
  if (!body) throw new Error('could not locate tauri-cli Commands enum')
  const commands = []
  for (const match of body.matchAll(/(?:^|\n)\s*(?:#\[[^\n]+\]\s*)*([A-Z][A-Za-z0-9_]*)\s*(?:\{|\(|,)/g)) {
    const value = match[1].replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase()
    if (!commands.includes(value)) commands.push(value)
  }
  return {
    generatedFrom: `tauri-cli@${lock.cli.version}`,
    commit: lock.cli.commit,
    commands,
    desktopMilestonePolicy: { android: 'recognized-unsupported', ios: 'recognized-unsupported' }
  }
}

function permissionManifest(files) {
  const modules = {}
  for (const [name, bytes] of files) {
    const match = /\/crates\/tauri\/permissions\/([^/]+)\/autogenerated\/reference\.md$/.exec(name)
    if (!match) continue
    const identifiers = [...bytes.toString('utf8').matchAll(/`(core:[^`]+)`/g)].map(value => value[1])
    modules[match[1]] = [...new Set(identifiers)].sort()
  }
  return {
    generatedFrom: `tauri@${lock.runtime.version}`,
    commit: lock.runtime.commit,
    customApplicationCommands: {
      localWithoutAppManifest: 'allowed-by-default',
      remoteWithoutAppManifest: 'denied',
      withAnyAppManifest: 'all-custom-commands-require-resolved-permission'
    },
    denyPrecedesAllow: true,
    modules
  }
}

const [apiTarball, cliTarball, cliSourceArchive, runtimeArchive, typescriptTarball] = await Promise.all([
  download(lock.api.tarball),
  download(lock.cli.tarball),
  download(lock.cli.sourceArchive),
  download(lock.runtime.archive),
  download(lock.tooling.typescript.tarball)
])
verifySRI(apiTarball, lock.api.integrity)
verifySRI(cliTarball, lock.cli.integrity)
verifySRI(typescriptTarball, lock.tooling.typescript.integrity)

const apiFiles = untarGzip(apiTarball)
const cliPackageFiles = untarGzip(cliTarball)
const cliSourceFiles = untarGzip(cliSourceArchive)
const runtimeFiles = untarGzip(runtimeArchive)
const typescriptFiles = untarGzip(typescriptTarball)
const typescriptPath = path.join(os.tmpdir(), `netwiv-typescript-${lock.tooling.typescript.version}.cjs`)
await writeFile(typescriptPath, requireFile(typescriptFiles, '/lib/typescript.js'))
const typescriptModule = await import(pathToFileURL(typescriptPath).href)
const ts = typescriptModule.default ?? typescriptModule
const apiPackage = JSON.parse(requireFile(apiFiles, '/package.json').toString('utf8'))
const cliPackage = JSON.parse(requireFile(cliPackageFiles, '/package.json').toString('utf8'))
if (apiPackage.version !== lock.api.version || cliPackage.version !== lock.cli.version) throw new Error('package version does not match lock')

const schema = requireFile(cliPackageFiles, '/config.schema.json')
if (sha256(schema) !== lock.cli.configSchemaSha256) throw new Error('CLI config schema hash does not match lock')

const outputs = {
  'api-manifest.json': apiManifest(apiFiles, ts),
  'cli-manifest.json': cliManifest(cliSourceFiles),
  'permissions-manifest.json': permissionManifest(runtimeFiles)
}
for (const [name, value] of Object.entries(outputs)) {
  await writeFile(path.join(generated, name), `${JSON.stringify(value, null, 2)}\n`)
}
await writeFile(path.join(generated, 'tauri.config.schema.json'), schema)
console.log(`generated ${Object.keys(outputs).length} manifests and config schema in ${generated}`)
