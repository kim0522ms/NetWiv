(protocolIpc => {
  if (window.top !== window) return;
  const post = window.webkit.messageHandlers.ipc.postMessage.bind(window.webkit.messageHandlers.ipc);
  const nativeFetch = window.fetch.bind(window);
  const NativeHeaders = window.Headers;
  const forEachHeader = Function.call.bind(NativeHeaders.prototype.forEach);
  const stringify = JSON.stringify.bind(JSON);
  const NativeURL = window.URL;
  const [urlProtocol, urlHostname, urlPort, urlUsername, urlPassword, urlHref] =
    ['protocol', 'hostname', 'port', 'username', 'password', 'href'].map(name =>
      Function.call.bind(Object.getOwnPropertyDescriptor(NativeURL.prototype, name).get));
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
    const deliver = () => post(stringify({ __proto__: null, capability, documentId, payload }));
    if (capability) deliver();
    else pending.push(deliver);
  };
  const fetchIpc = (url, options) => new Promise((resolve, reject) => {
    if (!protocolIpc) {
      reject(new TypeError('The private IPC protocol transport is unavailable.'));
      return;
    }
    const target = new NativeURL(url);
    if (urlProtocol(target) !== 'ipc:' || urlHostname(target) !== 'localhost' ||
        urlPort(target) || urlUsername(target) || urlPassword(target)) {
      reject(new TypeError('The private IPC transport requires ipc://localhost.'));
      return;
    }
    // Snapshot before caller-controlled headers/options or queued delivery can run.
    const href = urlHref(target);
    const deliver = () => {
      try {
        const headers = { __proto__: null };
        forEachHeader(new NativeHeaders(options.headers), (value, name) => { headers[name] = value; });
        headers['netwiv-document-capability'] = capability;
        headers['netwiv-document-id'] = String(documentId);
        nativeFetch(href, { __proto__: null, ...options, headers, redirect: 'error' }).then(resolve, reject);
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
})
