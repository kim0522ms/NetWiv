import { invoke } from './vendor/@tauri-apps/api/core.js';
const status = document.querySelector('#status'), next = document.querySelector('#next'), finish = document.querySelector('#finish');
const check = (value) => { if (!value) throw Error('Probe assertion failed'); };
try {
  const config = await invoke('probe_ready');
  if (config.denied) {
    let denied = false;
    try { await invoke('plugin:dialog|open', { options: {} }); } catch { denied = true; }
    check(denied); await invoke('probe_denied');
  } else if (!config.interactive) status.textContent = 'Automatic native dialog cancellation/lifecycle checks are running. Do not interact with their dialogs.';
  else {
    document.querySelector('#root').textContent = config.root;
    let denied = false;
    const nested = config.root + '/folder-one/nested/inside.txt';
    try { await invoke('plugin:fs|read_text_file', { path: nested }); } catch { denied = true; }
    check(denied);
    const steps = [
      ['Select alpha.txt.', 'open', { defaultPath: config.root }],
      ['Select BOTH alpha.txt and beta.txt.', 'open', { defaultPath: config.root, multiple: true }],
      ['Select folder-one. Its nested synthetic file must become readable.', 'open', { defaultPath: config.root, directory: true, recursive: true }],
      ['Select BOTH folder-one and folder-two.', 'open', { defaultPath: config.root, directory: true, multiple: true }],
      ['Save as saved.txt in the sample directory. The probe does not write through the returned path.', 'save', { defaultPath: config.root + '/saved.txt' }],
      ['Choose Accept in the native custom-button message.', 'message', null],
      ['Cancel the file picker.', 'open', { defaultPath: config.root }],
      ['Select alpha.txt again to verify repeated use after cancellation.', 'open', { defaultPath: config.root }]
    ];
    let step = 0;
    const update = () => {
      document.querySelector('#instructions').textContent = steps[step]?.[0] ?? 'All selections checked. Confirm native appearance and closure, then Finish.';
      next.disabled = step === steps.length;
      finish.disabled = step !== steps.length || !document.querySelector('#visual').checked;
    };
    document.querySelector('#visual').onchange = update;
    next.onclick = async () => {
      next.disabled = true;
      try {
        const [, command, options] = steps[step];
        const selected = await invoke('plugin:dialog|' + command, command === 'message'
          ? { message: 'Choose Accept to verify the custom result.', title: 'NetWiv message probe', buttons: { OkCancelCustom: ['Accept', 'Cancel'] } }
          : { options });
        // Host validates only known synthetic selections before any file read.
        await invoke('probe_verify', { step, selected });
        if (step === 0 || step === 7) check(await invoke('plugin:fs|read_text_file', { path: config.root + '/alpha.txt' }) === 'synthetic dialog sample');
        if (step === 2) check(await invoke('plugin:fs|read_text_file', { path: nested }) === 'synthetic dialog sample');
        step++; status.textContent = 'Selection and session scope checked.'; update();
      } catch { status.textContent = 'FAIL native dialog selection or scope check. Close the probe; no path or native error details are displayed.'; }
    };
    finish.onclick = async () => { finish.disabled = true; await invoke('probe_finish', { visual: document.querySelector('#visual').checked }); };
    status.textContent = 'READY. Use only the synthetic files and folders listed below.'; update();
    await invoke('probe_interactive_ready');
  }
} catch { status.textContent = 'FAIL native dialog guest setup or permission check.'; }
