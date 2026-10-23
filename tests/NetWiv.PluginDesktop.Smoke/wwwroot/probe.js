import { invoke } from './vendor/@netwiv/api/core.js';
import { open } from './vendor/@netwiv/api/plugins/dialog.js';
import { readText, writeText } from './vendor/@netwiv/api/plugins/clipboard-manager.js';

const clipboardStatus = document.querySelector('#clipboard-status');
const dialogSelectStatus = document.querySelector('#dialog-select-status');
const dialogCancelStatus = document.querySelector('#dialog-cancel-status');
const notificationStatus = document.querySelector('#notification-status');
const fixture = await invoke('smoke_fixture');
const fixtureRoot = fixture.root;
let busy = false;
let clipboardComplete = !fixture.clipboardIncluded, dialogSelected = false, dialogCanceled = false, notificationSent = false;

if (!fixture.clipboardIncluded) {
  document.querySelector('#clipboard-ready').disabled = true;
  document.querySelector('#clipboard').disabled = true;
  clipboardStatus.textContent = 'NOT REQUESTED: clipboard check excluded from this run.';
}

function updateFinish() {
  document.querySelector('#finish').disabled = busy || !clipboardComplete || !dialogSelected || !dialogCanceled ||
    !notificationSent || !document.querySelector('#notification-visible').checked;
}

async function run(button, action) {
  if (busy) return;
  busy = true;
  button.disabled = true;
  try { await action(); }
  finally { busy = false; button.disabled = false; updateFinish(); }
}

document.querySelector('#clipboard').onclick = event => run(event.currentTarget, async () => {
  clipboardComplete = false;
  if (!document.querySelector('#clipboard-ready').checked) {
    clipboardStatus.textContent = 'CHECK: confirm disposable clipboard text first.';
    return;
  }
  let original;
  try { original = await readText(); }
  catch {
    clipboardComplete = false;
    clipboardStatus.textContent = 'SKIP: clipboard text could not be read; clipboard was not changed.';
    updateFinish();
    return;
  }

  const marker = 'synthetic NetWiv clipboard smoke';
  let roundtrip = false, restored = false, mutationAttempted = false;
  try {
    mutationAttempted = true;
    await writeText(marker);
    roundtrip = await readText() === marker;
  } catch { /* Fixed status below; never print clipboard contents or native errors. */ }
  finally {
    if (mutationAttempted) {
      try {
        await writeText(original);
        restored = await readText() === original;
      } catch { /* Fixed status below; never print clipboard contents or native errors. */ }
    }
  }
  clipboardStatus.textContent = roundtrip && restored
    ? 'PASS: synthetic text roundtripped and original text was restored.'
    : restored
      ? 'FAIL: synthetic text roundtrip did not match; original text was restored.'
      : 'FAIL: clipboard restoration could not be verified.';
  clipboardComplete = roundtrip && restored;
});

document.querySelector('#select').onclick = event => run(event.currentTarget, async () => {
  dialogSelected = false;
  try {
    const selected = await open({ defaultPath: fixtureRoot });
    dialogSelected = typeof selected === 'string' && await invoke('smoke_validate_selection', { selected });
    dialogSelectStatus.textContent = dialogSelected
      ? 'PASS: generated synthetic file selected.' : 'FAIL: selection did not match the generated synthetic file.';
  } catch { dialogSelectStatus.textContent = 'FAIL: native file selection failed.'; }
});

document.querySelector('#cancel').onclick = event => run(event.currentTarget, async () => {
  dialogCanceled = false;
  try {
    const selected = await open({ defaultPath: fixtureRoot });
    dialogCanceled = selected === null;
    dialogCancelStatus.textContent = dialogCanceled
      ? 'PASS: picker cancellation returned null.' : 'FAIL: picker returned a selection instead of cancellation.';
  } catch { dialogCancelStatus.textContent = 'FAIL: native picker cancellation failed.'; }
});

document.querySelector('#notify').onclick = event => run(event.currentTarget, async () => {
  notificationSent = false;
  const visible = document.querySelector('#notification-visible');
  visible.checked = false;
  visible.disabled = true;
  document.querySelector('#visibility-status').textContent = 'Visibility not confirmed.';
  try {
    await invoke('plugin:notification|notify', { options: {
      title: 'NetWiv notification smoke', body: 'Synthetic desktop notification'
    } });
    notificationSent = true;
    document.querySelector('#notification-visible').disabled = false;
    notificationStatus.textContent = 'PASS: notification command returned; visibility is not implied.';
  } catch { notificationStatus.textContent = 'FAIL: notification command transport.'; }
});

document.querySelector('#notification-visible').onchange = event => {
  document.querySelector('#visibility-status').textContent = event.currentTarget.checked
    ? 'MANUAL: notification visibility acknowledged.'
    : 'Visibility not confirmed.';
  updateFinish();
};

document.querySelector('#finish').onclick = async event => {
  event.currentTarget.disabled = true;
  try {
    await invoke('smoke_finish', { complete: true });
    document.querySelector('#finish-status').textContent = 'PASS: checks acknowledged; close the app window to exit cleanly.';
  } catch {
    notificationStatus.textContent = 'FAIL: the host did not accept the completed checks.';
    event.currentTarget.disabled = false;
    updateFinish();
  }
};
