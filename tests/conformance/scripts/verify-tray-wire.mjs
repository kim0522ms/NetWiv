import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { TrayIcon } from '../dist/vendor/@tauri-apps/api/tray.js';
import { mockIPC, clearMocks } from '../dist/vendor/@tauri-apps/api/mocks.js';
import { PhysicalPosition, PhysicalSize } from '../dist/vendor/@tauri-apps/api/dpi.js';

const file = process.argv[2];
if (!file) throw Error('Pass the JSON artifact emitted by Rust tests with NETWIV_TRAY_WIRE_OUTPUT');
const values = JSON.parse(await readFile(file, 'utf8'));
assert.equal(values.length, 5);
globalThis.window = globalThis;
let channel;
mockIPC((command, args) => {
  assert.equal(command, 'plugin:tray|new');
  channel = args.handler;
  return [1, 'fractional'];
});
const received = [];
try {
  await TrayIcon.new({action: event => received.push(event)});
  // Test plumbing feeds the native adapter's actual serialized messages into
  // the official Channel. No OS tray input is simulated by this assertion.
  for (const [index, message] of values.entries())
    window.__TAURI_INTERNALS__.runCallback(channel.id, {message, index});
  assert.equal(received.length, 5);
  for (const [index, event] of received.entries()) {
    assert.equal(event.type, ['Click', 'DoubleClick', 'Enter', 'Move', 'Leave'][index]);
    assert.equal(event.id, 'fractional');
    assert.ok(event.position instanceof PhysicalPosition);
    assert.ok(event.rect.position instanceof PhysicalPosition);
    assert.ok(event.rect.size instanceof PhysicalSize);
    assert.deepEqual([event.position.x, event.position.y], [12.5, -34.25]);
    assert.deepEqual([event.rect.position.x, event.rect.position.y], [11, -31]);
    assert.deepEqual([event.rect.size.width, event.rect.size.height], [16, 24]);
    assert.equal(event.button, index === 0 ? 'Right' : index === 1 ? 'Middle' : undefined);
    assert.equal(event.buttonState, index === 0 ? 'Up' : undefined);
  }
  console.log('PASS 5 native tray payloads through unchanged official TrayIcon and Channel');
} finally { clearMocks(); }
