namespace NetWiv;

internal static class InitializationScript
{
    public static string Create(string invokeKey, bool freezePrototype, string? isolationScheme = null) => $$"""
        (() => {
          if (window.top !== window) {
            try {
              if (window.top.location.origin !== window.location.origin) return;
            } catch { return; }
          }
          {{(isolationScheme is null ? string.Empty : "if (window.top !== window) return;")}}
          const bridge = window.__NETWIV_IPC__;
          if (!bridge || typeof bridge.postMessage !== 'function') {
            throw new Error('NetWiv IPC bridge is unavailable');
          }
          {{(freezePrototype ? "Object.freeze(Object.prototype);" : string.Empty)}}
          const callbacks = new Map();
          const eventCallbacks = new Map();
          const pendingInvocations = new Map();
          const documentNonce = Array.from(crypto.getRandomValues(new Uint8Array(16)),
            value => value.toString(16).padStart(2, '0')).join('');
          const isolatedDocument = {{(isolationScheme is null ? "false" : "true")}};
          let documentActive = true;
          let clientGeneration = 0;
          const documentHiddenError = () => {
            const error = new Error('The originating document was hidden.');
            error.name = 'AbortError';
            return error;
          };
          window.addEventListener('pagehide', () => {
            documentActive = false;
            clientGeneration++;
            const error = documentHiddenError();
            for (const reject of pendingInvocations.values()) reject(error);
            pendingInvocations.clear();
            callbacks.clear();
            eventCallbacks.clear();
          });
          window.addEventListener('pageshow', () => { documentActive = true; });
          const transformCallback = (callback, once = false) => {
            let id;
            do { id = crypto.getRandomValues(new Uint32Array(1))[0]; } while (callbacks.has(id));
            callbacks.set(id, data => {
              if (once) callbacks.delete(id);
              if (callback) return callback(data);
            });
            return id;
          };
          const runCallback = (id, data) => {
            const callback = callbacks.get(id);
            if (callback) callback(data);
          };
          const ipcReplacer = (_key, value) => {
            if (value instanceof Map) return Object.fromEntries(value.entries());
            if (value instanceof Uint8Array) return Array.from(value);
            if (value instanceof ArrayBuffer) return Array.from(new Uint8Array(value));
            if (typeof value === 'object' && value !== null && '__TAURI_TO_IPC_KEY__' in value) {
              return value.__TAURI_TO_IPC_KEY__();
            }
            return value;
          };
          let protocolIpcFailed = false;
          const sendToNative = message => {
            // Isolation can finish preparing a message after its document retired.
            if (message.__NETWIV_CLIENT_GENERATION__ !== clientGeneration || !pendingInvocations.has(message.error)) return;
            let payload = message.payload;
            const binary = ArrayBuffer.isView(payload) || payload instanceof ArrayBuffer || Array.isArray(payload);
            // JSON text must stay opaque to JavaScript, including on protocol-enabled hosts.
            if (bridge.protocolIpc && !message.__NETWIV_JSON__) {
              if (!protocolIpcFailed) {
                const generation = clientGeneration;
                const headers = new Headers(message.options?.headers || {});
                headers.set('Content-Type', binary ? 'application/octet-stream' : 'application/json');
                headers.set('Tauri-Callback', message.callback);
                headers.set('Tauri-Error', message.error);
                headers.set('Tauri-Invoke-Key', '{{invokeKey}}');
                (bridge.fetchIpc || fetch)(window.__TAURI_INTERNALS__.convertFileSrc(message.cmd, 'ipc'), {
                  method: 'POST', body: binary ? payload : JSON.stringify(payload, ipcReplacer), headers
                }).then(async response => {
                  const callback = response.headers.get('Tauri-Response') === 'ok' ? message.callback : message.error;
                  const type = (response.headers.get('content-type') || '').split(',')[0];
                  const value = type === 'application/json' ? await response.json() :
                    type === 'text/plain' ? await response.text() : await response.arrayBuffer();
                  return [callback, value];
                }).then(([callback, value]) => {
                  if (generation === clientGeneration) runCallback(callback, value);
                }, error => {
                  if (generation !== clientGeneration) return;
                  protocolIpcFailed = true;
                  // A lost response can follow successful native dispatch. Never
                  // replay this command; use the message transport for future calls.
                  runCallback(message.error, error);
                });
              } else {
                bridge.postMessage(JSON.stringify({ ...message,
                  options: { ...message.options, customProtocolIpcBlocked: true },
                  __NETWIV_INVOKE_KEY__: '{{invokeKey}}'
                }, ipcReplacer));
              }
              return;
            }
            if (message.options && message.options.headers) message = {
              ...message, options: { ...message.options, headers: Object.fromEntries(new Headers(message.options.headers)) }
            };
            if (binary) payload = Array.from(ArrayBuffer.isView(payload)
              ? new Uint8Array(payload.buffer, payload.byteOffset, payload.byteLength) : new Uint8Array(payload));
            bridge.postMessage(JSON.stringify({
              ...message, payload, __NETWIV_BODY_TYPE__: binary ? 'application/octet-stream' : 'application/json',
              __NETWIV_INVOKE_KEY__: '{{invokeKey}}'
            }, ipcReplacer));
          };
          let sendIpc = sendToNative;
          {{(isolationScheme is null ? string.Empty : IsolationPattern.TransportScript(isolationScheme))}}
          const invokeWithCallbacks = send => new Promise((resolve, reject) => {
            if (!documentActive) { reject(documentHiddenError()); return; }
            let callback;
            let error;
            callback = transformCallback(value => { callbacks.delete(error); pendingInvocations.delete(error); resolve(value); }, true);
            error = transformCallback(value => { callbacks.delete(callback); pendingInvocations.delete(error); reject(value); }, true);
            pendingInvocations.set(error, reject);
            try {
              send(callback, error);
            } catch (exception) {
              callbacks.delete(callback); callbacks.delete(error); pendingInvocations.delete(error); reject(exception);
            }
          });
          const invoke = (cmd, args = {}, options, json = false) => invokeWithCallbacks((callback, error) =>
            sendIpc({ cmd, callback, error, payload: args, options, __NETWIV_JSON__: json, __NETWIV_CLIENT_GENERATION__: clientGeneration }));
          const getFilePath = async file => {
            if (bridge.os !== 'windows' || typeof bridge.postFileMessage !== 'function' || isolatedDocument || window.top !== window)
              throw new Error('FilePathNotSupported');
            if (!documentActive) throw documentHiddenError();
            if (typeof File === 'undefined' || !(file instanceof File)) throw new Error('FilePathUnavailable');
            const generation = clientGeneration;
            // Keep authorization on the WebView message channel so native sees
            // the same full document source as the AdditionalObjects message.
            const ticket = await invokeWithCallbacks((callback, error) => bridge.postMessage(JSON.stringify({
              cmd: 'plugin:webview|get_file_path', callback, error, payload: { nonce: documentNonce, generation },
              __NETWIV_BODY_TYPE__: 'application/json', __NETWIV_INVOKE_KEY__: '{{invokeKey}}',
              __NETWIV_CLIENT_GENERATION__: generation
            })));
            if (!documentActive || generation !== clientGeneration) throw documentHiddenError();
            return invokeWithCallbacks((callback, error) => {
              try {
                bridge.postFileMessage(JSON.stringify({ ticket, nonce: documentNonce, generation, callback, error }), file);
              } catch (exception) {
                if (exception?.message === 'FilePathNotSupported') throw exception;
                throw new Error('FilePathUnavailable');
              }
            });
          };
          const runFileCallback = (nonce, generation, id, data) => {
            if (!documentActive || nonce !== documentNonce || generation !== clientGeneration || !callbacks.has(id)) return false;
            runCallback(id, data);
            return true;
          };
          const internals = window.__TAURI_INTERNALS__ || {};
          const isWindows = bridge.os === 'windows';
          Object.assign(internals, {
            plugins: {
              ...(internals.plugins || {}),
              path: { sep: isWindows ? '\\' : '/', delimiter: isWindows ? ';' : ':' }
            },
            callbacks,
            transformCallback,
            unregisterCallback: id => callbacks.delete(id),
            runCallback,
            runFileCallback,
            invoke,
            getFilePath,
            invokeJson: (cmd, args = '{}', options) => {
              if (typeof args !== 'string') return Promise.reject(new TypeError('invokeJson requires JSON text'));
              return invoke(cmd, args, options, true);
            },
            convertFileSrc: (path, protocol = 'asset') => isWindows
              ? `${bridge.protocolScheme === 'https' ? 'https' : 'http'}://${protocol}.localhost/${encodeURIComponent(path)}`
              : `${protocol}://localhost/${encodeURIComponent(path)}`,
            metadata: {
              currentWindow: { label: bridge.windowLabel || bridge.webviewLabel || 'main' },
              currentWebview: {
                windowLabel: bridge.windowLabel || bridge.webviewLabel || 'main',
                label: bridge.webviewLabel || 'main'
              }
            }
          });
          Object.defineProperty(window, '__TAURI_INTERNALS__', { value: internals, configurable: true });
          window.__TAURI_EVENT_PLUGIN_INTERNALS__ = {
            registerListener: (id, callback) => eventCallbacks.set(id, callback),
            unregisterListener: (_event, id) => {
              const callback = eventCallbacks.get(id);
              if (callback !== undefined) callbacks.delete(callback);
              eventCallbacks.delete(id);
            }
          };
        })();
        """;
}
