import { invoke, Channel, SERIALIZE_TO_IPC_FN } from './core.js';

const results = JSON.parse(sessionStorage.getItem('isolation-results') || '[]');
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const require = (condition, message) => { if (!condition) throw new Error(message); };
const test = async (name, run, scope = 'public') => {
  try { await run(); results.push({ name, scope, passed: true }); }
  catch (error) { results.push({ name, scope, passed: false, error: String(error) }); }
  document.querySelector('#results').textContent = JSON.stringify(results, null, 2);
};
window.addEventListener('securitypolicyviolation', event => {
  console.error('CSP', event.violatedDirective, event.blockedURI);
});

async function opaqueFrame(probe) {
  const frame = document.createElement('iframe');
  frame.sandbox.add('allow-scripts');
  const secured = document.querySelector('#__tauri_isolation__');
  let leaked = false;
  let probeReady;
  const started = new Promise(resolve => { probeReady = resolve; });
  const receive = event => {
    if (event.source === frame.contentWindow && event.data === 'probe-started') probeReady();
    if (event.data?.cmd === 'echo' && event.data?.callback === 90909090) leaked = true;
  };
  window.addEventListener('message', receive);
  frame.src = 'data:text/html,' + encodeURIComponent('<script>' + probe + '<\/script>');
  document.body.append(frame);
  try {
    await Promise.race([started, delay(2000).then(() => { throw new Error('Origin probe did not execute'); })]);
    await delay(150);
    require(!leaked && secured.contentWindow !== frame.contentWindow, 'Foreign frame entered isolation');
  } finally {
    frame.remove();
    window.removeEventListener('message', receive);
  }
}

async function reloadWithPending(phase) {
  const channel = new Channel(() => {});
  const before = new Set(window.__TAURI_INTERNALS__.callbacks.keys());
  invoke('pending', { channel });
  const added = [...window.__TAURI_INTERNALS__.callbacks.keys()].filter(id => !before.has(id));
  await invoke('wait_pending_started');
  sessionStorage.setItem('old-callback', String(added[0]));
  sessionStorage.setItem('old-channel', String(channel.id));
  sessionStorage.setItem('isolation-results', JSON.stringify(results));
  sessionStorage.setItem('isolation-reloaded', phase);
  location.reload();
}

