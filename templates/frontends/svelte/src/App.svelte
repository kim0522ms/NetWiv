<script>
import { isTauri } from '@tauri-apps/api/core';
import { commands } from './generated/commands.js';
let name = $state('World');
let message = $state('');
async function greet(event) {
  event.preventDefault();
  try { const result = await commands.greet({ name }); message = result.message + ' (#' + result.count + ')'; }
  catch (error) { message = String(error); }
}


</script>
<main><h1>NetWiv + Svelte</h1><form onsubmit={greet}><input bind:value={name} aria-label="Name" required /><button disabled={!isTauri()}>Greet</button></form><output>{message}</output></main>
