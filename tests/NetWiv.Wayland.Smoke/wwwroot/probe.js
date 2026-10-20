import { invoke } from './vendor/@netwiv/api/core.js';
import { Menu, MenuItem, Submenu } from './vendor/@netwiv/api/menu.js';
import { TrayIcon } from './vendor/@netwiv/api/tray.js';
import { Image } from './vendor/@netwiv/api/image.js';
import { getCurrentWindow } from './vendor/@netwiv/api/window.js';
import { newState, composition, shortcut, missingChecks } from './checks.mjs';

const state = newState();
const owned = [], unlisten = [];
const win = getCurrentWindow();
const text = document.querySelector('#sample');
const status = document.querySelector('#status');
const notice = document.querySelector('#notice');
const finish = document.querySelector('#finish');
const track = resource => { owned.push(resource); return resource; };
const trayId = 'netwiv-wayland-interaction-probe';
let ready = false, ending = false, cleanupPromise, previousMenu, menuAttached = false;

function show() {
  if (ending) return;
  const pending = missingChecks(state, text.value,
    [...document.querySelectorAll('.manual')].map(input => input.checked));
  status.textContent = `Native menu: ${state.menu} / tray menu: ${state.tray} / Ctrl+Shift+K: ${state.keyboard}\n`
    + `IME start/end: ${state.starts}/${state.ends} / composing: ${state.composing}\n`
    + `Changed resize events: ${state.resized} / focus lost/returned: ${state.blurred}/${state.refocused}\n`
    + `Maximized/restored: ${state.maximized}/${state.restored} / drag calls: ${state.dragCalls}\n`
    + (pending.length ? `Remaining: ${pending.join(', ')}` : '자동 조건과 수동 확인 완료. 종료 버튼을 누르세요.');
  finish.disabled = !ready || pending.length > 0;
}

function cleanup() {
  cleanupPromise ??= (async () => {
    let failed = false;
    for (const stop of unlisten.splice(0)) {
      try { await stop(); } catch { failed = true; }
    }
    if (menuAttached) {
      try {
        const replacement = previousMenu ?? track(await Menu.new());
        const displaced = await replacement.setAsWindowMenu(win);
        if (displaced) track(displaced);
      } catch { failed = true; }
      menuAttached = false;
    }
    while (owned.length) {
      try { await owned.pop().close(); } catch { failed = true; }
    }
    try { if (await TrayIcon.getById(trayId) !== null) failed = true; }
    catch { failed = true; }
    if (failed) throw Error('Probe resource cleanup failed');
  })();
  return cleanupPromise;
}

async function complete(passed, error) {
  if (ending) return;
  ending = true;
  finish.disabled = true;
  document.querySelector('#abort').disabled = true;
  clearTimeout(deadline);
  status.textContent = passed ? '검사 완료. 네이티브 리소스 정리 중…' : 'FAIL / 미확인. 네이티브 리소스 정리 중…';
  if (error) notice.textContent = String(error);
  try { await cleanup(); }
  catch { passed = false; notice.textContent = 'Probe resource cleanup failed'; }
  try { await invoke('wayland_finish', { passed }); }
  catch { status.textContent = 'FAIL: 종료 명령 실패. 창을 닫으세요. Watchdog exits nonzero.'; }
}

const deadline = setTimeout(() => void complete(false, 'Interactive deadline reached'), 590_000);
document.querySelector('#abort').onclick = () => void complete(false);
finish.onclick = () => {
  const pending = missingChecks(state, text.value,
    [...document.querySelectorAll('.manual')].map(input => input.checked));
  void complete(ready && pending.length === 0);
};
window.addEventListener('unhandledrejection', event => { event.preventDefault(); void complete(false, event.reason); });

