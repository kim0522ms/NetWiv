import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import vm from 'node:vm';
import test from 'node:test';
import { getFilePath, invokeJson } from '../../packages/api/lib/core.js';

const read = file => readFile(new URL(file, import.meta.url), 'utf8');
const initialization = await read('../../src/NetWiv.Core/Ipc/InitializationScript.cs');
const transport = await read('../../src/NetWiv.Core/Security/Isolation/transport.js');
const secure = await read('../../src/NetWiv.Core/Security/Isolation/secure.js');
const processMessage = await read('../../src/NetWiv.Core/Security/Isolation/process-ipc-message.js');
const filePathSmoke = await read('../NetWiv.Desktop.Smoke/wwwroot/file-path.html');
const numbers = '{"long":9223372036854775807,"decimal":0.1234567890123456789012345678,"nested":[9007199254740993,null,"escaped \\" string"]}';

// Execute the shipped scripts, with just the browser/native boundary supplied by the test.
async function nativeDropComparison() {
  const events = {};
  const clicks = {};
  const timers = [];
  const reports = [];
  const button = { disabled: true, addEventListener: (name, handler) => { clicks[name] = handler; } };
  const window = {};
  const document = {
    querySelector: selector => selector === '#finish-native' ? button : {},
    addEventListener: (name, handler) => { events[name] = handler; }
  };
  const source = filePathSmoke.split('<script type="module">')[1].split('</script>')[0]
    .replace("import { getFilePath, invoke } from './core.js';", '');
  await vm.runInNewContext(`(async () => { ${source} })()`, {
    window, document, File: class {},
    getFilePath: async () => { throw new Error('not allowed'); },
    invoke: async (command, args) => {
      if (command === 'file_path_config') return { window: 'native', windows: true, fileName: 'sample.txt' };
      reports.push(args);
    },
    setTimeout: callback => timers.push(callback)
  });
  assert.equal(reports.some(value => value.kind === 'failure'), false);
  return { window, button, events, clicks, timers, reports };
}

test('native drop comparison waits for settling and an explicit finish before reporting absence', async () => {
  const runtime = await nativeDropComparison();
  const counts = () => runtime.reports.filter(value => value.kind === 'native-html-drop-count');
  runtime.window.__netwivCheckNativeDomDrop();
  assert.equal(counts().length, 0);
  assert.equal(runtime.button.disabled, true);
  runtime.window.__netwivCheckNativeDomDrop();
  runtime.timers.shift()();
  assert.equal(runtime.button.disabled, true, 'A prior drop cannot enable completion for a newer drop');
  runtime.timers.shift()();
  assert.equal(runtime.button.disabled, false);
  assert.equal(counts().length, 0);
  runtime.clicks.click();
  assert.equal(runtime.button.disabled, true);
  assert.equal(counts().at(-1).count, 0);
  // A DOM event delivered after the zero report must still notify the host of failure.
  runtime.events.drop();
  assert.equal(counts().at(-1).count, 1);
});

test('DOM co-delivery blocks native comparison completion and reports the positive count', async () => {
  const runtime = await nativeDropComparison();
  runtime.window.__netwivCheckNativeDomDrop();
  runtime.events.drop();
  runtime.timers.shift()();
  assert.equal(runtime.button.disabled, true);
  assert.equal(runtime.reports.at(-1).kind, 'native-html-drop-count');
  assert.equal(runtime.reports.at(-1).count, 1);
});

