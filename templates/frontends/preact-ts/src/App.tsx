import { useState } from 'preact/hooks';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = useState('World');
  const [message, setMessage] = useState('');
  async function greet(event: Event) {
    event.preventDefault();
    try { const result = await commands.greet({ name }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + Preact</h1><form onSubmit={greet}><input aria-label="Name" value={name} onInput={event => setName(event.currentTarget.value)} required /><button disabled={!isNetWiv()}>Greet</button></form><output>{message}</output></main>;
}
