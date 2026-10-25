(() => {
  if (window.top !== window) return;
  const random = crypto.getRandomValues.bind(crypto);
  const stringify = JSON.stringify.bind(JSON);
  const resolve = Promise.resolve.bind(Promise);
  const then = Function.call.bind(Promise.prototype.then);
  const entries = new Map();
  const get = Function.call.bind(Map.prototype.get);
  const set = Function.call.bind(Map.prototype.set);
  const remove = Function.call.bind(Map.prototype.delete);
  const clear = Function.call.bind(Map.prototype.clear);
  const size = Function.call.bind(Object.getOwnPropertyDescriptor(Map.prototype, 'size').get);
  const createNonce = () => Array.from(random(new Uint8Array(32)), value => value.toString(16).padStart(2, '0')).join('');
  let nonce = createNonce();
  const cancelled = '{"kind":"cancelled"}';
  const text = (read, fallback) => { try { return String(read()); } catch { return fallback; } };
  const bridge = (operation, expected, key, value) => {
    if (operation === 'nonce') return nonce;
    if (expected !== nonce) return cancelled;
    const state = get(entries, key);
    if (operation === 'poll') return state ? state.result : cancelled;
    if (operation === 'cancel') { remove(entries, key); return null; }
    if (operation === 'resolve') {
      if (!state) return cancelled;
      if (value === null || (typeof value !== 'object' && typeof value !== 'function')) state.finish(value);
      else then(resolve(value), state.finish, state.fail);
      return null;
    }
    if (operation === 'reject') { if (!state) return cancelled; state.fail(value); return null; }
    if (operation !== 'prepare' || typeof key !== 'string' || state || size(entries) >= 64) return cancelled;
    const pending = { result: null };
    pending.fail = value => {
      pending.result = stringify({ kind: 'error', name: text(() => value?.name ?? 'Error', 'Error'),
        message: text(() => value?.message ?? value, 'JavaScript evaluation failed'),
        stack: text(() => value?.stack ?? '', '') });
    };
    pending.finish = value => {
      try {
        const json = stringify(value);
        pending.result = '{"kind":"result","value":' + (json === undefined ? 'null' : json) + '}';
      } catch (value) { pending.fail(value); }
    };
    set(entries, key, pending);
    return 'started';
  };
  Object.defineProperty(globalThis, '__NETWIV_EVALUATE__', { value: bridge });
  addEventListener('pagehide', () => { nonce = createNonce(); clear(entries); });
})()
