import { afterEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { mockIPC } from '@tauri-apps/api/mocks';
import { readFile } from 'node:fs/promises';
import { Channel, Resource } from '../lib/core.js';
import * as notification from '../lib/plugins/notification.js';
import * as dialog from '../lib/plugins/dialog.js';
import { openUrl } from '../lib/plugins/opener.js';
import * as fs from '../lib/plugins/fs.js';
import { load } from '../lib/plugins/store.js';
import { register } from '../lib/plugins/global-shortcut.js';
import Database from '../lib/plugins/sql.js';
import WebSocket from '../lib/plugins/websocket.js';

afterEach(() => { delete globalThis.window; });

test('all declared plugin exports import without upstream packages in their module graph', async () => {
  const manifest = JSON.parse(await readFile(new URL('../package.json', import.meta.url)));
  for (const name of Object.keys(manifest.exports).filter(name => name.startsWith('./plugins/'))) {
    const module = await import(`../lib/${name.slice(2)}.js`);
    assert.ok(Object.keys(module).length > 0, name);
    const text = await readFile(new URL(`../lib/upstream/${name.slice(2)}.js`, import.meta.url), 'utf8');
    assert.doesNotMatch(text.replace(/\/\*[\s\S]*?\*\//g, ''), /(?:from\s*|import\s*)['"]@tauri-apps\//);
  }
  assert.equal(typeof Database.load, 'function');
  assert.equal(typeof WebSocket.connect, 'function');
});

test('plugin resources and callbacks share core identity and preserve invoke arguments', async () => {
  globalThis.window = { crypto: webcrypto };
  const calls = [];
  mockIPC((command, args) => {
    calls.push({ command, args });
    if (command === 'plugin:store|load') return 17;
    if (command === 'plugin:store|get') return ['value', true];
  });
  await openUrl('https://example.com', 'firefox');
  assert.deepEqual(calls.at(-1), { command: 'plugin:opener|open_url', args: { url: 'https://example.com', with: 'firefox' } });
  const store = await load('settings.json', { autoSave: false });
  assert.ok(store instanceof Resource);
  await store.set('key', 'value');
  assert.equal(await store.get('key'), 'value');
  await store.close();
  assert.deepEqual(calls.find(call => call.command === 'plugin:store|set').args,
    { rid: 17, key: 'key', value: 'value' });
  assert.deepEqual(calls.at(-1), { command: 'plugin:resources|close', args: { rid: 17 } });
  let received;
  await register('Control+Shift+K', event => { received = event; });
  const args = calls.at(-1).args;
  assert.ok(args.handler instanceof Channel);
  assert.deepEqual(args.shortcuts, ['Control+Shift+K']);
  args.handler.onmessage({ state: 'Pressed' });
  assert.deepEqual(received, { state: 'Pressed' });
});

test('mobile plugin APIs are absent and mobile options fail before dispatch', async () => {
  for (const name of ['Schedule', 'createChannel', 'onAction', 'registerActionTypes']) {
    assert.equal(name in notification, false);
  }
  for (const name of ['startAccessingSecurityScopedResource', 'stopAccessingSecurityScopedResource']) {
    assert.equal(name in fs, false);
  }
  assert.throws(() => notification.sendNotification({ title: 'Example', channelId: 'mobile' }), TypeError);
  await assert.rejects(openUrl('https://example.com', 'inAppBrowser'), TypeError);
  await assert.rejects(dialog.open({ pickerMode: 'media' }), TypeError);
  await assert.rejects(dialog.open({ fileAccessMode: 'copy' }), TypeError);
  const notifications = [];
  globalThis.window = { Notification: class { constructor(title, options) { notifications.push({ title, options }); } } };
  notification.sendNotification({ title: 'Example', body: 'Desktop' });
  assert.equal(notifications[0].title, 'Example');
  assert.equal(notifications[0].options.body, 'Desktop');
});
