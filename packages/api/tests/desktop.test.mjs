import { afterEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { mockIPC, mockWindows } from '@tauri-apps/api/mocks';
import { Window as OfficialWindow } from '@tauri-apps/api/window';
import { WebviewWindow as OfficialWebviewWindow } from '@tauri-apps/api/webviewWindow';
import { TauriEvent as OfficialEvent } from '@tauri-apps/api/event';
import * as api from '../lib/index.js';

afterEach(() => { delete globalThis.window; });

function bridge() {
  const calls = [];
  globalThis.window = { crypto: webcrypto };
  mockIPC((command, args) => {
    calls.push({ command, args });
    if (command === 'plugin:window|get_all_windows') return ['main'];
    if (command === 'plugin:webview|get_all_webviews') return [{ label: 'main', windowLabel: 'main' }];
    if (command === 'plugin:window|is_focused') return true;
    if (command === 'plugin:window|title') return 'Desktop';
    if (command === 'plugin:menu|new') return [7, 'menu'];
    if (command === 'plugin:sample|check_permissions') return { files: 'prompt' };
    if (command === 'plugin:sample|request_permissions') return { files: 'granted' };
    if (command === 'greet') return `Hello ${args.name}`;
  }, { shouldMockEvents: true });
  mockWindows('main');
  return calls;
}

function desktop(handle) {
  assert.equal(handle.label, 'main');
  for (const method of ['activityName', 'sceneIdentifier']) {
    assert.equal(handle[method], undefined);
    assert.equal(method in handle, false);
  }
}

test('identity is SSR safe and specific to the NetWiv marker', () => {
  assert.equal(api.isNetWiv(), false);
  globalThis.window = { isTauri: true };
  assert.equal(api.isNetWiv(), false);
  window.isNetWiv = true;
  assert.equal(api.core.isNetWiv(), true);
});

test('window, webview and combined factories expose desktop handles', async () => {
  bridge();
  const handles = [
    api.window.getCurrentWindow(), api.window.Window.getCurrent(),
    ...await api.window.getAllWindows(), ...await api.window.Window.getAll(),
    await api.window.Window.getByLabel('main'), await api.window.Window.getFocusedWindow(),
    api.webview.getCurrentWebview().window, api.webview.Webview.getCurrent().window,
    ...(await api.webview.getAllWebviews()).map(value => value.window),
    ...(await api.webview.Webview.getAll()).map(value => value.window),
    (await api.webview.Webview.getByLabel('main')).window,
    api.webviewWindow.getCurrentWebviewWindow(), api.webviewWindow.WebviewWindow.getCurrent(),
    ...await api.webviewWindow.getAllWebviewWindows(), ...await api.webviewWindow.WebviewWindow.getAll(),
    await api.webviewWindow.WebviewWindow.getByLabel('main')
  ];
  for (const handle of handles) {
    desktop(handle);
    assert.equal(await handle.title(), 'Desktop');
  }
  assert.equal(await api.window.Window.getByLabel('missing'), null);
  assert.equal(await api.webview.Webview.getByLabel('missing'), null);
  assert.equal(await api.webviewWindow.WebviewWindow.getByLabel('missing'), null);
  const current = api.window.getCurrentWindow();
  assert.ok(current instanceof api.window.Window);
  assert.ok(current instanceof OfficialWindow);
  assert.equal(typeof new OfficialWindow('main', { skip: true }).activityName, 'function');
});

test('desktop constructors preserve commands, options, events and instance identity', async () => {
  const calls = bridge();
  const created = new api.window.Window('main', { title: 'Desktop', width: 640 });
  let events = 0;
  await created.once('tauri://created', () => { events++; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(events, 1);
  desktop(created);
  assert.ok(created instanceof api.window.Window);
  await created.setTitle('Changed');
  const child = new api.webview.Webview(created, 'child', {
    x: 1, y: 2, width: 320, height: 240, allowLinkPreview: false
  });
  assert.equal(child.window.label, 'main');
  assert.equal(child.window, created);
  await child.reparent(created);
  const combined = new api.webviewWindow.WebviewWindow('main', {
    title: 'Combined', dataStoreIdentifier: Array(16).fill(0)
  });
  assert.ok(combined instanceof api.webviewWindow.WebviewWindow);
  assert.ok(combined instanceof OfficialWebviewWindow);
  desktop(combined);
  await new Promise(resolve => setImmediate(resolve));
  assert.ok(calls.some(value => value.command === 'plugin:window|set_title' && value.args.value === 'Changed'));
  assert.ok(calls.some(value => value.command === 'plugin:webview|create_webview' && value.args.options.allowLinkPreview === false));
  assert.ok(calls.some(value => value.command === 'plugin:webview|create_webview_window' && value.args.options.title === 'Combined'));
});

test('mobile options fail synchronously before invoking the backend', () => {
  const calls = bridge();
  for (const name of ['activityName', 'createdByActivityName', 'requestedBySceneIdentifier', 'disableInputAccessoryView']) {
    assert.throws(() => new api.window.Window('main', { [name]: true }), TypeError);
    assert.throws(() => new api.webviewWindow.WebviewWindow('main', { [name]: true }), TypeError);
    assert.throws(() => new api.webview.Webview(api.window.getCurrentWindow(), 'child', {
      x: 0, y: 0, width: 100, height: 100, [name]: true
    }), TypeError);
  }
  assert.equal(calls.length, 0);
});

test('desktop delegates retain IPC, events and plugin permission semantics', async () => {
  bridge();
  assert.equal(await api.core.invoke('greet', { name: 'Desktop' }), 'Hello Desktop');
  assert.deepEqual(await api.core.checkPermissions('sample'), { files: 'prompt' });
  assert.deepEqual(await api.core.requestPermissions('sample'), { files: 'granted' });
  let received;
  const unlisten = await api.event.listen('sample-event', event => { received = event.payload; });
  await api.event.emit('sample-event', { ready: true });
  assert.deepEqual(received, { ready: true });
  await unlisten();
});

test('selected constants and exports omit mobile names without changing upstream', () => {
  assert.equal('onBackButtonPress' in api.app, false);
  assert.equal('getTauriVersion' in api.app, false);
  assert.equal('isTauri' in api.core, false);
  assert.equal('mocks' in api, false);
  assert.equal('WINDOW_SUSPENDED' in api.event.TauriEvent, false);
  assert.equal('WINDOW_RESUMED' in api.event.TauriEvent, false);
  assert.equal(OfficialEvent.WINDOW_SUSPENDED, 'tauri://suspended');
  assert.equal(api.event.TauriEvent.WINDOW_CREATED, 'tauri://window-created');
  assert.equal(api.path.BaseDirectory.AppConfig, 13);
  assert.deepEqual(Object.keys(api.path.BaseDirectory).filter(key => /android/i.test(key)), []);
});
