import { invoke } from './vendor/@netwiv/api/core.js';
const status = document.querySelector('#status'), next = document.querySelector('#next'), finish = document.querySelector('#finish');
const check = (value) => { if (!value) throw Error('Probe assertion failed'); };
try {
  const config = await invoke('probe_ready');
  if (config.denied) {
    let denied = false;
    try { await invoke('plugin:dialog|open', { options: {} }); } catch { denied = true; }
    check(denied); await invoke('probe_denied');
  } else if (!config.interactive) {
    status.textContent = 'Automatic native dialog cancellation/lifecycle checks are running. Do not interact with their dialogs.';
    if (config.guestAdmission) {
      void invoke('plugin:dialog|open', { options: { defaultPath: config.root } })
        .then(() => invoke('probe_guest_settled'), () => invoke('probe_guest_settled'));
      await invoke('probe_guest_started');
    }
  }
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
    let step = 0, busy = false;
    const stages = {
      'native-invoke': 'Native dialog invocation failed.',
      'native-acl': 'The guest dialog command was denied by capabilities.',
      'native-parent': 'The native dialog parent was unavailable.',
      'native-document': 'The source document or WebView was unavailable.',
      'native-options': 'The native dialog options were rejected.',
      'verify-invoke': 'Host selection verification failed.',
      'selection-shape': 'The result was canceled or had the wrong selection shape.',
      'selection-cardinality': 'Select exactly the requested number of synthetic entries.',
      'selection-match': 'Choose only the synthetic entries or button named in this step.',
      'asset-grant': 'The selected asset scope was not granted.',
      'recursive-grant': 'The recursive asset scope was not granted.',
      'fs-read': 'Reading the approved synthetic sample through the Fs plugin failed.',
      'fs-content': 'The approved synthetic sample content did not match.',
      'acknowledgment': 'The host could not acknowledge the completed step.'
    };
    const update = () => {
      document.querySelector('#instructions').textContent = steps[step]?.[0] ?? 'All selections checked. Confirm native appearance and closure, then Finish.';
      next.disabled = busy || step === steps.length;
      finish.disabled = busy || step !== steps.length || !document.querySelector('#visual').checked;
    };
    document.querySelector('#visual').onchange = update;
    next.onclick = async () => {
      if (busy || step === steps.length) return;
      busy = true; update();
      let stage = 'native-invoke';
      try {
        const [, command, options] = steps[step];
        const selected = await invoke('plugin:dialog|' + command, command === 'message'
          ? { message: 'Choose Accept to verify the custom result.', title: 'NetWiv message probe', buttons: { OkCancelCustom: ['Accept', 'Cancel'] } }
          : { options });
        // Host validates only known synthetic selections before any file read.
        stage = 'verify-invoke';
        const rejection = await invoke('probe_verify', { step, selected });
        if (rejection !== null) { stage = Object.hasOwn(stages, rejection) ? rejection : 'verify-invoke'; throw Error('Selection not accepted'); }
        if (step === 0 || step === 2 || step === 7) {
          stage = 'fs-read';
          const text = await invoke('plugin:fs|read_text_file', { path: step === 2 ? nested : config.root + '/alpha.txt' });
          stage = 'fs-content'; check(text === 'synthetic dialog sample');
        }
        stage = 'acknowledgment'; await invoke('probe_ack', { step });
        step++; next.textContent = 'Open next native dialog'; status.textContent = 'Selection and session scope checked.';
      } catch (error) {
        if (stage === 'native-invoke' && typeof error === 'string') {
          if (['open', 'save', 'message'].some(command => error === `Command plugin:dialog|${command} not allowed by ACL`)) stage = 'native-acl';
          else if (['dialog request has no parent window', 'dialog parent window was not found', 'dialog parent window was closed'].includes(error)) stage = 'native-parent';
          else if (['dialog source webview was closed', 'dialog source document changed', 'The originating document has been replaced.'].includes(error)) stage = 'native-document';
          else if (['missing dialog options', 'dialog message is missing', 'dialog text must not contain a NUL character', 'invalid custom dialog buttons'].includes(error)) stage = 'native-options';
        }
        status.textContent = `CHECK ${stage}: ${stages[stage]} Correct the selection or retry this step. No step has advanced.`;
        next.textContent = 'Retry current step';
        try { await invoke('probe_failure', { stage }); } catch { /* Keep diagnostics fixed and private. */ }
      } finally { busy = false; update(); }
    };
    finish.onclick = async () => { finish.disabled = true; await invoke('probe_finish', { visual: document.querySelector('#visual').checked }); };
    status.textContent = 'READY. Use only the synthetic files and folders listed below.'; update();
    await invoke('probe_interactive_ready');
  }
} catch { status.textContent = 'FAIL native dialog guest setup or permission check.'; }
