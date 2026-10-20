import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('./ipc.js', import.meta.url), 'utf8');
function document(seed = 1) {
  const posts = [];
  const requests = [];
  const listeners = {};
  const window = {
    Headers, URL, addEventListener: (name, handler) => { listeners[name] = handler; },
    webkit: { messageHandlers: { ipc: { postMessage: value => posts.push(JSON.parse(value)) } } },
    fetch: (url, options) => { requests.push({ url, options }); return Promise.resolve('response'); }
  };
  window.top = window;
  runInNewContext(script, { window, crypto: { getRandomValues: value => value.fill(seed++) } });
  return { window, posts, requests, listeners, nonce: window.__NETWIV_IPC_NONCE__() };
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
  assert.equal(page.requests[0].options.headers.get('NetWiv-Document-Capability'), 'secret');
  assert.equal(headers.get('NetWiv-Document-Capability'), null);
});
