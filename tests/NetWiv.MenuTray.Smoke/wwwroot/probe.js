import { invoke } from './vendor/@tauri-apps/api/core.js';
import { Menu, MenuItem, Submenu, PredefinedMenuItem } from './vendor/@tauri-apps/api/menu.js';
import { TrayIcon } from './vendor/@tauri-apps/api/tray.js';
import { Image } from './vendor/@tauri-apps/api/image.js';
import { PhysicalPosition, PhysicalSize } from './vendor/@tauri-apps/api/dpi.js';
import { getAllWindows } from './vendor/@tauri-apps/api/window.js';

const status = document.querySelector('#status');
const owned = [];
const counts = { help: 0, leftDown: 0, leftUp: 0, rightDown: 0, rightMenu: 0, leftMenu: 0, replacement: 0 };
let tray, previousMenu, leftMenu = false, lastButton, iconStage = 0;
const track = value => { owned.push(value); return value; };
const require = (condition, message) => { if (!condition) throw Error(message); };
const show = () => { status.textContent = `Help action: ${counts.help}\nLeft Down/Up: ${counts.leftDown}/${counts.leftUp}\nRight Down: ${counts.rightDown}\nRight/left menu selections: ${counts.rightMenu}/${counts.leftMenu}\nReplacement selections: ${counts.replacement}`; };

async function fail(error) {
  status.textContent = `FAIL: ${String(error)}`;
  try { await cleanup(); }
  finally { await invoke('menu_tray_finish', { error: String(error) }); }
}
async function cleanup() {
  if (previousMenu) { await previousMenu.setAsAppMenu(); await previousMenu.close(); previousMenu = null; }
  for (const resource of owned.reverse()) await resource.close();
  owned.length = 0;
}
function button(id, action) {
  const element = document.querySelector(`#${id}`);
  element.disabled = false;
  element.onclick = async () => {
    try { await action(); element.disabled = true; show(); }
    catch (error) { await fail(error); }
  };
}
function pixels(square) {
  const rgba = new Uint8Array(22 * 22 * 4);
  for (let y = 0; y < 22; y++) for (let x = 0; x < 22; x++) {
    const radius = Math.hypot(x - 10.5, y - 10.5);
    if (square ? x >= 5 && x < 17 && y >= 5 && y < 17 : radius >= 5 && radius <= 9) rgba.set([230, 30, 30, 255], (y * 22 + x) * 4);
  }
  return rgba;
}

try {
  const ring = track(await Image.new(pixels(false), 22, 22));
  const square = track(await Image.new(pixels(true), 22, 22));
  const about = track(await PredefinedMenuItem.new({ text: 'About menu probe', item: { About: { name: 'NetWiv menu probe', shortVersion: '1.2', version: '1.2.3', credits: 'NETWIV CREDITS CHECK' } } }));
  const helpAction = track(await MenuItem.new({ id: 'menu-probe-action', text: 'Probe action', action: () => { counts.help++; show(); } }));
  const application = track(await Submenu.new({ text: 'Application', items: [about, helpAction] }));
  // Custom titles prevent AppKit's localized "Help" title heuristic from masking a missing role.
  // Search a command in another menu instead of relying on Help's own items being indexed.
  const help = track(await Submenu.new({ text: 'Assistance', items: [] }));
  const minimize = track(await PredefinedMenuItem.new({ item: 'Minimize' }));
  const windows = track(await Submenu.new({ text: 'Scenes', items: [minimize] }));
  const menu = track(await Menu.new({ items: [application, windows, help] }));
  previousMenu = await menu.setAsAppMenu();
  // muda resolves roles only after attachment to the current NSApp menu bar.
  await help.setAsHelpMenuForNSApp();
  await windows.setAsWindowsMenuForNSApp();
  // AppKit registers windows when their titles change. These test windows
  // already existed before the role was assigned, so exercise that public path.
  for (const window of await getAllWindows()) {
    if (!['main', 'secondary'].includes(window.label)) continue;
    const title = await window.title();
    await window.setTitle(`${title} — menu registration`);
    await window.setTitle(title);
  }
  const trayAction = track(await MenuItem.new({ id: 'tray-probe-action', text: 'Tray action', action: () => {
    if (lastButton === 'Right') counts.rightMenu++;
    else if (lastButton === 'Left' && leftMenu) counts.leftMenu++;
    else { void fail(Error('Tray action arrived without the expected native click')); return; }
    show();
  } }));
  const trayMenu = track(await Menu.new({ items: [trayAction] }));
  tray = track(await TrayIcon.new({ id: 'netwiv-menu-tray-probe', title: 'NW', tooltip: 'NetWiv menu/tray check', icon: ring, iconAsTemplate: true, showMenuOnLeftClick: false, menu: trayMenu, action: event => {
    try {
      require(event.id === 'netwiv-menu-tray-probe', 'Tray event has wrong identity');
      require(event.position instanceof PhysicalPosition && event.rect.position instanceof PhysicalPosition && event.rect.size instanceof PhysicalSize, 'Official guest did not map native tray geometry');
      if (event.type === 'Click') {
        lastButton = event.button;
        if (event.button === 'Left') {
          if (event.buttonState === 'Down') counts.leftDown++;
          if (event.buttonState === 'Up') counts.leftUp++;
        } else if (event.button === 'Right' && event.buttonState === 'Down') counts.rightDown++;
        show();
      }
    } catch (error) { void fail(error); }
  } }));
  button('color', async () => {
    require(document.querySelector('#initial').checked, 'Inspect the initial template icon first');
    await tray.setIcon(ring);
    iconStage = 1;
    button('template', async () => {
      require(document.querySelector('#colored').checked, 'Inspect the color replacement first');
      await tray.setIconAsTemplate(true);
      iconStage = 2;
      button('replace', async () => {
        require(document.querySelector('#templated').checked, 'Inspect template-flag mutation first');
        await tray.setIconWithAsTemplate(square, true);
        iconStage = 3;
      });
    });
  });
  button('left', async () => {
    require(counts.leftDown > 0 && counts.leftUp > 0 && counts.rightDown > 0 && counts.rightMenu > 0 && document.querySelector('#left-off').checked, 'Exercise left click without a menu and select the right-click menu first');
    await tray.setShowMenuOnLeftClick(true);
    leftMenu = true;
    button('swap', async () => {
      require(counts.leftMenu > 0, 'Select Tray action using a left click first');
      const replacement = track(await MenuItem.new({ id: 'tray-probe-replacement', text: 'Replacement action', action: () => { counts.replacement++; show(); } }));
      const replacementMenu = track(await Menu.new({ items: [replacement] }));
      await tray.setMenu(replacementMenu);
    });
  });
  button('finish', async () => {
    require(['help', 'windows', 'about', 'initial', 'colored', 'templated', 'replaced', 'left-off', 'menu-replaced'].every(id => document.querySelector(`#${id}`).checked), 'Confirm every native presentation check');
    require(counts.help > 0 && counts.leftMenu > 0 && counts.replacement > 0, 'Exercise native help and tray menu selections');
    require(iconStage === 3, 'Complete all three icon mutations');
    await cleanup();
    require(await TrayIcon.getById('netwiv-menu-tray-probe') === null, 'Tray remained registered after close');
    await invoke('menu_tray_finish');
  });
  show();
  await invoke('menu_tray_ready');
} catch (error) { await fail(error); }
