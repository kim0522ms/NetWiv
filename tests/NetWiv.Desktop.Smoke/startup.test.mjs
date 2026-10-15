import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const html = readFileSync(new URL('wwwroot/index.html', import.meta.url), 'utf8')
const script = html.match(/<script type="module">([\s\S]*?)<\/script>/)[1]
// Execute the guest itself, supplying its module loader so failures are deterministic.
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
const startup = new AsyncFunction('window', 'document', 'loadCore',
  script.replace("await import('./core.js')", 'await loadCore()'))

function fixture() {
  const stages = [], failures = [], result = {}
  const window = { __TAURI_INTERNALS__: {
    metadata: { currentWebview: { label: 'child' } },
    async invoke(command, payload) {
      if (command === 'startup_stage') stages.push(payload.stage)
      else if (command === 'failed') failures.push(payload.error)
      else throw Error('Unexpected bridge command')
    }
  } }
  return { stages, failures, result, window, document: { querySelector: () => result } }
}

test('module load failure reports through the preinstalled bridge without private error details', async () => {
  const value = fixture()
  await startup(value.window, value.document, async () => { throw Error('private fixture details') })
  assert.deepEqual(value.stages, ['module import'])
  assert.deepEqual(value.failures, ['Guest startup failed during module import'])
  assert.equal(value.result.textContent, value.failures[0])
})

test('metadata failure is handled before loading the guest module', async () => {
  const value = fixture()
  delete value.window.__TAURI_INTERNALS__.metadata
  await startup(value.window, value.document, () => assert.fail('Module must not load'))
  assert.deepEqual(value.failures, ['Guest startup failed during document initialization'])
})

test('a pending guest command leaves its awaited stage available to the host watchdog', { timeout: 1000 }, async () => {
  const value = fixture()
  const entered = Promise.withResolvers()
  let completed = false
  void startup(value.window, value.document, async () => ({
    invoke(command) { entered.resolve(command); return new Promise(() => {}) }
  })).then(() => { completed = true })
  assert.equal(await entered.promise, 'plugin:generated-external|get_status')
  assert.deepEqual(value.stages, ['module import', 'external command'])
  assert.deepEqual(value.failures, [])
  assert.equal(completed, false)
})
