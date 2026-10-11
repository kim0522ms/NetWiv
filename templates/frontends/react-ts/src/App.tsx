import { useState, type FormEvent } from 'react';
import { isTauri } from '@tauri-apps/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = useState('World');
  const [message, setMessage] = useState('');
  async function greet(event: FormEvent) {
    event.preventDefault();
    try { const result = await commands.greet({ name }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + React</h1><form onSubmit={greet}><input aria-label="Name" value={name} onChange={event => setName(event.target.value)} required /><button disabled={!isTauri()}>Greet</button></form><output>{message}</output></main>;
}
