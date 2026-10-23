(() => {
  const send = message => window.__NETWIV_IPC__.postMessage(message);
  const assert = (condition, message) => { if (!condition) throw new Error(message); };

  async function waitFor(predicate) {
    const limit = performance.now() + 15000;
    while (!predicate()) {
      if (performance.now() > limit) throw new Error("Browser assertion timed out.");
      await new Promise(resolve => setTimeout(resolve, 20));
    }
  }

  async function run() {
    await waitFor(() => document.getElementById("rcl-route-link"));
    document.getElementById("rcl-route-link").click();
    await waitFor(() => document.getElementById("rcl-route-content") && window.__netwivManagedRclModuleLoaded);

    document.getElementById("rcl-submit").click();
    await waitFor(() => document.getElementById("rcl-form-status").textContent === "Invalid");
    assert(document.querySelector(".validation-errors li"), "The required RCL form field did not report validation.");

    const name = document.getElementById("rcl-name");
    name.value = "NetWiv";
    name.dispatchEvent(new Event("change", { bubbles: true }));
    document.getElementById("rcl-submit").click();
    await waitFor(() => document.getElementById("rcl-form-status").textContent === "Valid");

    document.getElementById("result").textContent = "PASS";
    send("__managed-rcl-proof:" + JSON.stringify({ passed: true }));
  }

  function failed(error) {
    document.getElementById("result").textContent = "FAIL";
    send("__managed-rcl-proof:" + JSON.stringify({ passed: false, error: String(error) }));
  }

  window.addEventListener("error", event => failed(event.error || event.message));
  window.addEventListener("unhandledrejection", event => failed(event.reason));
  run().catch(failed);
})();
