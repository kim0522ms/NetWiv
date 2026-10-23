import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import vm from 'node:vm';
import test from 'node:test';
import { invokeJson } from '../../packages/api/lib/core.js';

const read = file => readFile(new URL(file, import.meta.url), 'utf8');
const initialization = await read('../../src/NetWiv.Core/Ipc/InitializationScript.cs');
const transport = await read('../../src/NetWiv.Core/Security/Isolation/transport.js');
const secure = await read('../../src/NetWiv.Core/Security/Isolation/secure.js');
const processMessage = await read('../../src/NetWiv.Core/Security/Isolation/process-ipc-message.js');
const numbers = '{"long":9223372036854775807,"decimal":0.1234567890123456789012345678,"nested":[9007199254740993,null,"escaped \\" string"]}';

// Execute the shipped scripts, with just the browser/native boundary supplied by the test.
function script(isolated) {
  const result = initialization.split('=> $$"""')[1].split('""";')[0]
    .replaceAll('{{invokeKey}}', 'test-key')
    .replace(/\{\{\(freezePrototype[^\n]+/, '')
    .replace(/\{\{\(isolationScheme[^\n]+/g, match => match.includes('TransportScript')
      ? (isolated ? transport.replace('__NETWIV_ISOLATION_SCHEME__', '"isolation"') : '') : '');
  assert.ok(!result.includes('{{'));
  return result;
}

for (const protocolIpc of [false, true]) {
  for (const isolated of [false, true]) {
    test(`JSON text round trip: protocol=${protocolIpc}, isolation=${isolated}`, async () => {
      const messages = [];
      const events = {};
      const frameEvents = {};
      const timers = [];
      const keyBytes = new Uint8Array(32).fill(7);
      const key = await webcrypto.subtle.importKey('raw', keyBytes, 'AES-GCM', false, ['decrypt']);
      const window = {
        addEventListener: (name, handler) => { events[name] = handler; },
        __NETWIV_IPC__: { protocolIpc, os: 'windows', postMessage: text => messages.push(JSON.parse(text)) }
      };
      window.top = window;
      const frameWindow = {
        crypto: webcrypto,
        __TAURI_ISOLATION_HOOK__: data => {
          assert.equal(data.payload, numbers);
          // A replacing hook must not control the originating client generation.
          return { ...data, __NETWIV_CLIENT_GENERATION__: 999 };
        },
        addEventListener: (name, handler) => { frameEvents[name] = handler; },
        parent: { postMessage: data => events.message({ source: frameWindow, data }) }
      };
      const frame = { sandbox: { add() {} }, contentWindow: frameWindow };
      frameWindow.postMessage = data => { void frameEvents.message({ origin: 'tauri://localhost', data }); };
      const context = vm.createContext({
        window, crypto: webcrypto, Headers, Uint8Array, Uint32Array, ArrayBuffer,
        fetch() { throw new Error('JSON text must not use the numeric protocol decoder'); },
        document: {
          createElement: tag => tag === 'iframe' ? frame : {},
          head: { append() {} }, body: { append() {} }
        }
      });
      vm.runInContext(script(isolated), context);
      if (isolated) {
        events.DOMContentLoaded();
        const secureScript = secure.replace('__TEMPLATE_origin__', '"tauri://localhost"')
          .replace('__TEMPLATE_runtime_aes_gcm_key__', JSON.stringify([...keyBytes]))
          .replace('__RAW_process_ipc_message_fn__', processMessage.trim());
        vm.runInNewContext(secureScript, {
          window: frameWindow, Uint8Array, ArrayBuffer, TextEncoder,
          document: {}, setTimeout: callback => timers.push(callback)
        });
        while (!timers.length) await new Promise(resolve => setImmediate(resolve));
        timers.shift()();
      }
      globalThis.window = window;
      try {
        const pending = invokeJson('echo', numbers, { headers: { 'x-test': 'custom' } });
        for (let i = 0; !messages.length && i < 100; i++) await new Promise(resolve => setTimeout(resolve, 5));
        assert.equal(messages.length, 1);
        const message = messages.shift();
        assert.equal(message.__NETWIV_CLIENT_GENERATION__, 0);
        assert.equal(message.__NETWIV_JSON__, true);
        assert.equal(message.__NETWIV_INVOKE_KEY__, 'test-key');
        assert.equal(message.options.headers['x-test'], 'custom');
        let payload = message.payload;
        if (isolated) {
          const decrypted = await webcrypto.subtle.decrypt(
            { name: 'AES-GCM', iv: new Uint8Array(payload.nonce) }, key, new Uint8Array(payload.payload));
          payload = JSON.parse(new TextDecoder().decode(decrypted));
        }
        assert.equal(payload, numbers);
        // Native code returns a string literal, not a JavaScript object literal.
        vm.runInContext(`window.__TAURI_INTERNALS__.runCallback(${message.callback},${JSON.stringify(numbers)})`, context);
        assert.equal(await pending, numbers);
        assert.equal(window.__TAURI_INTERNALS__.callbacks.size, 0);
        await assert.rejects(invokeJson('echo', {}), /JSON text/);
        assert.equal(window.__TAURI_INTERNALS__.callbacks.size, 0);
      } finally { delete globalThis.window; }
    });
  }
}

for (const outcome of ['success', 'error']) {
  test(`retired protocol ${outcome} cannot affect reused callback IDs or current transport`, async () => {
    const events = {};
    const responses = [];
    const ids = [1, 2, 1, 2, 3, 4];
    const window = {
      addEventListener: (name, handler) => { events[name] = handler; },
      __NETWIV_IPC__: {
        os: 'macos', protocolIpc: true,
        postMessage() { assert.fail('A retired failure disabled the current protocol'); },
        fetchIpc: () => new Promise((resolve, reject) => responses.push({ resolve, reject }))
      }
    };
    window.top = window;
    vm.runInNewContext(script(false), { window, Headers, ArrayBuffer, Uint8Array,
      crypto: { getRandomValues: value => { value[0] = ids.shift(); return value; } } });
    const api = window.__TAURI_INTERNALS__;
    const retired = api.invoke('retired');
    events.pagehide();
    await assert.rejects(retired, error => error.name === 'AbortError');
    events.pageshow();
    let settled = false;
    const fresh = api.invoke('fresh').finally(() => { settled = true; });
    const response = value => ({ headers: new Headers({ 'Tauri-Response': 'ok', 'content-type': 'application/json' }), json: async () => value });
    if (outcome === 'success') responses[0].resolve(response('retired'));
    else responses[0].reject(new Error('retired failure'));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(settled, false);
    assert.equal(api.callbacks.size, 2);
    responses[1].resolve(response('fresh'));
    assert.equal(await fresh, 'fresh');
    const next = api.invoke('next');
    responses[2].resolve(response('next'));
    assert.equal(await next, 'next');
    assert.equal(api.callbacks.size, 0);
  });
}

test('older runtime fails explicitly and the Blazor bridge has no numeric conversion', async () => {
  globalThis.window = { __TAURI_INTERNALS__: {} };
  try { await assert.rejects(invokeJson('echo', numbers), /does not support invokeJson/); }
  finally { delete globalThis.window; }
  const bridge = await read('../../templates/frontends/blazor/frontend/wwwroot/netwiv.js');
  assert.match(bridge, /export \{ invokeJson \} from/);
  assert.doesNotMatch(bridge, /JSON\.(parse|stringify)/);
});

for (const failure of ['fetch', 'json', 'text', 'arrayBuffer']) {
  test(`protocol ${failure} rejection never replays a dispatched command`, async () => {
    let executions = 0;
    let posted = 0;
    let finish;
    const inFlight = new Promise(resolve => { finish = resolve; });
    const error = new TypeError('response unavailable');
    const window = { addEventListener() {}, location: { origin: 'netwiv://localhost' }, __NETWIV_IPC__: {
      os: 'macos', protocolIpc: true,
      fetchIpc: async () => {
        executions++;
        // A transport failure does not cancel already-dispatched native work.
        void inFlight.then(() => { executions += 10; });
        if (failure === 'fetch') throw error;
        return {
          headers: new Headers({ 'Tauri-Response': 'ok', 'content-type':
            failure === 'json' ? 'application/json' : failure === 'text' ? 'text/plain' : 'application/octet-stream' }),
          [failure]: async () => { throw error; }
        };
      },
      postMessage: text => {
        posted++;
        const message = JSON.parse(text);
        assert.equal(message.cmd, 'nextCommand');
        assert.equal(message.options.customProtocolIpcBlocked, true);
        window.__TAURI_INTERNALS__.runCallback(message.callback, 'next result');
      }
    }};
    window.top = window;
    vm.runInNewContext(script(false), { window, crypto: webcrypto, Headers, URL, ArrayBuffer, Uint8Array, Map });
    const api = window.__TAURI_INTERNALS__;
    await assert.rejects(api.invoke('increment'), rejected => rejected === error);
    assert.equal(executions, 1);
    assert.equal(posted, 0);
    assert.equal(api.callbacks.size, 0);
    assert.equal(await api.invoke('nextCommand'), 'next result');
    assert.equal(posted, 1);
    assert.equal(api.callbacks.size, 0);
    finish();
    await inFlight;
    assert.equal(executions, 11, 'The original in-flight operation must finish only once');
  });
}

test('known unavailable protocol uses message transport before dispatch', async () => {
  let executions = 0;
  const window = { addEventListener() {}, __NETWIV_IPC__: { os: 'macos', protocolIpc: false,
    fetchIpc: () => { throw new Error('An unavailable protocol must not be attempted'); },
    postMessage: text => {
      executions++;
      const message = JSON.parse(text);
      window.__TAURI_INTERNALS__.runCallback(message.callback, executions);
    }
  }};
  window.top = window;
  vm.runInNewContext(script(false), { window, crypto: webcrypto, Headers, URL, ArrayBuffer, Uint8Array, Map });
  assert.equal(await window.__TAURI_INTERNALS__.invoke('increment'), 1);
  assert.equal(executions, 1);
  assert.equal(window.__TAURI_INTERNALS__.callbacks.size, 0);
});

for (const mode of ['message', 'json', 'protocol']) {
  test(`pagehide rejects pending ${mode} calls and ignores late completion`, async () => {
    const events = {};
    const messages = [];
    const responses = [];
    const window = {
      addEventListener: (name, handler) => { events[name] = handler; },
      __NETWIV_IPC__: {
        os: 'macos', protocolIpc: mode === 'protocol',
        postMessage: text => messages.push(JSON.parse(text)),
        fetchIpc: () => new Promise(resolve => responses.push(resolve))
      }
    };
    window.top = window;
    vm.runInNewContext(script(false), { window, crypto: webcrypto, Headers, ArrayBuffer, Uint8Array });
    const api = window.__TAURI_INTERNALS__;
    const invoke = () => mode === 'json' ? api.invokeJson('pending') : api.invoke('pending');
    let settlements = 0;
    const pending = invoke().finally(() => { settlements++; });
    const oldCallbacks = [...api.callbacks.keys()];
    const persistentCallback = api.transformCallback(() => assert.fail('retired channel callback ran'));
    events.pagehide({ persisted: true });
    await assert.rejects(pending, error => error.name === 'AbortError');
    // A later pagehide handler must not create a fresh unresolved invocation.
    await assert.rejects(invoke(), error => error.name === 'AbortError');
    assert.equal(api.callbacks.size, 0);
    events.pagehide({ persisted: true });
    api.runCallback(persistentCallback, 'late channel');
    for (const callback of oldCallbacks) api.runCallback(callback, 'late reply');
    if (mode === 'protocol') {
      responses.shift()({ headers: new Headers({ 'Tauri-Response': 'ok', 'content-type': 'application/json' }), json: async () => 'late reply' });
      await new Promise(resolve => setImmediate(resolve));
    }
    assert.equal(settlements, 1);
    assert.equal(api.callbacks.size, 0);
    events.pageshow({ persisted: true });
    const fresh = invoke();
    if (mode === 'protocol') {
      responses.shift()({ headers: new Headers({ 'Tauri-Response': 'ok', 'content-type': 'application/json' }), json: async () => 'fresh' });
    } else api.runCallback(messages.at(-1).callback, 'fresh');
    assert.equal(await fresh, 'fresh');
    assert.equal(api.callbacks.size, 0);
    assert.equal(mode === 'protocol' ? messages.length : messages.length - 2, 0);
  });
}

for (const ready of [false, true]) {
  test(`pagehide discards isolation work before native dispatch: ready=${ready}`, async () => {
    const events = new Map();
    const fire = (name, event) => { for (const handler of events.get(name) || []) handler(event); };
    const messages = [];
    const prepared = [];
    const ids = [1, 2, 1, 2];
    const frameWindow = { postMessage: message => prepared.push(message) };
    const window = {
      addEventListener: (name, handler) => events.set(name, [...(events.get(name) || []), handler]),
      __NETWIV_IPC__: { os: 'macos', protocolIpc: false, postMessage: text => messages.push(JSON.parse(text)) }
    };
    window.top = window;
    vm.runInNewContext(script(true), {
      window, crypto: { getRandomValues: value => { value[0] = ids.shift(); return value; } }, Headers, ArrayBuffer, Uint8Array,
      document: {
        createElement: tag => tag === 'iframe' ? { sandbox: { add() {} }, contentWindow: frameWindow } : {},
        head: { append() {} }, body: { append() {} }
      }
    });
    fire('DOMContentLoaded');
    if (ready) fire('message', { source: frameWindow, data: '__TAURI_ISOLATION_READY__' });
    const api = window.__TAURI_INTERNALS__;
    const pending = api.invoke('retired');
    const [callback, error] = api.callbacks.keys();
    const retiredMessage = ready ? prepared[0] : { cmd: 'retired', callback, error, __NETWIV_CLIENT_GENERATION__: 0 };
    let late;
    window.addEventListener('pagehide', () => { late = api.invoke('late'); });
    fire('pagehide', { persisted: true });
    await assert.rejects(pending, rejected => rejected.name === 'AbortError');
    await assert.rejects(late, rejected => rejected.name === 'AbortError');
    fire('message', { source: frameWindow, data: '__TAURI_ISOLATION_READY__' });
    assert.equal(prepared.length, ready ? 1 : 0);
    // An encryption operation already in flight may still return from the secure frame.
    const envelope = { nonce: [1], payload: [2], contentType: 'application/json' };
    fire('message', { source: frameWindow, data: { ...retiredMessage, payload: envelope } });
    assert.equal(messages.length, 0);
    assert.equal(api.callbacks.size, 0);
    fire('pageshow', { persisted: true });
    const fresh = api.invoke('fresh');
    fire('message', { source: frameWindow, data: { ...retiredMessage, payload: envelope } });
    assert.equal(messages.length, 0, 'Retired preparation reused a fresh invocation callback ID');
    assert.equal(api.callbacks.size, 2);
    const message = prepared.at(-1);
    fire('message', { source: frameWindow, data: { ...message, payload: envelope } });
    assert.equal(messages.length, 1);
    api.runCallback(messages[0].callback, 'fresh result');
    assert.equal(await fresh, 'fresh result');
    assert.equal(api.callbacks.size, 0);
  });
}
