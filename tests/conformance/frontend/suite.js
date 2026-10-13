import { getIdentifier, getName, getTauriVersion, getVersion, getBundleType, supportsMultipleWindows, fetchDataStoreIdentifiers, removeDataStore, show, hide, setDockVisibility } from '@tauri-apps/api/app'
import { Channel, Resource, SERIALIZE_TO_IPC_FN, invoke, convertFileSrc, isTauri, transformCallback } from '@tauri-apps/api/core'
import { LogicalPosition, LogicalSize, PhysicalPosition, PhysicalSize, Position, Size } from '@tauri-apps/api/dpi'
import { emit, emitTo, listen, once } from '@tauri-apps/api/event'
import { Image } from '@tauri-apps/api/image'
import { Menu, MenuItem, CheckMenuItem, IconMenuItem, PredefinedMenuItem, Submenu } from '@tauri-apps/api/menu'
import { sep, homeDir, isAbsolute, normalize as normalizePath, join, dirname, basename, extname } from '@tauri-apps/api/path'
import * as pathApi from '@tauri-apps/api/path'
import { TrayIcon } from '@tauri-apps/api/tray'
import { WebviewWindow, getAllWebviewWindows } from '@tauri-apps/api/webviewWindow'
import { Webview, getCurrentWebview, getAllWebviews } from '@tauri-apps/api/webview'
import { Window, getCurrentWindow, getAllWindows } from '@tauri-apps/api/window'

const report = document.querySelector('#report')
const results = []
const selectedCases = await invoke('conformance_cases')
if (selectedCases !== null && (!Array.isArray(selectedCases) || !selectedCases.length ||
    selectedCases.some(name => typeof name !== 'string' || !name)))
  throw Error('Invalid harness case selection')
const assert = (condition, message) => { if (!condition) throw new Error(message) }
const until = (predicate, timeoutMs = 2000) => new Promise((resolve, reject) => {
  const deadline = performance.now() + timeoutMs
  const poll = () => {
    if (predicate()) resolve()
    else if (performance.now() >= deadline) reject(new Error(`timed out after ${timeoutMs}ms`))
    else setTimeout(poll, 10)
  }
  poll()
})
const withTimeout = (promise, timeoutMs = 5000) => Promise.race([
  promise,
  new Promise((_, reject) => setTimeout(() => reject(new Error(`timed out after ${timeoutMs}ms`)), timeoutMs))
])
const untilAsync = async (predicate, timeoutMs = 2000) => {
  const deadline = performance.now()+timeoutMs;
  while (!await predicate()) {
    if (performance.now()>=deadline) throw Error(`timed out after ${timeoutMs}ms`);
    await new Promise(resolve=>setTimeout(resolve,10));
  }
}
const created = object => withTimeout(new Promise((resolve,reject) => {
  object.once('tauri://created',()=>resolve(object));
  object.once('tauri://error',event=>reject(event.payload));
}));
const resizeTo = async (window, width, height) => {
  const scale = await window.scaleFactor();
  const expected = [Math.round(width * scale), Math.round(height * scale)];
  await window.setSize(new LogicalSize(width, height));
  try {
    await untilAsync(async () => {
      const size = await window.innerSize();
      return size.width === expected[0] && size.height === expected[1];
    }, 5000);
  } catch (error) { throw Error(`parent expected ${expected}, actual ${JSON.stringify(await window.innerSize())}: ${error}`); }
  return scale;
};
const viewBoundsEqual = async (view, scale, x, y, width, height) => {
  const expected = [x, y, width, height].map(value => Math.round(value * scale));
  const read = async () => {
    const position = await view.position(), size = await view.size();
    return [position.x, position.y, size.width, size.height];
  };
  try { await untilAsync(async () => String(await read()) === String(expected), 5000); }
  catch (error) { throw Error(`webview ${view.label} expected ${expected}, actual ${await read()}: ${error}`); }
};
const rejected = async operation => {
  try { await operation(); }
  catch (error) { return normalize(error); }
  throw Error('operation unexpectedly succeeded');
};
const normalize = value => {
  if (value instanceof ArrayBuffer) return { bytes: [...new Uint8Array(value)] }
  if (ArrayBuffer.isView(value)) return { bytes: [...new Uint8Array(value.buffer, value.byteOffset, value.byteLength)] }
  if (value && typeof value === 'object') {
    if (Array.isArray(value)) return value.map(normalize)
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, normalize(value[key])]))
  }
  return value
}
async function record(result) {
  results.push(result)
  report.textContent = JSON.stringify(results, null, 2)
  await invoke('conformance_record', { result })
}
async function test(name, operation) {
  if (selectedCases !== null && !selectedCases.includes(name)) return
  try { await record({ type: 'case', name, status: 'pass', value: normalize(await operation()) }) }
  catch (error) { await record({ type: 'case', name, status: 'fail', error: String(error) }) }
}

await test('protocol.custom-handlers', async () => {
  const results = [];
  for (const [scheme, status] of [['contract-sync', 201], ['contract-async', 202]]) {
    const response = await withTimeout(fetch(convertFileSrc('echo', scheme), {
      method: 'POST', headers: { 'X-Request': 'probe' }, body: new Uint8Array([0, 255, 42])
    }));
    const bytes = [...new Uint8Array(await response.arrayBuffer())];
    assert(response.status === status && String(bytes) === '0,255,42', 'custom protocol response changed');
    assert(response.headers.get('X-Webview') === 'main' && response.headers.get('X-Method') === 'POST' && response.headers.get('X-Request') === 'probe', 'custom protocol lost context or request headers');
    results.push({ status, bytes });
  }
  return results;
});

await test('protocol.custom-document', async () => {
  let result;
  const unlisten = await listen('custom-protocol-finished', event => { result = event.payload; });
  let child;
  try {
    child = await created(new WebviewWindow('conformance-child', { url: convertFileSrc('page', 'contract-sync'), visible: false }));
    await until(() => result !== undefined, 5000);
    assert(!result.error && result.result === 'custom document IPC' && result.label === 'conformance-child' && String(result.bytes) === '0,255,42', 'custom document IPC or protocol routing failed: ' + JSON.stringify(result));
    return result;
  } finally { unlisten(); if (child) await child.destroy(); }
});