try {
  const phase = sessionStorage.getItem('isolation-reloaded');
  if (phase === '1' || phase === '2') {
    // Reuse the callback id from the previous document. A late reply must not hit it.
    const callbackId = Number(sessionStorage.getItem('old-callback'));
    let late = false;
    window.__TAURI_INTERNALS__.callbacks.set(callbackId, () => { late = true; });
    window.__TAURI_INTERNALS__.callbacks.set(Number(sessionStorage.getItem('old-channel')), () => { late = true; });
    await test(phase === '1' ? 'reload discards old command and Channel callbacks' : 'fallback reload discards old command and Channel callbacks', async () => {
      await invoke('wait_pending');
      await delay(100);
      require(!late, 'Old document callback was delivered after reload');
    }, 'netwiv-security');
    await test(phase === '1' ? 'new isolation frame works after reload' : 'new isolation frame works after fallback reload', async () => {
      require((await invoke('echo', { value: 'reloaded' })).filtered, 'Reload lost secure IPC');
    });
    if (phase === '2') {
      sessionStorage.removeItem('isolation-reloaded');
      sessionStorage.removeItem('isolation-results');
      await invoke('complete', { results });
    } else {
      const probe = await fetch('iframe-probe.js').then(response => response.text());
      const policy = document.createElement('meta');
      policy.httpEquiv = 'Content-Security-Policy';
      policy.content = "connect-src 'none'";
      document.head.append(policy);
      // Select the message transport before comparing subsequent requests.
      // NetWiv rejects this first failed protocol call to avoid replaying side
      // effects; the pinned upstream retries it. That difference is not a parity pass.
      try { await invoke('echo', { value: 'select-message-transport' }); }
      catch { /* NetWiv's transport rejection is covered by its replay regression. */ }
      const bytes = new Uint8Array([99, 1, 41, 65, 12, 78, 99]);
      for (const [kind, value] of [['ArrayBuffer', bytes.slice(1, 6).buffer], ['DataView', new DataView(bytes.buffer, 1, 5)], ['number array', [1, 41, 65, 12, 78]]]) {
        await test(`CSP fallback receives ${kind} as JSON bytes`, async () => {
          const response = await invoke('body_info', value);
          require(!response.binary && String(response.value) === '1,41,65,12,78', 'Fallback body kind or bytes changed');
        });
      }
      await test('CSP fallback preserves custom request headers and JSON', async () => {
        const response = await invoke('request_info', { value: 42 }, { headers: { 'X-Test': 'fallback' } });
        require(response.header === 'fallback' && response.value === 42 && !response.binary, 'Fallback request metadata changed');
      });
      await test('CSP fallback Channel retains ordering', async () => {
        const values = [];
        await invoke('channel', { channel: new Channel(value => values.push(value)) });
        await delay(100);
        require(String(values) === '0,1,2,3,4', 'Fallback Channel order changed');
      });
      await test('CSP fallback retains isolation hook blocking', async () => {
        let completed = false;
        invoke('blocked').then(() => { completed = true; });
        await delay(150);
        require(!completed && await invoke('blocked_count') === 0, 'Fallback bypassed hook');
      });
      await test('CSP fallback denies opaque-origin iframe', () => opaqueFrame(probe));
      await reloadWithPending('2');
    }
  } else {
    sessionStorage.removeItem('isolation-results');
    await test('invoke queues before secure iframe ready', async () => {
      const result = await invoke('echo', { value: 'queued' });
      require(result.filtered && result.original === 'queued', 'Async hook was bypassed');
    });
    await test('nested IPC values and Maps survive isolation', async () => {
      const data = { array: [{ [SERIALIZE_TO_IPC_FN]: () => 'serialized' }], map: new Map([['key', 7]]) };
      const result = await invoke('echo', { value: data });
      require(result.original.array[0] === 'serialized' && result.original.map.key === 7, 'IPC serialization changed');
    });
    const bytes = new Uint8Array([99, 1, 41, 65, 12, 78, 99]);
    for (const [kind, value] of [['ArrayBuffer', bytes.slice(1, 6).buffer], ['DataView', new DataView(bytes.buffer, 1, 5)], ['number array', [1, 41, 65, 12, 78]]]) {
      await test(`binary ${kind} round-trip`, async () => {
        const response = await invoke('raw', value);
        require(response instanceof ArrayBuffer && String(new Uint8Array(response)) === '1,41,65,12,78', 'Binary body or result changed');
      });
    }
    await test('Request injection preserves headers and JSON body', async () => {
      const response = await invoke('request_info', { value: 42 }, { headers: { 'X-Test': 'custom value' } });
      require(response.header === 'custom value' && response.value === 42 && !response.binary, 'Request metadata was lost');
    });
    await test('Headers instance rejects at isolation structured clone boundary', async () => {
      try {
        await invoke('request_info', { value: 42 }, { headers: new Headers([['X-Test', 'custom value']]) });
        throw new Error('Expected Headers clone rejection');
      } catch (error) { require(error.name === 'DataCloneError', 'Unexpected clone error: ' + error); }
    });
    await test('structured errors retain JSON', async () => {
      try { await invoke('fail'); throw new Error('Expected rejection'); }
      catch (error) { require(error.code === 'expected' && error.detail === 42, 'Error payload changed'); }
    });
    await test('Channel retains ordering through secure IPC', async () => {
      const values = [];
      const channel = new Channel(value => values.push(value));
      await invoke('channel', { channel });
      await delay(100);
      require(String(values) === '0,1,2,3,4', 'Channel order or serialization changed');
    });
    await test('isolation hook blocks application command', async () => {
      let completed = false;
      invoke('blocked').then(() => { completed = true; });
      await delay(150);
      require(!completed && await invoke('blocked_count') === 0, 'Blocked command reached backend');
    });
    await test('main document cannot read secure frame', async () => {
      const frame = document.querySelector('#__tauri_isolation__');
      require(frame.sandbox.contains('allow-scripts') && !frame.sandbox.contains('allow-same-origin'), 'Iframe sandbox changed');
      let denied = false;
      try { void frame.contentWindow.document; } catch { denied = true; }
      require(denied, 'Main document can read isolation key');
      try { await fetch(frame.src); denied = false; } catch { /* cross-origin response stays unreadable */ }
      require(denied, 'Isolation response is readable cross-origin');
    });
    await test('forged frame messages cannot complete a callback', async () => {
      let called = false;
      const id = window.__TAURI_INTERNALS__.transformCallback(() => { called = true; }, true);
      window.postMessage('__TAURI_ISOLATION_READY__', '*');
      window.postMessage({ cmd: 'blocked', callback: id, error: id, payload: { nonce: [], payload: [], contentType: 'application/json' } }, '*');
      await delay(100);
      window.__TAURI_INTERNALS__.unregisterCallback(id);
      require(!called && await invoke('blocked_count') === 0, 'Untrusted message entered native IPC');
    }, 'netwiv-security');
    await test('opaque-origin iframe cannot invoke secure hook', async () =>
      opaqueFrame(await fetch('iframe-probe.js').then(response => response.text())));
    // The command acknowledges it started before navigation so the test cannot pass vacuously.
    await reloadWithPending('1');
  }
} catch (error) {
  results.push({ name: 'suite', passed: false, error: String(error) });
  await invoke('complete', { results });
}