function script(isolated) {
  const result = initialization.split('=> $$"""')[1].split('""";')[0]
    .replaceAll('{{invokeKey}}', 'test-key')
    .replace('{{(isolationScheme is null ? "false" : "true")}}', isolated ? 'true' : 'false')
    .replace(/\{\{\(freezePrototype[^\n]+/, '')
    .replace(/\{\{\(isolationScheme[^\n]+/g, match => match.includes('TransportScript')
      ? (isolated ? transport.replace('__NETWIV_ISOLATION_SCHEME__', '"isolation"') : '') : '');
  assert.ok(!result.includes('{{'));
  return result;
}

function filePathRuntime({ os = 'windows', isolated = false, topFrame = true, fileTransport = true, protocolIpc = false, ids } = {}) {
  const events = {};
  const messages = [];
  const transfers = [];
  class File { constructor(name) { this.name = name; } }
  const bridge = {
    os, protocolIpc,
    postMessage: text => messages.push(JSON.parse(text))
  };
  if (fileTransport) bridge.postFileMessage = (text, file) => transfers.push({ message: JSON.parse(text), file });
  const window = {
    addEventListener: (name, handler) => { events[name] = handler; },
    location: { origin: 'https://app.test' },
    __NETWIV_IPC__: bridge
  };
  window.top = topFrame ? window : { location: { origin: 'https://app.test' } };
  const crypto = ids ? { getRandomValues: value => {
    if (value instanceof Uint32Array) value[0] = ids.shift();
    else value.fill(3);
    return value;
  } } : webcrypto;
  vm.runInNewContext(script(isolated), { window, crypto, ArrayBuffer, Uint8Array, Uint32Array, File });
  return { api: window.__TAURI_INTERNALS__, bridge, events, messages, transfers, File };
}

test('core.getFilePath delegates the File to the installed NetWiv runtime', async () => {
  const file = {};
  globalThis.window = { __TAURI_INTERNALS__: { getFilePath: async value => {
    assert.equal(value, file);
    return 'C:\\picked\\file.txt';
  } } };
  try {
    assert.equal(await getFilePath(file), 'C:\\picked\\file.txt');
  } finally { delete globalThis.window; }
  globalThis.window = { __TAURI_INTERNALS__: {} };
  try { await assert.rejects(getFilePath(file), { message: 'FilePathNotSupported' }); }
  finally { delete globalThis.window; }
});

test('getFilePath preauthorizes, transfers the original File and settles through document-bound callbacks', async () => {
  const runtime = filePathRuntime();
  const file = new runtime.File('dropped.txt');
  const pending = runtime.api.getFilePath(file);
  assert.equal(runtime.messages.length, 1);
  const authorization = runtime.messages[0];
  assert.equal(authorization.cmd, 'plugin:webview|get_file_path');
  assert.equal(authorization.payload.generation, 0);
  assert.equal(typeof authorization.payload.nonce, 'string');
  assert.equal(runtime.transfers.length, 0);
  runtime.api.runCallback(authorization.callback, 'one-use-ticket');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(runtime.transfers.length, 1);
  const transfer = runtime.transfers[0];
  assert.equal(transfer.file, file);
  assert.equal(transfer.message.ticket, 'one-use-ticket');
  assert.equal(transfer.message.nonce, authorization.payload.nonce);
  assert.equal(transfer.message.generation, 0);
  assert.equal(runtime.api.runFileCallback('wrong-nonce', transfer.message.generation,
    transfer.message.callback, 'stale path'), false);
  assert.equal(runtime.api.runFileCallback(transfer.message.nonce, transfer.message.generation,
    transfer.message.callback, 'C:\\picked\\dropped.txt'), true);
  assert.equal(await pending, 'C:\\picked\\dropped.txt');
  assert.equal(runtime.api.callbacks.size, 0);
  assert.equal(runtime.api.runFileCallback(transfer.message.nonce, transfer.message.generation,
    transfer.message.error, 'FilePathUnavailable'), false);
});

test('getFilePath ticket authorization keeps full-source message transport when protocol IPC is enabled', async () => {
  const runtime = filePathRuntime({ protocolIpc: true });
  runtime.bridge.fetchIpc = () => { throw new Error('ticket authorization used protocol IPC'); };
  const pending = runtime.api.getFilePath(new runtime.File('dropped.txt'));
  assert.equal(runtime.messages.length, 1);
  assert.equal(runtime.messages[0].cmd, 'plugin:webview|get_file_path');
  runtime.api.runCallback(runtime.messages[0].callback, 'one-use-ticket');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(runtime.transfers.length, 1);
  const transfer = runtime.transfers[0].message;
  runtime.api.runFileCallback(transfer.nonce, transfer.generation, transfer.callback, 'C:\\picked\\dropped.txt');
  assert.equal(await pending, 'C:\\picked\\dropped.txt');
});

test('getFilePath rejects unsupported contexts and non-File inputs before dispatch', async () => {
  const file = new (filePathRuntime().File)('real.txt');
  for (const options of [
    { os: 'macos' }, { isolated: true }, { topFrame: false }, { fileTransport: false }
  ]) {
    const runtime = filePathRuntime(options);
    await assert.rejects(runtime.api.getFilePath(new runtime.File('real.txt')), { message: 'FilePathNotSupported' });
    assert.equal(runtime.messages.length, 0);
    assert.equal(runtime.transfers.length, 0);
  }
  const runtime = filePathRuntime();
  await assert.rejects(runtime.api.getFilePath(file), { message: 'FilePathUnavailable' });
  assert.equal(runtime.messages.length, 0);
});

test('getFilePath ACL rejection prevents transfer and retires the authorization callbacks', async () => {
  const runtime = filePathRuntime();
  const pending = runtime.api.getFilePath(new runtime.File('dropped.txt'));
  const authorization = runtime.messages[0];
  runtime.api.runCallback(authorization.error, 'permission denied');
  await assert.rejects(pending, error => error === 'permission denied');
  assert.equal(runtime.transfers.length, 0);
  assert.equal(runtime.api.callbacks.size, 0);
});

test('getFilePath native failure rejects and retires its transfer callbacks', async () => {
  const runtime = filePathRuntime();
  const pending = runtime.api.getFilePath(new runtime.File('synthetic.txt'));
  const authorization = runtime.messages[0];
  runtime.api.runCallback(authorization.callback, 'one-use-ticket');
  await new Promise(resolve => setImmediate(resolve));
  const transfer = runtime.transfers[0].message;
  assert.equal(runtime.api.runFileCallback(transfer.nonce, transfer.generation, transfer.error, 'FilePathUnavailable'), true);
  await assert.rejects(pending, error => error === 'FilePathUnavailable');
  assert.equal(runtime.api.callbacks.size, 0);
});

test('getFilePath transport exceptions normalize unexpected errors and preserve unsupported errors', async () => {
  for (const [thrown, expected] of [
    [new TypeError('File cannot be transferred'), 'FilePathUnavailable'],
    [new Error('FilePathNotSupported'), 'FilePathNotSupported']
  ]) {
    const runtime = filePathRuntime();
    runtime.bridge.postFileMessage = () => { throw thrown; };
    const pending = runtime.api.getFilePath(new runtime.File('dropped.txt'));
    runtime.api.runCallback(runtime.messages[0].callback, 'one-use-ticket');
    await assert.rejects(pending, { message: expected });
    assert.equal(runtime.api.callbacks.size, 0);
  }
});

test('getFilePath pagehide before ticket response rejects and prevents a later transfer', async () => {
  const runtime = filePathRuntime();
  const pending = runtime.api.getFilePath(new runtime.File('dropped.txt'));
  const authorization = runtime.messages[0];
  runtime.events.pagehide({ persisted: true });
  await assert.rejects(pending, error => error.name === 'AbortError');
  runtime.api.runCallback(authorization.callback, 'late-ticket');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(runtime.transfers.length, 0);
  assert.equal(runtime.api.callbacks.size, 0);
});

test('getFilePath pagehide during transfer rejects and ignores its late result', async () => {
  const runtime = filePathRuntime();
  const pending = runtime.api.getFilePath(new runtime.File('dropped.txt'));
  const authorization = runtime.messages[0];
  runtime.api.runCallback(authorization.callback, 'one-use-ticket');
  await new Promise(resolve => setImmediate(resolve));
  const transfer = runtime.transfers[0].message;
  runtime.events.pagehide({ persisted: true });
  await assert.rejects(pending, error => error.name === 'AbortError');
  assert.equal(runtime.api.runFileCallback(transfer.nonce, transfer.generation, transfer.callback, 'late path'), false);
  assert.equal(runtime.api.callbacks.size, 0);
});

test('getFilePath rejects stale BFCache callbacks even when callback IDs are reused', async () => {
  const runtime = filePathRuntime({ ids: [1, 2, 1, 2, 1, 2, 1, 2] });
  const first = runtime.api.getFilePath(new runtime.File('first.txt'));
  const firstAuthorization = runtime.messages[0];
  runtime.api.runCallback(firstAuthorization.callback, 'first-ticket');
  await new Promise(resolve => setImmediate(resolve));
  const firstTransfer = runtime.transfers[0].message;
  runtime.events.pagehide({ persisted: true });
  await assert.rejects(first, error => error.name === 'AbortError');
  runtime.events.pageshow({ persisted: true });
  let secondSettled = false;
  const second = runtime.api.getFilePath(new runtime.File('second.txt')).finally(() => { secondSettled = true; });
  const secondAuthorization = runtime.messages[1];
  runtime.api.runCallback(secondAuthorization.callback, 'second-ticket');
  await new Promise(resolve => setImmediate(resolve));
  const secondTransfer = runtime.transfers[1].message;
  assert.equal(firstTransfer.callback, secondTransfer.callback);
  assert.equal(runtime.api.runFileCallback(firstTransfer.nonce, firstTransfer.generation,
    firstTransfer.callback, 'stale path'), false);
  assert.equal(secondSettled, false);
  runtime.api.runFileCallback(secondTransfer.nonce, secondTransfer.generation,
    secondTransfer.callback, 'C:\\picked\\second.txt');
  assert.equal(await second, 'C:\\picked\\second.txt');
});

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