await test('core.convert-file-src-http', async () => {
  const directory = await invoke('asset_fixture');
  const file = await join(directory, 'sample # 한글.txt');
  const request = async (path, options) => {
    const response = await withTimeout(fetch(convertFileSrc(path), options));
    return { status: response.status, type: response.headers.get('content-type'),
      length: response.headers.get('content-length'), range: response.headers.get('content-range'), body: await response.text() };
  };
  const full = await request(file);
  assert(full.status === 200 && full.body === '0123456789' && full.type === 'text/plain', 'file GET mismatch');
  const head = await request(file, {method:'HEAD'});
  assert(head.status === 200 && head.body === '' && head.length === '10', 'file HEAD mismatch');
  const partial = await request(file, {headers:{Range:'bytes=2-4'}});
  assert(partial.status === 206 && partial.body === '234' && partial.range === 'bytes 2-4/10', 'file Range mismatch');
  const preflight = {};
  for (const [name, value] of [['suffix', 'bytes=-3'], ['multipart', 'bytes=0-1,8-9']]) {
    let response;
    try { response = await request(file, {headers:{Range:value}}); }
    catch (error) {
      // WebView2 enforces a preflight here; WebKitGTK's custom protocol allows
      // the request. A timeout or application error is never an accepted result.
      assert(error instanceof TypeError, `${name} failed for a reason other than a fetch boundary: ${error}`);
      preflight[name] = 'rejected';
      continue;
    }
    if (name === 'suffix') assert(response.status === 206 && response.body === '789', 'suffix range mismatch');
    else {
      const boundary = /\r\n--([^\r]+)\r\n/.exec(response.body)?.[1];
      assert(response.status === 200 && boundary && response.body.includes('bytes 8-9/10'), 'multipart response mismatch');
      response.body = response.body.replaceAll(boundary, '<boundary>');
      if (response.type) response.type = response.type.replaceAll(boundary, '<boundary>');
    }
    preflight[name] = response;
  }
  const denied = await request(await join(directory, 'secret.txt'));
  const missing = await request(await join(directory, 'missing.txt'));
  const folder = await request(await join(directory, 'folder'));
  assert(folder.status === 403 || folder.status === 500, 'directory unexpectedly served as a file');
  const nul = await request(file + '\0');
  assert(nul.status === 500, 'invalid NUL file path did not return an input error');
  assert(denied.status === 403 && denied.body === '' && missing.status === 404 && missing.body === '', 'file scope/status mismatch');
  const traversal = await request(directory + sep() + 'child' + sep() + '..' + sep() + 'sample # 한글.txt');
  assert(traversal.status === 403 && traversal.body === '', 'parent traversal bypassed SafePathBuf');
  const binaryResponse = await withTimeout(fetch(convertFileSrc(await join(directory, 'bytes.bin'))));
  const bytes = new Uint8Array(await binaryResponse.arrayBuffer());
  assert(binaryResponse.status === 200 && bytes.length === 256 && bytes.every((value, index) => value === index), 'file binary payload mismatch');
  const binary = {length:bytes.length, type:binaryResponse.headers.get('content-type'), exact:true};
  return {full, head, partial, preflight, denied, missing, traversal, binary, folder, nul};
});
await test('security.literal-command-permissions', async () => {
  let denied = false;
  try { await invoke('restricted'); }
  catch { denied = true; }
  assert(denied, 'literal * command permission granted restricted');
  const echo = await invoke('echo_json', {input: 'exact allow'});
  assert(echo === 'exact allow', 'literal echo_* deny blocked echo_json');
  return {denied, echo};
});
await test('security.permission-set-scopes', async () => {
  const read = await invoke('scope_read');
  const write = await invoke('scope_write');
  assert(JSON.stringify(read.allow) === '["extension","read"]', 'read scope or scope order changed');
  assert(JSON.stringify(write.allow) === '["extension","write"]', 'write inherited another command scope');
  assert(JSON.stringify(read.deny) === '["private"]' && write.deny.length === 0, 'command deny scopes were merged');
  return {read, write};
});
await test('app.metadata', async () => {
  const value = {
    name: await getName(),
    version: await getVersion(),
    identifier: await getIdentifier(),
    tauriVersion: await getTauriVersion()
  }
  assert(value.name === 'NetWiv Conformance', `unexpected app name: ${value.name}`)
  assert(value.version === '0.1.0', `unexpected app version: ${value.version}`)
  assert(value.identifier === 'dev.netwiv.conformance', `unexpected identifier: ${value.identifier}`)
  assert(value.tauriVersion === '2.11.5', `unexpected Tauri compatibility version: ${value.tauriVersion}`)
  return value
})
await test('app.desktop-platform-contract', async () => {
  const bundleType = await getBundleType();
  const multiple = await supportsMultipleWindows();
  const stores = await fetchDataStoreIdentifiers();
  await removeDataStore(Array.from({length:16}, (_, index) => index));
  await show(); await hide(); await setDockVisibility(true);
  assert(bundleType === null && multiple === true && stores.length === 0, 'unexpected non-Apple unbundled app defaults');
  return {bundleType,multiple,stores};
})
await test('dpi.conversion-and-ipc', async () => {
  const logical = new LogicalSize(101, 51);
  const physical = logical.toPhysical(1.5);
  assert(physical.width === 151.5 && physical.height === 76.5, 'guest DPI conversion rounded early');
  const input = {
    logicalSize: new Size(logical), physicalSize: new Size(new PhysicalSize(300, 200)),
    logicalPosition: new Position(new LogicalPosition(-10.5, 22)),
    physicalPosition: new Position(new PhysicalPosition(30, -12)),
    roundTrip: physical.toLogical(1.5)
  };
  const value = await invoke('echo_json', {input});
  assert(value.logicalSize.Logical.width === 101 && value.physicalPosition.Physical.y === -12, 'tagged DPI IPC shape changed');
  return value;
})
await test('dpi.public-shapes-and-conversions', async () => {
  const output = [];
  for (const [Logical, Physical, Wrapper, first, second] of [
    [LogicalSize, PhysicalSize, Size, 'width', 'height'],
    [LogicalPosition, PhysicalPosition, Position, 'x', 'y']
  ]) {
    for (const [Type, type] of [[Logical, 'Logical'], [Physical, 'Physical']]) {
      const coordinates = {[first]: 10.25, [second]: first === 'x' ? -2.5 : 2.5};
      const variants = [new Type(coordinates[first], coordinates[second]), new Type(coordinates), new Type({[type]: coordinates})];
      for (const item of variants) {
        assert(item.type === type && item[first] === coordinates[first] && item[second] === coordinates[second], 'DPI constructor overload changed');
        assert(JSON.stringify(item.toJSON()) === JSON.stringify(coordinates), 'DPI toJSON shape changed');
        assert(JSON.stringify(item[SERIALIZE_TO_IPC_FN]()) === JSON.stringify(coordinates), 'DPI serialization hook changed');
        const wrapper = new Wrapper(item);
        assert((first === 'x' ? wrapper.position : wrapper.size) === item, 'DPI wrapper lost its object');
        assert(JSON.stringify(wrapper.toJSON()) === JSON.stringify({[type]: coordinates}), 'DPI tagged toJSON changed');
        assert(JSON.stringify(wrapper[SERIALIZE_TO_IPC_FN]()) === JSON.stringify({[type]: coordinates}), 'DPI tagged hook changed');
        for (const scale of [0.5, 1, 1.25, 1.5, 2]) {
          const logical = wrapper.toLogical(scale), physical = wrapper.toPhysical(scale);
          assert(logical instanceof Logical && physical instanceof Physical, 'DPI conversion class changed');
          assert(logical[first] === coordinates[first] / (type === 'Physical' ? scale : 1) &&
            physical[second] === coordinates[second] * (type === 'Logical' ? scale : 1), 'DPI conversion values changed');
          assert(type === 'Logical' ? logical === item : physical === item, 'same-unit conversion must retain identity');
          output.push({type, scale, logical, physical, tagged: wrapper});
        }
        item[first] += 1;
        assert(wrapper.toJSON()[type][first] === coordinates[first] + 1, 'DPI fields stopped being mutable');
      }
    }
  }
  const expected = JSON.parse(JSON.stringify(output));
  const echoed = await invoke('echo_json', {input: output});
  assert(JSON.stringify(normalize(echoed)) === JSON.stringify(normalize(expected)), 'DPI JSON and IPC serialization diverged');
  return echoed;
})
await test('mocks.public-functions', async () => {
  // Mocks target a frontend test environment. A worker has no native-injected
  // read-only internals, unlike same-origin iframes in Windows WebView2.
  const worker = new Worker('mocks-worker.js', {type:'module'});
  try {
    return await withTimeout(new Promise((resolve, reject) => {
      worker.onmessage = event => event.data.error ? reject(Error(event.data.error)) : resolve(event.data.value);
      worker.onerror = event => reject(Error(event.message || 'Mocks worker failed'));
    }));
  } finally { worker.terminate(); }
})
await test('plugin.configuration', async () => {
  const config = await invoke('plugin:contract|read_config');
  assert(config.message === '한글 plugin configuration' && config.count === 42, 'typed plugin initialization lost configuration');
  return config;
});
await test('window.effects-validation', async () => {
  const result = [];
  const targets = [getCurrentWindow(), await WebviewWindow.getByLabel('main')];
  assert(targets[1] !== null, 'main WebviewWindow is missing');
  for (const window of targets) {
    for (const value of [undefined, null, {effects: []}, {effects: ['sidebar'], state: 'active', radius: 4.5, color: '#12345678'}]) {
      await window.setEffects(value);
    }
    for (const value of [false, {}, {effects: null}, {effects: ['unknown']}, {effects: [], radius: '4'},
      {effects: [], state: 'unknown'}, {effects: [], color: [256, 0, 0, 255]}, {effects: [], unexpected: true}]) {
      let error;
      try { await window.setEffects(value); } catch (caught) { error = String(caught); }
      assert(error?.startsWith('invalid args `value` for command `set_effects`:'), `invalid effects accepted or wrong failure: ${JSON.stringify(value)} => ${error}`);
      result.push(error);
    }
    await window.clearEffects();
  }
  // WindowOptions must reject malformed effects before a window becomes live.
  let creationError;
  try { await created(new Window('conformance-child', {windowEffects: {effects: [], radius: '4'}})); }
  catch (error) { creationError = String(error); }
  assert(creationError?.includes('invalid args'), `invalid WindowOptions effects were not rejected: ${creationError}`);
  assert(await Window.getByLabel('conformance-child') === null, 'invalid creation leaked a window');
  return {errors: result, creationError};
});
await test('security.frame-origins', async () => {
  const output = {};
  async function probe(kind, base, frame, sandbox = false) {
    const id = `${kind}-${Date.now()}-${Math.random()}`;
    const url = new URL('frame-probe.html', base); url.searchParams.set('id', id);
    let window, iframe;
    if (frame) {
      iframe = document.createElement('iframe');
      if (sandbox) iframe.sandbox = 'allow-scripts';
      iframe.src = url.href; document.body.append(iframe);
    } else window = await created(new WebviewWindow('conformance-child', {url: url.href, visible: false}));
    try {
      let response;
      await untilAsync(async () => {
        response = await (await fetch('http://127.0.0.1:18191/report?id=' + encodeURIComponent(id))).json();
        return response !== null;
      }, 12000);
      assert(!Object.values(response).some(value => typeof value === 'string' && value.startsWith('error:')), `${kind}: ${JSON.stringify(response)}`);
      if (!frame) assert(response.bridge, `${kind}: native bridge missing`);
      if (!frame && kind === 'trusted-window') assert(response.echo === 'allowed', `remote capability positive control failed: ${JSON.stringify(response)}`);
      if (kind === 'untrusted-window' || kind === 'untrusted-frame' || sandbox)
        assert(response.echo !== 'allowed', `untrusted document borrowed parent permissions: ${JSON.stringify(response)}`);
      assert(response.restricted !== 'allowed', `${kind}: restricted command escaped ACL`);
      return response;
    } finally {
      iframe?.remove();
      if (window) await window.destroy();
    }
  }
  output.localControl = await invoke('echo_json', {input: 'local-origin-control'});
  assert(output.localControl === 'local-origin-control', 'local IPC positive control failed');
  output.trustedWindow = await probe('trusted-window', 'http://127.0.0.1:18191/', false);
  output.untrustedWindow = await probe('untrusted-window', 'http://127.0.0.1:18192/', false);
  output.localFrame = await probe('local-frame', location.href, true);
  output.trustedFrame = await probe('trusted-frame', 'http://127.0.0.1:18191/', true);
  output.untrustedFrame = await probe('untrusted-frame', 'http://127.0.0.1:18192/', true);
  output.opaqueFrame = await probe('opaque-frame', 'http://127.0.0.1:18191/', true, true);
  return output;
});
await test('invoke.json', async () => {
  const value = await invoke('echo_json', { input: { answer: 42, nested: ['a', null, true] } })
  assert(value.answer === 42 && value.nested[1] === null, 'JSON value changed across IPC')
  return value
})
await test('invoke.serialization', async () => {
  const custom = { [SERIALIZE_TO_IPC_FN]: () => ({ tagged: 'custom' }) }
  const result = await invoke('echo_json', {
    input: {
      date: new Date('2025-01-02T03:04:05.000Z'),
      map: new Map([['alpha', 1], ['beta', { nested: true }]]),
      int16: new Int16Array([-2, 513]),
      nested: { bytes: new Uint8Array([3, 5, 8]), custom }
    }
  })
  assert(result.date === '2025-01-02T03:04:05.000Z', `Date serialized as ${JSON.stringify(result.date)}`)
  assert(result.map.alpha === 1 && result.map.beta.nested === true, `Map serialized as ${JSON.stringify(result.map)}`)
  assert(result.int16['0'] === -2 && result.int16['1'] === 513, `Int16Array serialized as ${JSON.stringify(result.int16)}`)
  assert(JSON.stringify(result.nested.bytes) === '[3,5,8]', `Uint8Array serialized as ${JSON.stringify(result.nested.bytes)}`)
  assert(result.nested.custom.tagged === 'custom', `custom hook serialized as ${JSON.stringify(result.nested.custom)}`)
  return result
})
await test('invoke.error', async () => {
  try { await invoke('always_fails'); throw new Error('command unexpectedly resolved') }
  catch (error) {
    assert(String(error).includes('intentional conformance error'), `unexpected rejection: ${error}`)
    return { rejected: true, error: String(error) }
  }
})
await test('invoke.unknown-command', async () => {
  const errors = [];
  // These literal keys are allowed by both manifests but have no handlers.
  for (const command of ['*', 'custom_*']) {
    let rejected = false;
    try { await invoke(command); }
    catch (error) {
      rejected = true;
      assert(error === `Command ${command} not found`, `unknown command rejection changed: ${error}`);
      errors.push(error);
    }
    assert(rejected, `${command} unexpectedly resolved`);
  }
  return errors;
})
await test('invoke.invalid-arguments', async () => {
  const reject = async (label, args) => {
    try { await invoke('typed_request', args); throw new Error(`${label} unexpectedly resolved`) }
    catch (error) {
      assert(!String(error).includes('unexpectedly resolved'), String(error))
      return String(error)
    }
  }
  const valid = await invoke('typed_request', { request: { message: 'ok', count: 2 } })
  assert(valid.message === 'ok' && valid.count === 2, `valid DTO changed: ${JSON.stringify(valid)}`)
  return {
    valid,
    missing: await reject('missing request', {}),
    nullValue: await reject('null request', { request: null }),
    wrongScalar: await reject('scalar request', { request: 'wrong' }),
    wrongField: await reject('wrong field types', { request: { message: 1, count: 'two' } }),
    escapedStrings: await Promise.all(['\0\b\f\t\n\r\u001b', '"\\\'한글😀',
      '\u007f\u0085\u00ad\u0345\u200b\u2028\u2029\u2066\ufeff',
      'a\u0301👩\u200d💻\u{10ffff}'].map(count => reject('string count', { request: { message: 'ok', count } })))
  }
})
await test('invoke.numeric-arguments', async () => {
  const valid = [];
  for (const count of [0, -0, 1, 255, 65535, 4294967295]) {
    const value = await invoke('typed_request', {request: {message:'numeric', count}});
    assert(value.message === 'numeric' && value.count === count, 'valid u32 input changed');
    valid.push(value.count);
  }
  const errors = [];
  for (const count of [-1, -0.5, 0.5, 4294967296, 1e-7, 1e-6, 1e20, 1e21,
    1.2345678901234567, 2.2250738585072014e-308, Number.MIN_VALUE]) {
    const error = await rejected(() => invoke('typed_request', {request: {message:'numeric', count}}));
    assert(typeof error === 'string' && error.includes('expected u32'), 'numeric argument rejection changed');
    errors.push(error);
  }
  return {valid, errors};
})
await test('invoke.bytes', async () => {
  const value = await invoke('echo_bytes', { bytes: new Uint8Array([0, 1, 127, 255]) })
  assert(value instanceof ArrayBuffer || ArrayBuffer.isView(value), 'response used JSON instead of a raw IPC body')
  const bytes = value instanceof ArrayBuffer ? [...new Uint8Array(value)] : [...value]
  assert(JSON.stringify(bytes) === '[0,1,127,255]', `raw bytes changed: ${bytes}`)
  return { bytes }
})
await test('channel.ordered', async () => {
  const messages = []
  const initial = []
  const initialHandler = message => initial.push(message)
  const channel = new Channel(initialHandler)
  assert(channel.onmessage === initialHandler, 'Channel constructor lost its handler')
  assert(Number.isInteger(channel.id) && channel.id >= 0 && channel.id <= 0xffffffff, 'invalid Channel callback id')
  assert(SERIALIZE_TO_IPC_FN === '__TAURI_TO_IPC_KEY__', 'public serialization key changed')
  assert(channel[SERIALIZE_TO_IPC_FN]() === `__CHANNEL__:${channel.id}` && channel.toJSON() === channel[SERIALIZE_TO_IPC_FN](), 'Channel serialization disagrees with its id')
  const handler = message => messages.push(message)
  channel.onmessage = handler
  assert(channel.onmessage === handler, 'Channel setter lost its handler')
  await invoke('stream_numbers', { channel })
  await until(() => messages.length === 3)
  assert(JSON.stringify(messages) === '[0,1,2]', `channel messages out of order: ${messages}`)
  assert(initial.length === 0, 'Channel invoked its replaced handler')
  return messages
})
await test('core.callback-lifecycle', async () => {
  assert(isTauri() === true, 'official isTauri did not identify the desktop runtime');
  const repeated = [], single = [];
  const repeatId = transformCallback(value => repeated.push(value));
  const onceId = transformCallback(value => single.push(value), true);
  const emptyId = transformCallback(undefined, true);
  assert(new Set([repeatId, onceId, emptyId]).size === 3, 'callback identifiers collide');
  for (const id of [repeatId, onceId, emptyId])
    assert(Number.isInteger(id) && id >= 0 && id <= 0xffffffff, 'invalid public callback id');
  for (const id of [repeatId, repeatId, onceId, emptyId])
    await invoke('stream_numbers', { channel: `__CHANNEL__:${id}` });
  await until(() => repeated.length === 8 && single.length === 1);
  const expected = [{message:0,index:0},{message:1,index:1},{message:2,index:2},{end:true,index:3}];
  for (const [index, value] of repeated.entries()) {
    const wanted = expected[index % 4];
    assert(Object.keys(value).length === 2 && value.index === wanted.index &&
      ('end' in wanted ? value.end === true : value.message === wanted.message), 'callback payload or lifetime differs');
  }
  assert(single[0].index === 0 && single[0].message === 0, 'one-shot callback was not removed after its first message');
  return { runtimeDetected:true, uniqueIds:true, persistentMessages:repeated.length, onceMessages:single.length, emptyCallback:true };
})
await test('core.resource-lifecycle', async () => {
  const image = await Image.new(new Uint8Array([9, 8, 7, 255]), 1, 1);
  const resource = new Resource(image.rid);
  assert(image instanceof Resource && resource.rid === image.rid, 'Resource did not retain native handle identity');
  assert(await resource.close() === null, 'Resource.close did not resolve to null');
  for (const operation of [() => image.size(), () => resource.close()]) {
    const error = await rejected(operation);
    assert(error === `The resource id ${resource.rid} is invalid.`, `invalid resource error changed: ${error}`);
  }
  return { identity:true, closeResult:null, useAfterCloseRejected:true, duplicateCloseRejected:true };
})
await test('event.listen-emit-unlisten', async () => {
  const payloads = []
  const unlisten = await listen('netwiv://conformance', event => payloads.push(event.payload))
  await emit('netwiv://conformance', { sequence: 1 })
  await until(() => payloads.length === 1)
  await unlisten()
  await emit('netwiv://conformance', { sequence: 2 })
  await new Promise(resolve => setTimeout(resolve, 100))
  assert(payloads.length === 1 && payloads[0].sequence === 1, 'listener fired after unlisten')
  return payloads
})
await test('invoke.large-payload', async () => {
  const input = JSON.parse('{"__proto__":{"marker":"own data"},"text":""}');
  input.text = "한글🚀'\\\n\u2028".repeat(2048);
  const value = await invoke('echo_json', {input});
  assert(value.text === input.text, 'large invoke changed escaped Unicode data');
  const ownsProto = Object.hasOwn(value, '__proto__');
  assert(ownsProto && value.__proto__.marker === 'own data', 'large invoke lost own data property');
  return { length: value.text.length, ownsProto, marker: value.__proto__.marker };
});
await test('event.large-payload', async () => {
  const payload = JSON.parse('{"__proto__":{"marker":"own data"},"text":""}');
  payload.text = "한글🚀'\\\n\u2028".repeat(2048);
  const received = [];
  const unlisten = await listen('netwiv:large-json', event => received.push(event.payload));
  try {
    await emit('netwiv:large-json', payload);
    await until(() => received.length === 1);
    assert(received[0].text === payload.text, 'large callback changed escaped Unicode data');
    const ownsProto = Object.hasOwn(received[0], '__proto__');
    // The pinned event emitter uses a raw object literal, unlike large IPC replies.
    assert(!ownsProto && received[0].__proto__.marker === 'own data', 'pinned event object-literal behavior changed');
    return { length: received[0].text.length, ownsProto, marker: received[0].__proto__.marker };
  } finally { await unlisten(); }
})
await test('event.target-matrix-and-once', async () => {
  const targets = [{kind:'Any'}, {kind:'App'}, {kind:'AnyLabel',label:'main'},
    {kind:'Window',label:'main'}, {kind:'Webview',label:'main'},
    {kind:'WebviewWindow',label:'main'}, {kind:'AnyLabel',label:'elsewhere'}, {kind:'AnyLabel',label:''}];
  const received = [];
  const removers = [];
  try {
    for (let index=0; index<targets.length; index++)
      removers.push(await listen('netwiv:target-matrix', event => received.push([index,event.payload]), {target:targets[index]}));
    const matrix = [];
    for (let index=0; index<targets.length; index++) {
      await emitTo(targets[index], 'netwiv:target-matrix', index);
      await new Promise(resolve => setTimeout(resolve, 60));
      matrix.push(received.filter(row => row[1]===index).map(row=>row[0]).sort());
    }
    const expected = [[0,1,2,3,4,5,6,7],[0,1],[0,2,3,4,5],[0,2,3],[0,2,4],[0,2,5],[0,6],[0,7]];
    assert(JSON.stringify(matrix)===JSON.stringify(expected), `event target matrix: ${JSON.stringify(matrix)}`);
    const single = [];
    const stop = await once('netwiv:once', event => single.push(event.payload));
    await emit('netwiv:once', 1); await emit('netwiv:once', 2);
    await new Promise(resolve=>setTimeout(resolve,60));
    await stop(); await stop();
    assert(JSON.stringify(single)==='[1]', `once delivered ${JSON.stringify(single)}`);
    return {matrix,single};
  } finally { for (const remove of removers) await remove(); }
})
await test('event.name-validation', async () => {
  const valid = ['', '한글/事件:alpha_1-2', 'Ⅷ²', '\u0345', '\u05B0', '\u{10400}'];
  const delivered = [];
  for (const name of valid) {
    const stop = await once(name, event => delivered.push(event.event));
    await emit(name, null);
    await until(()=>delivered.includes(name));
    await stop();
  }
  const invalid = ['has space', 'dot.name', 'emoji😀', '\u0301', '\u0000', 'line\n'];
  const errors = [];
  for (const name of invalid) {
    for (const [command,operation] of [['emit',()=>emit(name,null)], ['listen',()=>listen(name,()=>{})]]) {
      let rejected = false;
      try { const cleanup=await operation(); if(typeof cleanup==='function')await cleanup(); }
      catch(error) { rejected=true; errors.push({name,command,error:String(error)}); }
      assert(rejected, `${command} accepted invalid event ${JSON.stringify(name)}`);
    }
  }
  return {delivered,errors};
})
await test('path.contract', async () => {
  const home = await homeDir()
  const homeAbsolute = await isAbsolute(home)
  assert(homeAbsolute, `home directory is not absolute: ${home}`)
  const rootOnlyAbsolute = await isAbsolute(sep())
  return { separatorType: typeof sep(), homeAbsolute, rootOnlyAbsolute }
})
await test('path.all-directories-and-resources', async () => {
  const names = ['appCacheDir','appConfigDir','appDataDir','appLocalDataDir','appLogDir',
    'audioDir','cacheDir','configDir','dataDir','desktopDir','documentDir','downloadDir',
    'executableDir','fontDir','homeDir','localDataDir','pictureDir','publicDir',
    'resourceDir','runtimeDir','tempDir','templateDir','videoDir'];
  const required = ['appCacheDir','appConfigDir','appDataDir','appLocalDataDir','appLogDir',
    'cacheDir','configDir','dataDir','homeDir','localDataDir','resourceDir','tempDir'];
  const directories = {};
  for (const name of names) {
    try {
      const value = await pathApi[name]();
      assert(await isAbsolute(value), `${name} returned a relative or empty path: ${JSON.stringify(value)}`);
      directories[name] = {value};
    } catch(error) {
      assert(!required.includes(name) && String(error)==='unknown path', `${name}: ${error}`);
      directories[name] = {error:String(error)};
    }
  }
  assert(directories.appLogDir.value === await join(directories.appLocalDataDir.value,'logs'), 'appLogDir must use local data');
  const root = directories.resourceDir.value;
  const resources = [];
  for (const [input,expected] of [['../asset.txt','_up_/asset.txt'],['/assets/icon.png','_root_/assets/icon.png'],['./foo/../bar','foo/_up_/bar']]) {
    const value = await pathApi.resolveResource(input);
    assert(value === await join(root,expected), `resource path mapping for ${input}: ${value}`);
    resources.push({input,relative:value.slice(root.length)});
  }
  // Executable placement differs between reference and NetWiv; verify each root
  // and its mapping above, and normalize only this explicitly host-owned prefix.
  directories.resourceDir = {value:'<resource-dir>'};
  return {directories,resources,delimiter:pathApi.delimiter()};
})
await test('path.lexical-boundaries', async () => {
  const values = ['', '.', '..', '/', 'a/', 'a/../', 'a//b/./c', '../a', 'a.txt', '.hidden', 'file.', 'a/b.tar.gz', 'C:relative', 'C:/root', '\\\\server\\share\\file'];
  const capture = async operation => { try { return {value:await operation()}; } catch(error) {return {error:String(error)};} };
  const paths = [];
  for (const path of values) paths.push({path,
    normalized:await normalizePath(path), absolute:await isAbsolute(path),
    parent:await capture(()=>dirname(path)), base:await capture(()=>basename(path)), extension:await capture(()=>extname(path))});
  const resolved = [];
  for (const parts of [[], ['.'], ['a','..','b'], ['/a','/b','c'], ['C:/a','/b'], ['C:/a','C:b'], ['C:/a','D:b'], ['C:/a','..','..','b']])
    resolved.push({parts,value:await pathApi.resolve(...parts)});
  return {paths, resolved, joined:await join('a','/b','..','c'), stripped:await basename('a.tar.gz','.gz')};
})
await test('window.current', async () => {
  const current = getCurrentWindow()
  const value = { label: current.label, visible: await current.isVisible(), title: await current.title() }
  assert(value.label === 'main', `unexpected window label: ${value.label}`)
  assert(value.visible === true, `main window is not visible: ${value.visible}`)
  assert(value.title === 'Tauri App', `unexpected default window title: ${value.title}`)
  return value
})
await test('window.configured-startup', async () => {
  const windows = (await getAllWindows()).map(value => value.label).sort();
  const views = (await getAllWebviews()).map(value => value.label).sort();
  assert(String(windows) === 'main' && String(views) === 'main', 'create:false created native objects');
  assert(await Window.getByLabel('deferred-config') === null, 'deferred config appeared as a live window');
  assert(await WebviewWindow.getByLabel('deferred-config') === null, 'deferred config appeared as a live WebView window');
  return {windows, views, title: await getCurrentWindow().title()};
})
await test('webview-window.lifecycle', async () => {
  const child = new WebviewWindow('conformance-child', { url: 'child.html', visible: true })
  await withTimeout(new Promise((resolve, reject) => { child.once('tauri://created', resolve); child.once('tauri://error', event => reject(event.payload)) }))
  const found = (await getAllWebviewWindows()).some(item => item.label === child.label)
  assert(found, 'created webview window was not discoverable')
  const targets = [{kind:'Any'}, {kind:'App'}, {kind:'AnyLabel',label:child.label},
    {kind:'Window',label:child.label}, {kind:'Webview',label:child.label}, {kind:'WebviewWindow',label:child.label}];
  const received = [];
  const removers = [];
  let requests=0;
  try {
    for (let index=0;index<targets.length;index++) removers.push(await listen('tauri://close-requested',()=>received.push(index),{target:targets[index]}));
    const stop = await child.onCloseRequested(event=>{ requests++; event.preventDefault(); });
    removers.push(stop);
    await child.close();
    await until(()=>requests===1 && received.length>=3);
    assert((await getAllWebviewWindows()).some(item=>item.label===child.label), 'preventDefault did not keep window alive');
    assert(JSON.stringify(received.sort())==='[0,3,5]', `native window event targets: ${JSON.stringify(received)}`);
  } finally { for(const remove of removers) await remove(); }
  await child.close();
  await untilAsync(async()=>!(await getAllWebviewWindows()).some(item=>item.label===child.label));
  return { created: found, prevented:requests, nativeTargets:received, closed:true }
})
await test('window.property-roundtrip', async () => {
  const current = getCurrentWindow();
  const title = await current.title();
  const resizable = await current.isResizable();
  try {
    await current.setTitle('Property roundtrip');
    await current.setResizable(false);
    const value = {title:await current.title(),resizable:await current.isResizable()};
    assert(value.title === 'Property roundtrip' && value.resizable === false, 'window property setter had no effect');
    return value;
  } finally { await current.setTitle(title); await current.setResizable(resizable); }
})
await test('webview.bounds', async () => {
  const view = getCurrentWebview();
  const position = await view.position();
  const size = await view.size();
  assert(Number.isFinite(position.x) && Number.isFinite(position.y) && size.width > 0 && size.height > 0, 'invalid native webview bounds');
  return {label:view.label,positionType:position.type,sizeType:size.type,positiveSize:true};
})
await test('webview.creation-user-agent', async () => {
  let ready;
  const unlisten = await listen('creation-ready', event => { ready = event.payload; });
  const results = [];
  try {
    for (const kind of ['root', 'child']) {
      for (const option of ['userAgent', 'user-agent']) {
        ready = undefined;
        const agent = 'NetWiv-Conformance-Agent/1.0';
        let window;
        try {
          if (kind === 'root') {
            window = await created(new WebviewWindow('conformance-child', { url: 'creation-options.html', visible: false, [option]: agent }));
          } else {
            window = await created(new Window('conformance-child', { visible: false }));
            await created(new Webview(window, 'creation-options-child', { url: 'creation-options.html', x: 0, y: 0, width: 320, height: 240, [option]: agent }));
          }
          await until(() => ready !== undefined, 5000);
          assert(ready.userAgent === agent, `${kind} ${option}: unexpected user agent ${ready.userAgent}`);
          results.push({ kind, option, userAgent: ready.userAgent });
        } finally { if (window) await window.destroy(); }
      }
    }
    return results;
  } finally { await unlisten(); }
})
await test('webview.hidden-parent-show', async () => {
  let ready, painted, window;
  const unlistenReady = await listen('creation-ready', event => { ready = event.payload; });
  const unlistenPainted = await listen('creation-painted', event => { painted = event.payload; });
  try {
    window = await created(new WebviewWindow('conformance-child', { url: 'creation-options.html', visible: false }));
    await until(() => ready !== undefined, 5000);
    assert(!await window.isVisible(), 'window was not created hidden');
    await window.show();
    await untilAsync(() => window.isVisible(), 5000);
    await emitTo({ kind: 'Webview', label: window.label }, 'creation-paint');
    await until(() => painted !== undefined, 5000);
    assert(painted.label === window.label && painted.visibility === 'visible', 'shown root WebView did not become visible');
    return { animationFrameAfterShow: true, visibility: painted.visibility };
  } finally { await unlistenReady(); await unlistenPainted(); if (window) await window.destroy(); }
})
await test('webview.root-auto-resize', async () => {
  const window = await created(new WebviewWindow('root-auto-resize', { url: 'child.html', visible: true, decorations: false, width: 400, height: 300 }));
  try {
    const view = await Webview.getByLabel(window.label);
    assert(view, 'root webview was not found');
    let scale = await resizeTo(window, 600, 450);
    await viewBoundsEqual(view, scale, 0, 0, 600, 450);
    scale = await resizeTo(window, 320, 240);
    await viewBoundsEqual(view, scale, 0, 0, 320, 240);
    return { fillsParentOnGrowAndShrink: true };
  } finally { await window.destroy(); }
})
await test('webview.relative-auto-resize', async () => {
  const window = await created(new Window('relative-auto-parent', { visible: true, decorations: false, width: 400, height: 300 }));
  let view;
  const windows = sep() === '\\';
  const check = async (scale, relative, parent) => {
    // Pinned Tauri's GTK Box controls child allocation; wry's bounds setters
    // do not override it. Windows children use the explicit relative bounds.
    await viewBoundsEqual(view, scale, ...(windows ? relative : [0, 0, ...parent]));
  };
  try {
    await resizeTo(window, 400, 300);
    view = await created(new Webview(window, 'relative-auto-child', { url: 'child.html', x: 40, y: 40, width: 160, height: 120 }));
    await view.setAutoResize(true);
    let scale = await resizeTo(window, 600, 450);
    await check(scale, [60, 60, 240, 180], [600, 450]);
    await view.setAutoResize(false);
    scale = await resizeTo(window, 800, 600);
    await check(scale, [60, 60, 240, 180], [800, 600]);
    await view.setSize(new LogicalSize(200, 100));
    await view.setPosition(new LogicalPosition(80, 60));
    await view.setAutoResize(true);
    scale = await resizeTo(window, 400, 300);
    await check(scale, [40, 30, 100, 50], [400, 300]);
    await view.setSize(new LogicalSize(120, 90));
    await view.setPosition(new LogicalPosition(60, 30));
    scale = await resizeTo(window, 600, 450);
    await check(scale, [90, 45, 180, 135], [600, 450]);
    return { layout: windows ? 'relative-bounds' : 'gtk-box', enableDisableAndSettersChecked: true };
  } finally { if (view) await view.close(); await window.destroy(); }
})
await test('window.native-flags', async () => {
  const window = await created(new Window('native-flags', { visible: false, width: 320, height: 240 }));
  const windows = sep() === '\\';
  const observed = {};
  try {
    assert(await window.isVisible() === false, 'hidden creation unexpectedly showed the window');
    await window.show();
    await untilAsync(() => window.isVisible());
    for (const [name, set, get, linuxNoop] of [
      ['closable', value => window.setClosable(value), () => window.isClosable(), false],
      ['decorated', value => window.setDecorations(value), () => window.isDecorated(), false],
      ['enabled', value => window.setEnabled(value), () => window.isEnabled(), false],
      ['minimizable', value => window.setMinimizable(value), () => window.isMinimizable(), true],
      ['maximizable', value => window.setMaximizable(value), () => window.isMaximizable(), true]
    ]) {
      observed[name] = [];
      for (const value of [false, true]) {
        await set(value);
        // Pinned tao's Linux minimize/maximize button setters are no-ops and their getters return true.
        const expected = !windows && linuxNoop ? true : value;
        try { await untilAsync(async () => await get() === expected); }
        catch (error) { throw new Error(`${name}: requested ${value}, expected ${expected}, observed ${await get()}: ${error}`); }
        observed[name].push(await get());
      }
    }
    // GTK treats keep-above as a WM hint. Observe the actual getter rather than
    // assuming its cached state acknowledges the request; the paired host must match it.
    observed.alwaysOnTop = [];
    for (const value of [false, true]) {
      await window.setAlwaysOnTop(value);
      const deadline = performance.now() + 2000;
      let actual = await window.isAlwaysOnTop();
      while (actual !== value && performance.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 10));
        actual = await window.isAlwaysOnTop();
      }
      assert(typeof actual === 'boolean', 'topmost getter did not return a boolean');
      if (windows) assert(actual === value, 'Windows topmost flag did not change');
      observed.alwaysOnTop.push(actual);
    }
    await window.hide();
    await untilAsync(async () => !await window.isVisible());
    return { observed, shownAndHidden: true };
  } finally { await window.destroy(); }
})
await test('window.resize-and-dpi', async () => {
  const window = await created(new Window('native-resize', { visible: true, decorations: false, width: 240, height: 180 }));
  let unlisten;
  const events = [];
  try {
    const scale = await window.scaleFactor();
    assert(Number.isFinite(scale) && scale > 0, 'invalid native scale factor');
    unlisten = await window.onResized(event => events.push(event.payload));
    for (const requested of [new LogicalSize(320, 210), new PhysicalSize(360, 240)]) {
      const converted = requested.type === 'Logical' ? requested.toPhysical(scale) : requested;
      // The guest retains fractional physical coordinates; the native integer pixel conversion rounds them.
      const expected = new PhysicalSize(Math.round(converted.width), Math.round(converted.height));
      const start = events.length;
      await window.setSize(requested);
      try {
        await untilAsync(async () => {
          const actual = await window.innerSize();
          return actual.width === expected.width && actual.height === expected.height &&
            events.slice(start).some(size => size.width === expected.width && size.height === expected.height);
        }, 5000);
      } catch (error) {
        throw new Error(`resize ${JSON.stringify(requested)}, scale ${scale}, expected ${JSON.stringify(expected)}, actual ${JSON.stringify(await window.innerSize())}, events ${JSON.stringify(events.slice(start))}: ${error}`);
      }
      const size = await window.innerSize(), outer = await window.outerSize();
      assert(size.type === 'Physical' && outer.type === 'Physical', 'native sizes lost their DPI types');
      assert(outer.width >= size.width && outer.height >= size.height, 'outer size does not contain inner size');
    }
    return { logicalResize: true, physicalResize: true, resizeEvents: true, physicalSizeTypes: true };
  } finally { if (unlisten) await unlisten(); await window.destroy(); }
})
await test('window.maximize-and-fullscreen', async () => {
  const window = await created(new Window('native-presentation', { visible: true, width: 320, height: 240 }));
  const state = async () => ({ maximized: await window.isMaximized(), fullscreen: await window.isFullscreen(), visible: await window.isVisible(), size: await window.innerSize() });
  const wait = async (predicate, phase) => {
    // A GTK state notification can precede the compositor's geometry update.
    // Keep the requested state and geometry stable before issuing its inverse.
    let stableSince = performance.now(), previous;
    try {
      await untilAsync(async () => {
        const matched = await predicate(), current = JSON.stringify(await state());
        if (!matched || current !== previous) stableSince = performance.now();
        previous = current;
        return matched && performance.now() - stableSince >= 150;
      }, 5000);
    }
    catch (error) { throw new Error(`${phase}: ${JSON.stringify(await state())}: ${error}`); }
  };
  try {
    await wait(() => window.isVisible(), 'initial presentation');
    await window.maximize();
    await wait(() => window.isMaximized(), 'maximize');
    await window.unmaximize();
    await wait(async () => !await window.isMaximized(), 'unmaximize');
    await window.toggleMaximize();
    await wait(() => window.isMaximized(), 'toggle maximize');
    await window.toggleMaximize();
    await wait(async () => !await window.isMaximized(), 'toggle restore');
    await window.setFullscreen(true);
    await wait(() => window.isFullscreen(), 'fullscreen');
    await window.setFullscreen(false);
    await wait(async () => !await window.isFullscreen(), 'restore fullscreen');
    return { maximizedAndRestored: true, toggledTwice: true, fullscreenAndRestored: true };
  } finally { await window.destroy(); }
})
await test('window.color-union', async () => {
  const window = getCurrentWindow(), view = getCurrentWebview();
  const valid = ['#abc','#112233','#11223344',[1,2,3],[1,2,3,4],{red:1,green:2,blue:3},{red:1,green:2,blue:3,alpha:4}];
  const invalid = ['#ab','#gg0000',[1,2],[1,2,3,4,5],{red:256,green:2,blue:3},{red:1,green:2,blue:3,alpha:1.5}];
  const errors = [];
  try {
    // The pinned guest sends `color`, while its runtime setter deserializes
    // `value`. Exercise that public behavior separately from actual decoding.
    for (const color of [...valid,...invalid]) {
      await window.setBackgroundColor(color);
      await view.setBackgroundColor(color);
    }
    for (const value of valid) {
      await invoke('plugin:window|set_background_color',{value});
      await invoke('plugin:webview|set_webview_background_color',{value});
    }
    for (const color of invalid) errors.push({color,
      window:await rejected(()=>invoke('plugin:window|set_background_color',{value:color})),
      webview:await rejected(()=>invoke('plugin:webview|set_webview_background_color',{value:color}))});
    return {publicResolved:valid.length+invalid.length,decoded:valid.length,errors};
  } finally {
    await invoke('plugin:window|set_background_color',{value:null});
    await invoke('plugin:webview|set_webview_background_color',{value:null});
  }
})
await test('window.creation-validation', async () => {
  const errors = [];
  let sequence = 0;
  for (const options of [{width:'bad'},{visible:'bad'},{url:42},{notAnOption:true}]) {
    const label = `invalid-options-${sequence++}`;
    const error = await rejected(()=>created(new WebviewWindow(label,{url:'child.html',visible:false,...options})));
    assert(!(await getAllWindows()).some(window=>window.label===label),'invalid options created a window');
    errors.push({options,error});
  }
  for (const create of [label=>new Window(label,{visible:false}),label=>new WebviewWindow(label,{url:'child.html',visible:false})]) {
    errors.push({label:'bad label!',error:await rejected(()=>created(create('bad label!')))});
    assert(!(await getAllWindows()).some(window=>window.label==='bad label!'),'invalid label entered window registry');
  }
  const unicode = await created(new Window('한글_事件',{visible:false}));
  await unicode.destroy();
  await untilAsync(async()=>!(await getAllWindows()).some(window=>window.label===unicode.label));
  return {errors,unicodeCreated:true};
})
await test('window.numeric-and-stale-handles', async () => {
  const window = await created(new Window('argument-window',{visible:false}));
  const errors = [];
  try {
    for (const value of [1.5,'bad',1e30]) errors.push({badge:value,error:await rejected(()=>window.setBadgeCount(value))});
    for (const value of [{progress:-1},{progress:1.5},{progress:'bad'},{status:'bad'}])
      errors.push({progress:value,error:await rejected(()=>window.setProgressBar(value))});
    for (const value of [new PhysicalSize(-1,20),new PhysicalSize(1.5,20),new PhysicalSize(2**32,20)])
      errors.push({size:normalize(value),error:await rejected(()=>window.setSize(value))});
    for (const value of [new PhysicalPosition(0.5,0),new PhysicalPosition(2**31,0)])
      errors.push({position:normalize(value),error:await rejected(()=>window.setPosition(value))});
  } finally {
    await window.destroy();
    await untilAsync(async()=>!(await getAllWindows()).some(item=>item.label===window.label));
  }
  const stale = [await rejected(()=>window.close()),await rejected(()=>window.destroy())];
  return {errors,stale};
})
await test('webview.child-lifecycle', async () => {
  const parent = await created(new Window('child-container',{visible:true,width:240,height:180}));
  let child;
  try {
    const invalidLabel = await rejected(()=>created(new Webview(parent,'bad label!',{url:'child.html',x:0,y:0,width:80,height:60})));
    child = await created(new Webview(parent,'child-view',{url:'child.html',x:0,y:0,width:80,height:60}));
    await child.reparent('missing-destination');
    const found = (await getAllWebviews()).find(item=>item.label===child.label);
    const native = (await invoke('plugin:webview|get_all_webviews')).find(item=>item.label===child.label);
    assert(native?.window_label===parent.label,'missing reparent destination changed parent');
    assert(found && found.window.label===undefined,'pinned guest parent-field mismatch changed');
    await untilAsync(async()=>{ const size=await child.size(); return size.width>0 && size.height>0; });
    const size = await child.size();
    assert(size.width>0 && size.height>0,'child has no native size');
    await child.close();
    await untilAsync(async()=>!(await getAllWebviews()).some(item=>item.label===child.label));
    const stale = await rejected(()=>child.close());
    return {invalidLabel,parent:native.window_label,guestParentMissing:true,positiveSize:true,stale};
  } finally { await parent.destroy(); }
})
await test('webview.reparent-capability', async () => {
  let ready = 0;
  const replies = new Map();
  const unready = await listen('reparent-ready', () => ready++);
  const unreply = await listen('reparent-response', event => replies.set(event.payload.step, event.payload));
  let target, child;
  try {
    target = await created(new Window('move-target', { visible: false, width: 240, height: 180 }));
    child = await created(new Webview(getCurrentWindow(), 'moving-child', { url: 'reparent.html', x: 0, y: 0, width: 80, height: 60 }));
    await until(() => ready === 1, 5000);
    const probe = async (step, expected) => {
      await emitTo('moving-child', 'reparent-probe', step);
      await until(() => replies.has(step), 5000);
      const response = replies.get(step);
      assert(response.allowed === expected, `wrong window capability after ${step}: ${JSON.stringify(response)}`);
      assert(expected ? response.value === true : response.error.includes('not allowed'), `unexpected command result after ${step}: ${JSON.stringify(response)}`);
    };
    await probe('original', true);
    await child.reparent(target.label);
    await probe('moved', false);
    await emitTo('moving-child', 'reparent-probe', 'reload');
    await until(() => ready === 2, 5000);
    await probe('reloaded', false);
    await child.reparent(getCurrentWindow().label);
    await probe('returned', true);
    return Object.fromEntries([...replies].map(([step, response]) => [step, response.allowed]));
  } finally {
    if (child) await child.close();
    if (target) await target.destroy();
    unready(); unreply();
  }
});
await test('webview.reparent-inflight-channel', async () => {
  let ready = false, parent, target, child;
  const messages = [], replies = new Map();
  const unready = await listen('inflight-ready', () => { ready = true; });
  const unmessage = await listen('inflight-channel', event => messages.push(event.payload));
  const unreply = await listen('inflight-response', event => replies.set(event.payload.step, event.payload));
  try {
    parent = await created(new Window('conformance-child', { visible: false, width: 240, height: 180 }));
    target = await created(new Window('move-target', { visible: false, width: 240, height: 180 }));
    child = await created(new Webview(parent, 'moving-child', { url: 'reparent-lifetime.html', x: 0, y: 0, width: 80, height: 60 }));
    await until(() => ready, 5000);
    await emitTo(child.label, 'inflight-probe', 'start');
    await until(() => messages.length === 1, 5000);
    assert(messages[0] === 0 && !replies.has('start'), 'stream did not pause at its first channel message');
    await child.reparent(target.label);
    await parent.destroy(); parent = null;
    const moved = (await getAllWebviews()).find(view => view.label === child.label);
    assert(moved?.window_label === target.label, 'destroying the old parent removed the moved WebView');
    await emitTo(child.label, 'inflight-probe', 'moved');
    await until(() => replies.has('moved'), 5000);
    const denied = replies.get('moved');
    assert(denied.allowed === false && denied.error.includes('not allowed'), 'new invocation retained the old window capability');
    assert(messages.length === 1 && !replies.has('start'), 'reparent completed the paused invocation');
    await emit('inflight-resume', null);
    await until(() => messages.length === 3 && replies.has('start'), 5000);
    assert(String(messages) === '0,1,2', `moved document lost or reordered channel messages: ${messages}`);
    assert(replies.get('start').allowed === true && replies.get('start').value === null, 'admitted invocation failed after reparent');
    return { messages, admittedReply: true, destinationDenied: true, oldParentDestroyed: true };
  } finally {
    await emit('inflight-resume', null);
    if (child) await child.close();
    if (target) await target.destroy();
    if (parent) await parent.destroy();
    unready(); unmessage(); unreply();
  }
});
await test('image.resource', async () => {
  const image = await Image.new(new Uint8Array([255, 0, 0, 255]), 1, 1)
  const size = await image.size()
  const rgba = [...await image.rgba()]
  assert(JSON.stringify(rgba) === '[255,0,0,255]', 'image bytes changed')
  await image.close()
  const errors = []
  for (const operation of [()=>image.size(), ()=>image.rgba(), ()=>image.close()]) {
    try { await operation(); throw Error('closed image operation unexpectedly succeeded'); }
    catch (error) {
      const message = String(error)
      assert(message === `The resource id ${image.rid} is invalid.`, `unexpected closed resource error: ${message}`)
      errors.push(message.replace(String(image.rid), '<rid>'))
    }
  }
  const metadataOnly = await Image.new(new Uint8Array([1,2,3]), 10, 20)
  const stored = {size:await metadataOnly.size(),rgba:[...await metadataOnly.rgba()]}
  await metadataOnly.close()
  assert(stored.size.width === 10 && stored.size.height === 20 && stored.rgba.length === 3, 'Image.new decoded or rejected stored metadata')
  return {size,rgba,errors,stored}
})
await test('image.encoded-inputs', async () => {
  const directory = await invoke('asset_fixture');
  const results = [];
  for (const name of ['rgba.png', 'gray16.png', 'palette.png', 'multi.ico']) {
    const expected = name === 'gray16.png' ? [0,0,0,255,128,128,128,255,255,255,255,255] : [255,0,0,255,0,255,0,128];
    const response = await fetch(`images/${name}`);
    assert(response.ok, `fixture ${name} was not loaded`);
    const bytes = new Uint8Array(await response.arrayBuffer());
    const read = async image => {
      try {
        const size = await image.size(), rgba = [...await image.rgba()];
        assert(size.width === expected.length / 4 && size.height === 1 && String(rgba) === String(expected), `decoded ${name}: ${JSON.stringify({size, rgba})}`);
        return { size, rgba };
      } finally { await image.close(); }
    };
    const variants = [];
    for (const input of [bytes, bytes.buffer, [...bytes]]) variants.push(await read(await Image.fromBytes(input)));
    variants.push(await read(await Image.fromPath(await join(directory, name))));
    results.push({name, variants});
  }
  return results;
});
await test('image.decode-errors', async () => {
  const bytes = new Uint8Array(await (await fetch('images/rgba.png')).arrayBuffer());
  const errors = [];
  for (const input of [[], [1,2,3], [...bytes.slice(0,16)]]) {
    const error = await rejected(() => Image.fromBytes(input));
    assert(typeof error === 'string' && error.length > 0, 'missing decoder error');
    errors.push(error);
  }
  const directory = await invoke('asset_fixture');
  const missingPath = await join(directory, 'missing-image.png');
  errors.push(await rejected(() => Image.fromPath(missingPath)));
  return errors;
});
await test('menu.resource', async () => {
  const menu = await Menu.new({ items: [{ id: 'conformance-item', text: 'Conformance' }] })
  const count = (await menu.items()).length
  assert(count === 1, `expected one menu item, got ${count}`)
  await menu.close()
  return { count }
})
await test('menu.items-lifecycle', async () => {
  const image = await Image.new(new Uint8Array([255, 0, 0, 255]), 1, 1)
  const item = await MenuItem.new({ id: 'plain', text: 'Plain', enabled: false })
  const check = await CheckMenuItem.new({ id: 'checked', text: 'Checked', checked: true })
  const icon = await IconMenuItem.new({ id: 'icon', text: 'Icon', icon: image })
  const about = await PredefinedMenuItem.new({ text: 'About fixture', item: { About: {
    name: 'NetWiv conformance', version: '1.0.0', authors: ['Alice', 'Bob'], icon: image
  } } })
  const submenu = await Submenu.new({ id: 'sub', text: 'Submenu', items: [item, check] })
  const menu = await Menu.new({ id: 'lifecycle-menu', items: [submenu] })
  assert(await item.isEnabled() === false && await check.isChecked() === true, 'initial item state')
  await item.setText('Renamed'); await item.setEnabled(true); await item.setAccelerator('Ctrl+Shift+J')
  await check.setChecked(false); await check.setAccelerator(null)
  await icon.setIcon(null); await icon.setIcon(image)
  await submenu.append(icon); await submenu.prepend(about)
  const listed = await submenu.items()
  const kinds = listed.map(entry => entry.kind)
  assert(JSON.stringify(kinds) === JSON.stringify(['Predefined', 'MenuItem', 'Check', 'Icon']), 'submenu item kinds/order')
  const lookup = await menu.get('sub')
  assert(lookup?.kind === 'Submenu' && listed.find(entry => entry.id === 'icon')?.kind === 'Icon', 'lookup materialization')
  const removed = await submenu.removeAt(3)
  assert(removed?.id === 'icon', 'removeAt result')
  await submenu.insert(icon, 1)
  const checkedAttached = await check.isChecked()
  assert(checkedAttached === false, 'attached check item state did not change')
  await submenu.remove(check)
  const finalItems = await submenu.items()
  const finalKinds = finalItems.map(entry => entry.kind)
  const result = { kinds, finalKinds, text: await item.text(), enabled: await item.isEnabled(), checkedAttached, checkedDetached: await check.isChecked(), about: await about.text() }
  assert(result.text === 'Renamed' && result.enabled, `mutated item state: ${JSON.stringify(result)}`)
  for (const resource of [...listed, ...finalItems, lookup, removed, menu, submenu, about, icon, check, item, image]) if (resource) await resource.close()
  return result
})
await test('menu.payload-variants', async () => {
  const menu = await Menu.new({ items: [
    { id: 'check-wins', text: 'Check wins', checked: true, icon: 'Folder', items: [] },
    { id: 'normal-fallback', text: 'Plain fallback', checked: 'invalid' },
    { id: 'null-icon-fallback', text: 'Null icon fallback', icon: null },
    { id: 'invalid-about-fallback', text: 'Invalid About fallback', item: { About: false } },
    { id: 'sub-wins', text: 'Submenu', items: [] }
  ] })
  const items = await menu.items()
  const kinds = items.map(item => item.kind)
  assert(JSON.stringify(kinds) === JSON.stringify(['Check', 'MenuItem', 'MenuItem', 'MenuItem', 'Submenu']), 'untagged menu payload precedence')
  for (const item of items) await item.close()
  await menu.close()
  return kinds
})
await test('menu.argument-errors', async () => {
  const errors = []
  for (const metadata of [false, [], { name: 42 }, { authors: ['Alice', 42] }])
    errors.push(await rejected(() => PredefinedMenuItem.new({ item: { About: metadata } })))
  errors.push(await rejected(() => Menu.new({ items: [{ text: 42 }] })))
  errors.push(await rejected(() => MenuItem.new({ text: 'invalid', enabled: 'yes' })))
  return errors
})
await test('menu.item-state-roundtrip', async () => {
  const resources = [];
  try {
    for (const [kind, make] of [
      ['MenuItem', () => MenuItem.new({ id: 'state-plain', text: 'Before', enabled: false })],
      ['Check', () => CheckMenuItem.new({ id: 'state-check', text: 'Before', enabled: false, checked: false })],
      ['Icon', () => IconMenuItem.new({ id: 'state-icon', text: 'Before', enabled: false })],
      ['Submenu', () => Submenu.new({ id: 'state-submenu', text: 'Before', enabled: false, items: [] })]
    ]) {
      const item = await make(); resources.push(item);
      assert(item.kind === kind && await item.text() === 'Before' && await item.isEnabled() === false, `${kind} initial state`);
      await item.setText('After 한글'); await item.setEnabled(true);
      assert(await item.text() === 'After 한글' && await item.isEnabled() === true, `${kind} updated state`);
      if (kind !== 'Submenu') { await item.setAccelerator('Ctrl+Shift+K'); await item.setAccelerator(null); }
      if (kind === 'Check') { await item.setChecked(true); assert(await item.isChecked(), 'check state did not change'); }
    }
    const predefined = await PredefinedMenuItem.new({ item: 'Copy', text: 'Copy before' }); resources.push(predefined);
    await predefined.setText('Copy after');
    assert(await predefined.text() === 'Copy after', 'predefined text did not change');
    return { kinds: resources.map(item => item.kind), stateRoundtrip: true };
  } finally { for (const resource of resources) await resource.close(); }
});
await test('menu.container-and-window-assignment', async () => {
  const resources = [];
  let window;
  const keep = item => { if (item) resources.push(item); return item; };
  try {
    const menu = keep(await Menu.new({ id: 'container-menu', items: [] }));
    const first = keep(await MenuItem.new({ id: 'container-first', text: 'First' }));
    const second = keep(await CheckMenuItem.new({ id: 'container-second', text: 'Second' }));
    const third = keep(await IconMenuItem.new({ id: 'container-third', text: 'Third' }));
    await menu.append(second); await menu.prepend(first); await menu.insert(third, 1);
    const listed = await menu.items(); listed.forEach(keep);
    assert(String(listed.map(item => item.id)) === 'container-first,container-third,container-second', 'container order');
    const removed = keep(await menu.removeAt(1));
    assert(removed?.id === third.id, 'removed item identity');
    await menu.remove(first);
    const remaining = await menu.items(); remaining.forEach(keep);
    assert(remaining.length === 1 && remaining[0].id === second.id, 'container removal');
    assert(await menu.get('missing-item') === null, 'missing menu lookup');
    window = await created(new Window('conformance-child', { visible: false }));
    const previous = keep(await menu.setAsWindowMenu(window));
    assert(previous === null, 'bare window unexpectedly had a menu');
    const replacement = keep(await Menu.new({ id: 'replacement-menu', items: [] }));
    const returned = keep(await replacement.setAsWindowMenu(window));
    assert(returned?.id === menu.id, 'window menu replacement lost previous menu');
    return { order: listed.map(item => item.kind), remaining: remaining.map(item => item.kind), previousMenuReturned: true };
  } finally {
    if (window) await window.destroy();
    for (const resource of resources.reverse()) await resource.close();
  }
});
await test('menu.app-assignment', async () => {
  const resources = [];
  const keep = value => { if (value) resources.push(value); return value; };
  let window;
  try {
    window = await created(new Window('conformance-child', { visible: false }));
    const first = keep(await Menu.new({ id: 'app-first', items: [] }));
    const second = keep(await Menu.new({ id: 'app-second', items: [] }));
    const custom = keep(await Menu.new({ id: 'app-custom-window', items: [] }));
    assert(keep(await first.setAsAppMenu()) === null, 'unexpected initial app menu');
    assert(keep(await second.setAsAppMenu())?.id === first.id, 'app replacement did not return first menu');
    assert(keep(await custom.setAsWindowMenu(window))?.id === second.id, 'window did not inherit replaced app menu');
    assert(keep(await first.setAsAppMenu())?.id === second.id, 'app replacement lost previous menu');
    assert(keep(await custom.setAsWindowMenu(window))?.id === custom.id, 'app menu overwrote an explicit window menu');
    await window.destroy(); window = null;
    window = await created(new Window('conformance-child', { visible: false }));
    assert(keep(await custom.setAsWindowMenu(window))?.id === first.id, 'new window did not inherit current app menu');
    return { previousAppMenu: true, existingWindowInheritance: true, newWindowInheritance: true, explicitWindowPreserved: true };
  } finally {
    if (window) await window.destroy();
    const empty = await Menu.new({ id: 'app-cleanup', items: [] });
    keep(await empty.setAsAppMenu());
    resources.push(empty);
    for (const resource of resources.reverse()) await resource.close();
  }
});
await test('tray.resource', async () => {
  const icon = await Image.new(new Uint8Array([255, 0, 0, 255]), 1, 1)
  const tray = await TrayIcon.new({ id: 'netwiv-conformance', icon, tooltip: 'NetWiv conformance' })
  await tray.setVisible(false)
  assert((await TrayIcon.getById('netwiv-conformance'))?.id === tray.id, 'tray lookup failed')
  await TrayIcon.removeById('netwiv-conformance')
  await icon.close()
  return { created: true }
})
await test('acl.manifest-command-allow', async () => {
  const value = await invoke('custom_default_allowed')
  assert(value === 'allowed', 'default application command was not allowed')
  return value
})
await test('acl.manifest-command-denied', async () => {
  try { await invoke('restricted'); throw new Error('manifest-controlled command unexpectedly resolved') }
  catch (error) {
    assert(!String(error).includes('unexpectedly resolved'), String(error))
    const profile = await invoke('conformance_profile')
    assert(profile === 'debug' || profile === 'release', 'missing host build profile')
    const expected = profile === 'release' ? 'Command restricted not allowed by ACL'
      : 'restricted not allowed. Permissions associated with this command: allow-restricted'
    assert(error === expected, `unexpected ACL rejection: ${JSON.stringify(error)}`)
    return { profile, error }
  }
})
await record({ type: 'complete', count: results.filter(value => value.type === 'case').length })
await invoke('conformance_finish')
