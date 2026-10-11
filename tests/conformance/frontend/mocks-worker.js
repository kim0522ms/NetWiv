globalThis.window = globalThis;
import { mockIPC, mockWindows, mockConvertFileSrc, clearMocks } from './vendor/@tauri-apps/api/mocks.js';
import { invoke, convertFileSrc } from './vendor/@tauri-apps/api/core.js';
import { getCurrentWindow } from './vendor/@tauri-apps/api/window.js';
import { emit, listen } from './vendor/@tauri-apps/api/event.js';
try {
  mockIPC((command, args) => { if(command === 'add') return args.a + args.b; throw 'mock error'; }, {shouldMockEvents:true});
  mockWindows('mock-main');
  const value = {sum:await invoke('add', {a:12,b:15}), label:getCurrentWindow().label};
  try { await invoke('reject'); throw Error('mock rejection resolved'); } catch(error) { value.error=String(error); }
  const received = [];
  const unlisten = await listen('fixture', event => received.push(event.payload));
  await emit('fixture', 1); await unlisten(); await emit('fixture', 2);
  value.events = received;
  mockConvertFileSrc('windows'); value.windowsPath = convertFileSrc('C:\\space name.txt');
  mockConvertFileSrc('linux'); value.linuxPath = convertFileSrc('/tmp/space name.txt');
  clearMocks();
  if(value.sum !== 27 || value.label !== 'mock-main' || value.error !== 'mock error' || JSON.stringify(received) !== '[1]')
    throw Error('Official mocks produced unexpected behavior');
  postMessage({value});
} catch(error) { postMessage({error:String(error)}); }


