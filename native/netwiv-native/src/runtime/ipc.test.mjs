import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { createContext, runInContext } from 'node:vm';

const script = readFileSync(new URL('./ipc.js', import.meta.url), 'utf8');
function document(seed = 1, protocolIpc = true, convertRequest = false) {
  const posts = [];
  const requests = [];
  const listeners = {};
  class DocumentHeaders extends Headers {}
  class DocumentURL extends URL {}
  for (const name of ['protocol', 'hostname', 'port', 'username', 'password', 'href']) {
    Object.defineProperty(DocumentURL.prototype, name, Object.getOwnPropertyDescriptor(URL.prototype, name));
  }
  const window = {
    Headers: DocumentHeaders, URL: DocumentURL, addEventListener: (name, handler) => { listeners[name] = handler; },
    webkit: { messageHandlers: { ipc: { postMessage: value => posts.push(JSON.parse(value)) } } },
    fetch: (url, options) => {
      const request = convertRequest ? new Request(url, options) : undefined;
      requests.push({ url, options, request });
      return Promise.resolve('response');
    }
  };
  window.top = window;
  const context = createContext({ window, crypto: { getRandomValues: value => value.fill(seed++) } });
  runInContext(`${script}(${protocolIpc});`, context);
  return { window, posts, requests, listeners, context, nonce: window.__NETWIV_IPC_NONCE__() };
}

test('native grants are bound to one document nonce without draining another startup queue', () => {
  const first = document(1);
  const next = document(2);
  next.window.__NETWIV_DOCUMENT_TRANSPORT__.send('queued attach');
  next.window.__NETWIV_ACTIVATE_IPC__(first.nonce, 1, 'retired capability');
  assert.equal(next.posts.length, 0);
  next.window.__NETWIV_ACTIVATE_IPC__(next.nonce, 2, 'current capability');
  assert.deepEqual(next.posts, [{ capability: 'current capability', documentId: 2, payload: 'queued attach' }]);
  next.window.__NETWIV_ACTIVATE_IPC__(next.nonce, 1, 'stale capability');
  next.window.__NETWIV_DOCUMENT_TRANSPORT__.send('current');
  assert.equal(next.posts[1].capability, 'current capability');
});

test('captured native post cannot be replaced to extract the capability', () => {
  const page = document();
  page.window.webkit.messageHandlers.ipc.postMessage = () => assert.fail('replacement native post called');
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 3, 'secret');
  page.window.__NETWIV_DOCUMENT_TRANSPORT__.send('message');
  assert.equal(page.posts.length, 1);
  assert.equal(JSON.stringify(page.window.__NETWIV_DOCUMENT_TRANSPORT__), '{}');
});

test('inherited JSON serialization cannot inspect native message credentials', () => {
  for (const queued of [false, true]) {
    const page = document();
    runInContext(`Object.prototype.toJSON = function () {
      if (this.capability) window.leakedCapability = this.capability;
      return this;
    };`, page.context);
    if (!queued) page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
    page.window.__NETWIV_DOCUMENT_TRANSPORT__.send('message');
    if (queued) page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
    assert.equal(page.window.leakedCapability, undefined);
    assert.deepEqual(page.posts, [{ capability: 'secret', documentId: 1, payload: 'message' }]);
  }
});

test('native Request conversion cannot expose credentials to inherited header or init hooks', async () => {
  const page = document(1, true, true);
  const observed = [];
  const originalIterator = page.window.Headers.prototype[Symbol.iterator];
  page.window.Headers.prototype[Symbol.iterator] = function* () {
    observed.push(this.get('NetWiv-Document-Capability'));
    yield* originalIterator.call(this);
  };
  page.window.observed = observed;
  page.window.inheritedBodyReads = 0;
  runInContext(`Object.defineProperty(Object.prototype, 'body', {
    get() {
      window.inheritedBodyReads++;
      const headers = this.headers;
      if (headers) window.observed.push(headers['netwiv-document-capability'] ?? headers['NetWiv-Document-Capability'] ?? headers.get?.('NetWiv-Document-Capability'));
      return undefined;
    }
  });`, page.context);
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
  await page.window.__NETWIV_DOCUMENT_TRANSPORT__.fetchIpc('ipc://localhost/echo', {
    headers: new page.window.Headers({
      'Content-Type': 'application/json',
      'NetWiv-Document-Capability': 'caller value',
      'NetWiv-Document-Id': '999'
    })
  });
  assert.equal(observed.includes('secret'), false);
  assert.equal(page.window.inheritedBodyReads, 0);
  assert.equal(page.requests.length, 1);
  assert.equal(page.requests[0].request.headers.get('NetWiv-Document-Capability'), 'secret');
  assert.equal(page.requests[0].request.headers.get('NetWiv-Document-Id'), '1');
  assert.equal(page.requests[0].request.headers.get('Content-Type'), 'application/json');
  assert.equal(page.requests[0].request.redirect, 'error');
});

