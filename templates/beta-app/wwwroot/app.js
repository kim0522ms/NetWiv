import { invoke } from './vendor/@tauri-apps/api/core.js';

const output = document.querySelector('#result');
document.querySelector('#greeting').addEventListener('submit', async event => {
  event.preventDefault();
  try {
    const greeting = await invoke('greet', { name: document.querySelector('#name').value });
    output.textContent = `${greeting.message}\n호출 횟수: ${greeting.count}`;
  } catch (error) {
    output.textContent = String(error);
  }
});

if (new URLSearchParams(location.search).has('smoke')) {
  let result;
  try {
    const first = await invoke('greet', { name: 'Beta' });
    const second = await invoke('greet', { name: 'Beta' });
    if (first.message !== 'Hello, Beta!' || first.count !== 1 || second.count !== 2)
      throw new Error(`Unexpected greeting: ${JSON.stringify([first, second])}`);
    result = 'PASS: official JS → generated C# → JSON';
  } catch (error) { result = `FAIL: ${error}`; }
  output.textContent = result;
  await invoke('smoke_done', { result });
}
