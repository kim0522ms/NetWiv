import { createSignal } from 'solid-js';
import { isTauri } from '@tauri-apps/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = createSignal('World');
  const [message, setMessage] = createSignal('');
  async function greet(event: Event) {
    event.preventDefault();
    try { const result = await commands.greet({ name: name() }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + Solid</h1><form onSubmit={greet}><input aria-label="Name" value={name()} onInput={event => setName(event.currentTarget.value)} required /><button disabled={!isTauri()}>Greet</button></form><output>{message()}</output></main>;
}