try {
  const menuAction = track(await MenuItem.new({ id: 'wayland-native-menu', text: 'Native menu action',
    action: () => { state.menu++; show(); } }));
  const submenu = track(await Submenu.new({ text: 'Probe', items: [menuAction] }));
  const menu = track(await Menu.new({ items: [submenu] }));
  previousMenu = await menu.setAsWindowMenu(win);
  if (previousMenu) track(previousMenu);
  menuAttached = true;

  const trayAction = track(await MenuItem.new({ id: 'wayland-native-tray', text: 'Native tray action',
    action: () => { state.tray++; show(); } }));
  const trayMenu = track(await Menu.new({ items: [trayAction] }));
  const rgba = new Uint8Array(22 * 22 * 4);
  for (let y = 3; y < 19; y++) for (let x = 3; x < 19; x++) {
    if (x < 7 || x > 14 || (x + y >= 18 && x + y <= 24))
      rgba.set([30, 110, 230, 255], (y * 22 + x) * 4);
  }
  const icon = track(await Image.new(rgba, 22, 22));
  track(await TrayIcon.new({ id: trayId, title: 'NW', tooltip: 'NetWiv Wayland probe', icon, menu: trayMenu }));

  const baseline = await win.innerSize();
  unlisten.push(await win.onResized(event => {
    const size = event.payload;
    if (!Number.isFinite(size.width) || !Number.isFinite(size.height) || size.width <= 0 || size.height <= 0) {
      void complete(false, 'Invalid native resize payload'); return;
    }
    if (size.width !== baseline.width || size.height !== baseline.height) state.resized++;
    show();
  }));
  unlisten.push(await win.onFocusChanged(event => {
    if (event.payload === false) state.blurred = true;
    else if (event.payload === true && state.blurred) state.refocused = true;
    else if (typeof event.payload !== 'boolean') { void complete(false, 'Invalid native focus payload'); return; }
    show();
  }));

  document.addEventListener('keydown', event => {
    if (shortcut(state, event, document.hasFocus())) { event.preventDefault(); show(); }
  });
  text.addEventListener('compositionstart', event => { composition(state, 'start', event.isTrusted); show(); });
  text.addEventListener('compositionend', event => { composition(state, 'end', event.isTrusted); show(); });
  text.addEventListener('input', event => { if (event.isTrusted) state.input++; show(); });
  for (const kind of ['paste', 'drop']) text.addEventListener(kind, event => event.preventDefault());
  text.addEventListener('beforeinput', event => {
    if (['insertFromPaste', 'insertFromDrop'].includes(event.inputType)) event.preventDefault();
  });
  document.querySelector('#reset').onclick = () => {
    text.value = '';
    state.starts = state.ends = state.input = 0;
    state.composing = false;
    text.focus(); show();
  };
  document.querySelector('#maximized').onclick = async () => {
    try {
      if (await win.isMaximized()) { state.maximized = true; notice.textContent = ''; }
      else notice.textContent = '아직 최대화 상태가 아닙니다. 실제 최대화 후 다시 확인하세요.';
      show();
    } catch (error) { await complete(false, error); }
  };
  document.querySelector('#restored').onclick = async () => {
    try {
      if (state.maximized && !await win.isMaximized()) { state.restored = true; notice.textContent = ''; }
      else notice.textContent = '최대화 상태 확인을 먼저 마친 뒤 창을 복원하세요.';
      show();
    } catch (error) { await complete(false, error); }
  };
  document.querySelector('#drag').addEventListener('mousedown', async event => {
    if (!event.isTrusted || event.button !== 0 || ending) return;
    try { await win.startDragging(); state.dragCalls++; show(); }
    catch (error) { await complete(false, error); }
  });
  for (const input of document.querySelectorAll('.manual')) input.addEventListener('change', show);
  await invoke('wayland_ready');
  if (ending) throw Error('Probe ended during setup');
  ready = true;
  document.querySelector('#abort').disabled = false;
  for (const id of ['reset', 'maximized', 'restored']) document.querySelector(`#${id}`).disabled = false;
  show();
} catch (error) { await complete(false, error); }
