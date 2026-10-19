export const sample = '한글 입력 확인 123';

export function newState() {
  return { menu: 0, tray: 0, keyboard: 0, starts: 0, ends: 0, composing: false,
    input: 0, resized: 0, blurred: false, refocused: false, maximized: false, restored: false, dragCalls: 0 };
}

export function composition(state, kind, trusted) {
  if (!trusted) return;
  if (kind === 'start') { state.starts++; state.composing = true; }
  if (kind === 'end' && state.composing) { state.ends++; state.composing = false; }
}

export function shortcut(state, event, focused) {
  if (!event.isTrusted || !focused || event.repeat || event.isComposing
      || !event.ctrlKey || !event.shiftKey || event.altKey || event.metaKey
      || event.code !== 'KeyK') return false;
  state.keyboard++;
  return true;
}

export function missingChecks(state, text, confirmations) {
  const missing = [];
  if (state.menu < 1) missing.push('native menu action');
  if (state.tray < 1) missing.push('native tray menu action');
  if (state.keyboard < 1) missing.push('focused Ctrl+Shift+K');
  if (state.starts < 1 || state.ends < 1 || state.composing || state.input < 1
      || text.normalize('NFC') !== sample) missing.push('IME composition and exact sample');
  if (state.resized < 1) missing.push('native resize event with changed size');
  if (!state.blurred || !state.refocused) missing.push('native focus loss and return');
  if (!state.maximized || !state.restored) missing.push('maximize and restore state checks');
  if (state.dragCalls < 1) missing.push('drag-region API call');
  if (confirmations.length !== 4 || !confirmations.every(value => value === true))
    missing.push('all manual visual confirmations');
  return missing;
}
