// Both hosts supply the same unchanged public guest API functions.
export async function websiteDataStoreContract(feature, api) {
  const { fetchDataStoreIdentifiers, removeDataStore, WebviewWindow, listen, createWindow, untilAsync } = api;
  const identifiers = [0, 1].map(() => [...crypto.getRandomValues(new Uint8Array(16))]);
  const contains = (stores, identifier) => stores.some(store => JSON.stringify(store) === JSON.stringify(identifier));
  const assert = (condition, message) => { if (!condition) throw Error(message); };
  if (feature.os !== 'macos') {
    assert((await fetchDataStoreIdentifiers()).length === 0, 'Non-Apple enumeration must be empty');
    for (const identifier of identifiers) await removeDataStore(identifier);
    assert((await fetchDataStoreIdentifiers()).length === 0, 'Non-Apple removal must remain a no-op');
    return { enumeration: 'empty', removal: 'no-op' };
  }
  if (!feature.websiteDataStores) throw {
    platformSkip: {
      platforms: ['macos'],
      source: 'https://github.com/tauri-apps/wry/blob/wry-v0.55.1/src/wkwebview/mod.rs',
      detail: 'macOS below 14 cannot safely execute the pinned identifier-management selectors; creation falls back to the default store.'
    }
  };
  let child;
  const close = async () => {
    if (!child) return;
    await child.close();
    child = null;
    await untilAsync(async () => await WebviewWindow.getByLabel('conformance-child') === null, 5000);
  };
  const phase = async (identifier, expected, write, incognito = false) => {
    let response;
    const unlisten = await listen('data-store-result', event => { response = event.payload; });
    try {
      const query = new URLSearchParams({ expected: expected ?? '', write: write ?? '' });
      child = await createWindow({
        url: `data-store.html?${query}`, visible: false, dataStoreIdentifier: identifier, incognito
      });
      await untilAsync(async () => response !== undefined, 5000);
      assert(response?.ok === true, response?.error || 'Website data-store page did not complete its assertions');
    } finally { unlisten(); }
  };
  const removeClosed = async identifier => {
    // WebKit can release a closed view's store asynchronously. Retry only that error.
    await untilAsync(async () => {
      try { await removeDataStore(identifier); return true; }
      catch (error) {
        if (String(error) === 'runtime error: failed to remove data store') return false;
        throw error;
      }
    }, 5000);
  };
  try {
    const [first, second] = identifiers;
    await phase(first, null, 'first');
    // WebKit may create the persistent store's directory after its first storage write.
    await untilAsync(async () => contains(await fetchDataStoreIdentifiers(), first), 5000);
    let activeError;
    try { await removeDataStore(first); } catch (error) { activeError = String(error); }
    assert(activeError === 'runtime error: failed to remove data store', 'Active-store removal did not reject with the pinned runtime error');
    await close();
    await phase(second, null, 'second');
    await untilAsync(async () => {
      const stores = await fetchDataStoreIdentifiers();
      return contains(stores, first) && contains(stores, second);
    }, 5000);
    await close();
    await phase(first, 'first', null);
    await close();
    await phase(second, 'second', null);
    await close();
    await phase(first, null, 'incognito', true);
    await close();
    await phase(first, 'first', null);
    await close();
    await removeClosed(first);
    assert(!contains(await fetchDataStoreIdentifiers(), first), 'Removed store remained enumerated');
    assert(contains(await fetchDataStoreIdentifiers(), second), 'Removal affected a different store');
    await phase(first, null, null);
    await close();
    await phase(second, 'second', null);
    return { enumeration: 'sixteen-byte-identifiers', activeRemoval: 'rejected', isolation: true,
      persistence: true, incognitoPrecedence: true, removal: true, recreation: 'empty' };
  } finally {
    try { await close(); }
    finally {
      // Attempt both test-owned stores even if one removal fails; never remove ambient stores.
      const cleanup = await Promise.allSettled(identifiers.map(removeClosed));
      const failure = cleanup.find(result => result.status === 'rejected');
      if (failure) throw failure.reason;
    }
  }
}