test('a restored document queues calls and rejects grants from its retired activation', () => {
  const page = document();
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'retired');
  page.listeners.pagehide();
  page.window.__NETWIV_DOCUMENT_TRANSPORT__.send('restored');
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'retired');
  assert.equal(page.posts.length, 0);
  const currentNonce = page.window.__NETWIV_IPC_NONCE__();
  assert.notEqual(currentNonce, page.nonce);
  page.window.__NETWIV_ACTIVATE_IPC__(currentNonce, 3, 'current');
  assert.deepEqual(page.posts, [{ capability: 'current', documentId: 3, payload: 'restored' }]);
});

test('protocol capability stays on the private IPC authority and cannot follow redirects', async () => {
  const page = document();
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 4, 'secret');
  const fetchIpc = page.window.__NETWIV_DOCUMENT_TRANSPORT__.fetchIpc;
  for (const target of ['https://remote.invalid/', 'other://localhost/', 'ipc://foreign/', 'ipc://user@localhost/', 'ipc://localhost:8/']) {
    await assert.rejects(fetchIpc(target, {}));
  }
  assert.equal(page.requests.length, 0);
  let coercions = 0;
  const target = { toString() { coercions++; return coercions === 1 ? 'ipc://localhost/echo' : 'https://remote.invalid/'; } };
  const headers = new Headers({ 'Content-Type': 'application/json' });
  await fetchIpc(target, { headers, redirect: 'follow' });
  assert.equal(coercions, 1);
  assert.equal(page.requests[0].url, 'ipc://localhost/echo');
  assert.equal(page.requests[0].options.redirect, 'error');
  assert.equal(new Headers(page.requests[0].options.headers).get('NetWiv-Document-Capability'), 'secret');
  assert.equal(headers.get('NetWiv-Document-Capability'), null);
});

test('a claimed IPC scheme cannot expose capability headers through the private fetch helper', async () => {
  const page = document(1, false);
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
  await assert.rejects(page.window.__NETWIV_DOCUMENT_TRANSPORT__.fetchIpc('ipc://localhost/resource', {}));
  assert.equal(page.requests.length, 0);
  page.window.__NETWIV_DOCUMENT_TRANSPORT__.send('command');
  assert.equal(page.posts.length, 1);
});

test('URL prototype getters cannot disguise a remote target or redirect private credentials', async () => {
  const page = document();
  page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
  let poisonedReads = 0;
  for (const [name, value] of Object.entries({
    protocol: 'ipc:', hostname: 'localhost', port: '', username: '', password: '',
    href: 'https://remote.invalid/stolen'
  })) {
    Object.defineProperty(page.window.URL.prototype, name, { get() { poisonedReads++; return value; } });
  }
  const fetchIpc = page.window.__NETWIV_DOCUMENT_TRANSPORT__.fetchIpc;
  await assert.rejects(fetchIpc('https://remote.invalid/disguised', {}));
  assert.equal(page.requests.length, 0);
  await fetchIpc('ipc://localhost/echo', {});
  assert.equal(page.requests[0].url, 'ipc://localhost/echo');
  assert.equal(new Headers(page.requests[0].options.headers).get('NetWiv-Document-Capability'), 'secret');
  assert.equal(poisonedReads, 0);
});

test('queued delivery and option getters cannot change the validated fetch URL', async () => {
  for (const queued of [false, true]) {
    const page = document();
    if (!queued) page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
    let headerReads = 0;
    let bodyReads = 0;
    const options = {
      get headers() {
        headerReads++;
        Object.defineProperty(page.window.URL.prototype, 'href', { get: () => 'https://remote.invalid/header-getter' });
        return new Headers();
      },
      get body() {
        bodyReads++;
        Object.defineProperty(page.window.URL.prototype, 'href', { get: () => 'https://remote.invalid/options-spread' });
        return 'payload';
      }
    };
    const pending = page.window.__NETWIV_DOCUMENT_TRANSPORT__.fetchIpc('ipc://localhost/echo', options);
    if (queued) {
      Object.defineProperty(page.window.URL.prototype, 'href', { get: () => 'https://remote.invalid/queued' });
      page.window.__NETWIV_ACTIVATE_IPC__(page.nonce, 1, 'secret');
    }
    await pending;
    assert.equal(page.requests.length, 1);
    assert.equal(page.requests[0].url, 'ipc://localhost/echo');
    assert.equal(new Headers(page.requests[0].options.headers).get('NetWiv-Document-Capability'), 'secret');
    assert.equal(page.requests[0].options.redirect, 'error');
    assert.ok(headerReads > 0 && bodyReads > 0);
  }
});
