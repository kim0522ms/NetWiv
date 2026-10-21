(() => {
  const send = message => window.__NETWIV_IPC__.postMessage(message);
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  const input = {
    name: "input", count: 3,
    items: [{ text: "first", value: "9223372036854775807" }, null, { text: "last", value: "-5" }],
    optional: null
  };
  let instance;
  let disposedInstance;
  let jsDisposedInstance;
  let ready = false;
  let voidCalled = false;
  let receivedDto = false;
  window.proof = {
    VersionBox: class { constructor(value) { this.value = value; } },
    dto(value) {
      assert(JSON.stringify(value) === JSON.stringify(input), "Generated DTO input differs.");
      receivedDto = true;
      return {
        name: "reply", count: 7,
        items: [{ text: "nested reply", value: "-9223372036854775808" }, null],
        optional: { text: "optional reply", value: "42" }
      };
    },
    voidCall() { voidCalled = true; return window; },
    setInstances(live, disposed, jsDisposed) {
      instance = live; disposedInstance = disposed; jsDisposedInstance = jsDisposed;
    },
    ready() { ready = true; }
  };

  async function waitFor(predicate) {
    const limit = performance.now() + 15000;
    while (!predicate()) {
      if (performance.now() > limit) throw new Error("Browser assertion timed out.");
      await new Promise(resolve => setTimeout(resolve, 20));
    }
  }
  async function checkDto(promise, prefix, count) {
    const actual = await promise;
    assert(actual.name === prefix + input.name && actual.count === count, "JSInvokable result mismatch.");
    assert(actual.items[0].value === "9223372036854775807" &&
      actual.items[1] === null && actual.optional === null, "Nested DTO/long strings changed.");
  }
  async function run() {
    const foreign = document.createElement('iframe');
    const attempted = new Promise(resolve => {
      window.addEventListener('message', event => {
        if (event.source === foreign.contentWindow && event.data?.transportAttempted !== undefined) resolve(event.data.transportAttempted);
      });
    });
    foreign.src = 'data:text/html,' + encodeURIComponent(`<script>
      const raw = window.webkit?.messageHandlers?.ipc || window.chrome?.webview;
      if (raw) {
        raw.postMessage('__transport-forged:raw');
        raw.postMessage(JSON.stringify({ capability: 'forged', documentId: 1, payload: '__transport-forged:envelope' }));
      }
      parent.postMessage({transportAttempted: !!raw}, '*');
    <\/script>`);
    document.body.appendChild(foreign);
    assert(await attempted, 'The foreign frame did not expose the raw native handler.');
    let credentialExposed = false;
    const originalToJson = Object.getOwnPropertyDescriptor(Object.prototype, 'toJSON');
    const originalIterator = Headers.prototype[Symbol.iterator];
    const linux = window.__NETWIV_IPC__.os === 'linux';
    if (linux) {
      Object.defineProperty(Object.prototype, 'toJSON', { configurable: true, value() {
        if (typeof this.capability === 'string') credentialExposed = true;
        return this;
      } });
      Headers.prototype[Symbol.iterator] = function () {
        if (this.get('NetWiv-Document-Capability')) credentialExposed = true;
        return originalIterator.call(this);
      };
    }
    try {
      assert(await window.__TAURI_INTERNALS__.invoke('transport_echo', { input: 'protocol' }) === 'protocol', 'Native protocol command failed.');
      assert(await window.__TAURI_INTERNALS__.invokeJson('transport_echo', '{"input":"message"}') === '"message"', 'Native message command failed.');
    } finally {
      if (linux) {
        if (originalToJson) Object.defineProperty(Object.prototype, 'toJSON', originalToJson);
        else delete Object.prototype.toJSON;
        Headers.prototype[Symbol.iterator] = originalIterator;
      }
    }
    assert(!credentialExposed, 'A modified JavaScript prototype observed private transport credentials.');
    await waitFor(() => ready && receivedDto && document.getElementById("aot-click"));
    assert(voidCalled && window.proof.moduleCalled, "Standard void/module interop failed.");
    if (window.__NETWIV_IPC__.os === 'linux' && !new URLSearchParams(location.search).has('netwiv-proof-phase') && window.name !== 'netwiv-hybrid-back') {
      const nonce = window.__NETWIV_IPC_NONCE__();
      const failingUrl = await window.__TAURI_INTERNALS__.invoke('failure_url');
      location.href = failingUrl;
      const pending = window.__TAURI_INTERNALS__.invokeJson('transport_echo', '{"input":"pending"}');
      document.getElementById('aot-click').click();
      await new Promise(resolve => setTimeout(() => { window.stop(); resolve(); }, 300));
      assert(await pending === '"pending"', 'Message IPC stalled across canceled provisional navigation.');
      await waitFor(() => document.querySelector('output')?.textContent === 'count:1');
      assert(window.__NETWIV_IPC_NONCE__() === nonce, 'Canceled navigation replaced the committed document.');
      assert(await window.__TAURI_INTERNALS__.invoke('transport_echo', { input: 'after-cancel' }) === 'after-cancel', 'Protocol IPC stalled after canceled navigation.');
      assert(await window.__TAURI_INTERNALS__.invokeJson('transport_echo', '{"input":"after-message"}') === '"after-message"', 'Message IPC stalled after canceled navigation.');
    } else document.getElementById("aot-click").click();
    await waitFor(() => document.querySelector("output")?.textContent === "count:1");
    const text = document.getElementById("text");
    text.value = "changed";
    text.dispatchEvent(new Event("change", { bubbles: true }));
    const checkbox = document.getElementById("checked");
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event("change", { bubbles: true }));
    const selection = document.getElementById("selection");
    selection.value = "second";
    selection.dispatchEvent(new Event("change", { bubbles: true }));
    await waitFor(() => document.getElementById("bindings").textContent === "changed|True|second");
    await waitFor(() => document.getElementById("cascade").textContent === "child:changed");
    assert(document.getElementById("cascade").getAttribute("data-same-scope") === "True", "Child component DI scope differs.");

    const assembly = "NetWiv.BlazorHybrid.Aot.Tests";
    await checkDto(DotNet.invokeMethodAsync(assembly, "StaticTask", input), "static-task:", 13);
    await checkDto(DotNet.invokeMethodAsync(assembly, "StaticValueTask", input), "static-value-task:", 23);
    assert(await DotNet.invokeMethodAsync(assembly, "StaticNumber", 21) === 42, "Static ValueTask<int> failed.");

    await checkDto(instance.invokeMethodAsync("InstanceTask", input), "instance:task:", 33);
    await checkDto(instance.invokeMethodAsync("InstanceValueTask", input), "instance:value-task:", 43);
    assert(await instance.invokeMethodAsync("InstanceNumber", 41) === 42, "Instance ValueTask<int> failed.");
    let disposedRejected = false;
    try { await disposedInstance.invokeMethodAsync("InstanceNumber", 1); }
    catch { disposedRejected = true; }
    assert(disposedRejected, ".NET-disposed callback was accepted.");
    jsDisposedInstance.dispose();
    let jsDisposedRejected = false;
    try { await jsDisposedInstance.invokeMethodAsync("InstanceNumber", 1); }
    catch { jsDisposedRejected = true; }
    assert(jsDisposedRejected, "JS-disposed callback was accepted.");

    let rejected = false;
    try { await DotNet.invokeMethodAsync(assembly, "NotRegistered", input); }
    catch { rejected = true; }
    assert(rejected, "An unregistered method was accepted.");
    const before = await window.__TAURI_INTERNALS__.invoke('transport_count');
    let responseRejected = false;
    try { await window.__TAURI_INTERNALS__.invoke('transport_increment'); }
    catch { responseRejected = true; }
    assert(responseRejected, 'A malformed protocol response did not reject.');
    assert(await window.__TAURI_INTERNALS__.invoke('transport_count') === before + 1,
      'Protocol response failure replayed the command or broke subsequent message IPC.');
    document.getElementById("result").textContent = "PASS";
    send("__aot-proof:" + JSON.stringify({ passed: true, phase: Number(new URLSearchParams(location.search).get("netwiv-proof-phase") || 0), navigationType: performance.getEntriesByType('navigation')[0]?.type }));
  }
  function failed(error) {
    document.getElementById("result").textContent = "FAIL";
    send("__aot-proof:" + JSON.stringify({ passed: false, error: String(error) }));
  }
  window.addEventListener("error", event => failed(event.error || event.message));
  window.addEventListener("unhandledrejection", event => failed(event.reason));
  run().catch(failed);
})();
