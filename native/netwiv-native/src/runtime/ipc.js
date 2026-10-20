(() => {
  if (window.top !== window) return;
  const post = window.webkit.messageHandlers.ipc.postMessage.bind(window.webkit.messageHandlers.ipc);
  const nativeFetch = window.fetch.bind(window);
  const NativeHeaders = window.Headers;
  const setHeader = Function.call.bind(NativeHeaders.prototype.set);
  const stringify = JSON.stringify.bind(JSON);
  const NativeURL = window.URL;
  const random = crypto.getRandomValues.bind(crypto);
  const createNonce = () => Array.from(random(new Uint8Array(32)), value => value.toString(16).padStart(2, '0')).join('');
  let nonce = createNonce();
  let capability;
  let documentId = 0;
  let pending = [];
  window.addEventListener('pagehide', () => {
    capability = undefined;
    pending = [];
    nonce = createNonce();
  });
  const send = message => {
    const payload = typeof message === 'string' ? message : stringify(message);
    const deliver = () => post(stringify({ capability, documentId, payload }));
    if (capability) deliver();
    else pending.push(deliver);
  };
  const fetchIpc = (url, options) => new Promise((resolve, reject) => {
    const target = new NativeURL(url);
    if (target.protocol !== 'ipc:' || target.hostname !== 'localhost' || target.port || target.username || target.password) {
      reject(new TypeError('The private IPC transport requires ipc://localhost.'));
      return;
    }
    const deliver = () => {
      try {
        const headers = new NativeHeaders(options.headers);
        setHeader(headers, 'NetWiv-Document-Capability', capability);
        setHeader(headers, 'NetWiv-Document-Id', String(documentId));
        nativeFetch(target.href, { ...options, headers, redirect: 'error' }).then(resolve, reject);
      } catch (error) { reject(error); }
    };
    if (capability) deliver();
    else pending.push(deliver);
  });
  Object.defineProperty(window, '__NETWIV_IPC_NONCE__', { value: () => nonce });
  Object.defineProperty(window, '__NETWIV_ACTIVATE_IPC__', {
    value: (expectedNonce, epoch, secret) => {
      // A delayed native grant must not drain another document's startup queue.
      if (expectedNonce !== nonce || epoch < documentId) return;
      capability = secret;
      documentId = epoch;
      const queued = pending;
      pending = [];
      for (const deliver of queued) deliver();
    }
  });
  Object.defineProperty(window, '__NETWIV_DOCUMENT_TRANSPORT__', { value: Object.freeze({ send, fetchIpc }) });
})();
