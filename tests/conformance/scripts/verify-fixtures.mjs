import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..')
const lock = JSON.parse(await readFile(path.join(repo, 'compatibility/upstream-lock.json'), 'utf8'))
const schema = await readFile(path.join(repo, 'compatibility/generated/tauri.config.schema.json'))
const actual = createHash('sha256').update(schema).digest('hex')
if (actual !== lock.cli.configSchemaSha256) throw new Error(`schema hash mismatch: ${actual}`)
const manifests = ['api-manifest.json', 'cli-manifest.json', 'permissions-manifest.json']
for (const name of manifests) JSON.parse(await readFile(path.join(repo, 'compatibility/generated', name), 'utf8'))

const api = JSON.parse(await readFile(path.join(repo, 'compatibility/generated/api-manifest.json'), 'utf8'))
const declaration = (module, name) => api.declarations.find(value => value.module === module && value.name === name)
const memberNames = value => new Set(value.instanceMembers?.map(member => member.name) ?? [])
const image = declaration('image', 'Image')
if (!image || image.kind !== 'class' || !memberNames(image).has('close') || !memberNames(image).has('rid')) {
  throw new Error('API AST manifest lost Image inheritance from Resource')
}
const webviewWindow = declaration('webviewWindow', 'WebviewWindow')
const webviewWindowMembers = memberNames(webviewWindow ?? {})
if (!webviewWindow || webviewWindow.kind !== 'class' || !webviewWindowMembers.has('scaleFactor') || !webviewWindowMembers.has('setAutoResize') || !webviewWindowMembers.has('close') || !webviewWindow.constructors?.length) {
  throw new Error('API AST manifest lost WebviewWindow class merge or inherited members')
}
for (const value of api.declarations) {
  for (const member of [...(value.instanceMembers ?? []), ...(value.staticMembers ?? [])]) {
    if (member.name.startsWith('#')) throw new Error(`API AST manifest exposed private member ${value.module}.${value.name}.${member.name}`)
  }
}

const cli = JSON.parse(await readFile(path.join(repo, 'compatibility/generated/cli-manifest.json'), 'utf8'))
const expectedCommands = ['init', 'dev', 'build', 'bundle', 'android', 'ios', 'migrate', 'info', 'add', 'remove', 'plugin', 'icon', 'signer', 'completions', 'permission', 'capability', 'inspect']
if (JSON.stringify(cli.commands) !== JSON.stringify(expectedCommands)) throw new Error('CLI command inventory drifted')

const permissions = JSON.parse(await readFile(path.join(repo, 'compatibility/generated/permissions-manifest.json'), 'utf8'))
if (permissions.customApplicationCommands?.localWithoutAppManifest !== 'allowed-by-default' || permissions.customApplicationCommands?.withAnyAppManifest !== 'all-custom-commands-require-resolved-permission' || !permissions.denyPrecedesAllow) {
  throw new Error('custom-command ACL contract drifted')
}
console.log(`verified schema and ${manifests.length} generated manifests`)
