// Adapted from tauri/scripts/ipc.js and isolation.js at the pinned upstream commit.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
{
  const scheme = __NETWIV_ISOLATION_SCHEME__;
  const src = bridge.os === 'windows'
    ? `${bridge.protocolScheme === 'https' ? 'https' : 'http'}://${scheme}.localhost/`
    : `${scheme}://localhost/`;
  const queue = [];
  let frame;
  let ready = false;
  window.addEventListener('pagehide', () => { queue.length = 0; });
  function serializePayload(data) {
    if (data !== null && typeof data === 'object') {
      if (data.constructor === Array) return data.map(serializePayload);
      if ('__TAURI_TO_IPC_KEY__' in data) return data.__TAURI_TO_IPC_KEY__();
      if (data.constructor === Object) {
        const result = {};
        for (const [key, value] of Object.entries(data)) result[key] = serializePayload(value);
        return result;
      }
    }
    return data;
  }
  function send(message) {
    message.payload = serializePayload(message.payload);
    frame.contentWindow.postMessage(message, '*');
  }
  sendIpc = message => {
    if (ready) send(message);
    else queue.push(message);
  };
  window.addEventListener('message', event => {
    // The sandboxed secure frame has an opaque origin; bind messages to its window.
    if (!frame || event.source !== frame.contentWindow) return;
    if (event.data === '__TAURI_ISOLATION_READY__') {
      if (ready) return;
      ready = true;
      for (const message of queue.splice(0)) {
        try { send(message); }
        catch (error) { runCallback(message.error, String(error)); }
      }
      return;
    }
    const message = event.data;
    if (message && typeof message === 'object' && message.payload &&
        Object.keys(message.payload).length > 0 &&
        Object.keys(message.payload).every(key => key === 'nonce' || key === 'payload' || key === 'contentType')) {
      sendToNative(message);
    }
  });
  window.addEventListener('DOMContentLoaded', () => {
    const style = document.createElement('style');
    style.textContent = '#__tauri_isolation__ { display: none !important }';
    document.head.append(style);
    frame = document.createElement('iframe');
    frame.id = '__tauri_isolation__';
    frame.sandbox.add('allow-scripts');
    frame.src = src;
    document.body.append(frame);
  }, { once: true });
}
