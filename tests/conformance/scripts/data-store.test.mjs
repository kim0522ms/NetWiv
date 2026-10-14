import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import test from 'node:test';
import { websiteDataStoreContract } from '../frontend/data-store.js';

globalThis.crypto ??= webcrypto;

function fixture(fault) {
  const ambient = JSON.stringify(Array(16).fill(42));
  const stores = new Map([[ambient, 'ambient']]);
  const removed = [];
  let child, listener, releasePending = false;
  class WebviewWindow {
    constructor(label, options) {
      this.label = label;
      this.identifier = JSON.stringify(options.dataStoreIdentifier);
      this.incognito = options.incognito;
      child = this;
      const query = new URL(options.url, 'https://fixture.invalid/').searchParams;
      const expected = query.get('expected') || null;
      if (!stores.has(this.identifier) && !this.incognito) stores.set(this.identifier, null);
      let actual = this.incognito ? null : stores.get(this.identifier);
      if (['leak', 'cleanup-failure'].includes(fault) && query.get('write') === 'second') actual = 'first';
      if (fault === 'lost-persistence' && expected) actual = null;
      if (fault === 'incognito' && this.incognito) actual = stores.get(this.identifier);
      const ok = actual === expected;
      if (ok && query.get('write') && !this.incognito) stores.set(this.identifier, query.get('write'));
      queueMicrotask(() => listener?.({ payload: { ok, error: ok ? undefined : 'Website data-store persistence or isolation changed' } }));
    }
    async close() { child = null; releasePending = fault === 'delayed-release'; }
    static async getByLabel() { return child; }
  }
  return {
    stores, removed, ambient,
    api: {
      WebviewWindow,
      createWindow: async options => new WebviewWindow('conformance-child', options),
      listen: async (_, callback) => { listener = callback; return () => { listener = null; }; },
      untilAsync: async predicate => {
        for (let attempt = 0; attempt < 3; attempt++) if (await predicate()) return;
        throw Error('Fixture did not finish');
      },
      fetchDataStoreIdentifiers: async () => [...stores.keys()].map(JSON.parse),
      removeDataStore: async identifier => {
        const key = JSON.stringify(identifier);
        if (child?.identifier === key && !child.incognito && fault !== 'active-removal') throw 'runtime error: failed to remove data store';
        if (releasePending) { releasePending = false; throw 'runtime error: failed to remove data store'; }
        removed.push(key);
        if (fault === 'cleanup-failure' && removed.length === 1) throw 'unexpected removal failure';
        if (fault !== 'stale-recreation') stores.delete(key);
      }
    }
  };
}

test('strict non-Apple empty enumeration and no-op removal', async () => {
  for (const os of ['windows', 'linux']) {
    let calls = 0;
    const api = { fetchDataStoreIdentifiers: async () => [], removeDataStore: async identifier => {
      assert.equal(identifier.length, 16); calls++;
    } };
    assert.deepEqual(await websiteDataStoreContract({ os, websiteDataStores: false }, api), { enumeration: 'empty', removal: 'no-op' });
    assert.equal(calls, 2);
    await assert.rejects(websiteDataStoreContract({ os }, { ...api, fetchDataStoreIdentifiers: async () => [Array(16).fill(1)] }), /must be empty/);
  }
});

test('older macOS is explicitly skipped before unsafe public upstream operations', async () => {
  const unexpected = () => { throw Error('Unavailable selector reached'); };
  await assert.rejects(websiteDataStoreContract({ os: 'macos', websiteDataStores: false }, {
    fetchDataStoreIdentifiers: unexpected, removeDataStore: unexpected
  }), error => error.platformSkip?.platforms.includes('macos'));
});

test('isolated lifecycle checks pass and never delete ambient stores', async () => {
  const probe = fixture('delayed-release');
  const value = await websiteDataStoreContract({ os: 'macos', websiteDataStores: true }, probe.api);
  assert.equal(value.incognitoPrecedence, true);
  assert.equal(value.recreation, 'empty');
  assert.equal(probe.stores.get(probe.ambient), 'ambient');
  assert.ok(probe.removed.every(identifier => identifier !== probe.ambient));
  assert.equal(probe.stores.size, 1);
});

test('cleanup attempts both test-owned stores and preserves an unexpected deletion failure', async () => {
  const probe = fixture('cleanup-failure');
  await assert.rejects(websiteDataStoreContract({ os: 'macos', websiteDataStores: true }, probe.api),
    error => error === 'unexpected removal failure');
  assert.equal(new Set(probe.removed).size, 2);
  assert.equal(probe.stores.get(probe.ambient), 'ambient');
});

for (const [fault, expectedError] of [
  ['active-removal', /Active-store removal did not reject/],
  ['leak', /Website data-store persistence or isolation changed/],
  ['lost-persistence', /Website data-store persistence or isolation changed/],
  ['incognito', /Website data-store persistence or isolation changed/],
  ['stale-recreation', /Removed store remained enumerated/]
]) {
  test(`lifecycle fixture rejects ${fault}`, async () => {
    const probe = fixture(fault);
    await assert.rejects(websiteDataStoreContract({ os: 'macos', websiteDataStores: true }, probe.api), expectedError);
    assert.equal(probe.stores.get(probe.ambient), 'ambient');
    assert.ok(probe.removed.every(identifier => identifier !== probe.ambient));
  });
}
