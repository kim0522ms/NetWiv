<script lang="ts">
import { onMount } from 'svelte';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

const appName = 'NetWiv app';
let name = $state('Svelte');
let callState = $state<CallState>('idle');
let message = $state('No call yet');
let count = $state<number | null>(null);
let ms = $state(0);
let error = $state('');
let form = $state<HTMLFormElement>();

onMount(() => {
  // Smoke tests open the window with ?smoke=1 and expect one automatic call.
  if (smoke) form?.requestSubmit();
});

async function greet(event: SubmitEvent) {
  event.preventDefault();
  callState = 'calling';
  const started = performance.now();
  try {
    const result = await commands.greet({ name });
    ms = Math.round(performance.now() - started);
    message = result.message;
    count = result.count;
    callState = 'answered';
    if (smoke) {
      if (result.message !== 'Hello, Svelte!' || result.count !== 1)
        throw new Error(`Unexpected C# response: ${JSON.stringify(result)}`);
      await commands.smoke_done({ result: 'PASS: Svelte form → generated C# → JSON' });
    }
  } catch (cause) {
    error = String(cause);
    callState = 'failed';
    if (smoke) await commands.smoke_done({ result: `FAIL: ${cause}` });
  }
}

const statusText = $derived(callState === 'calling' ? 'Calling C#'
  : callState === 'answered' ? `Answered in ${ms} ms`
  : callState === 'failed' ? 'No answer'
  : 'No calls yet');
const answerState = $derived(callState === 'calling' ? (count === null ? 'idle' : 'answered') : callState);
</script>

<div class="starter">
  <header class="bar">
    <span class="brand"><svg class="mark" viewBox="76 88 348 348" aria-hidden="true"><rect class="back" x="160" y="88" width="264" height="264" rx="48"/><rect class="gap" x="76" y="148" width="288" height="288" rx="60"/><rect class="front" x="88" y="160" width="264" height="264" rx="48"/><path class="letter" d="M152 360V224h40l56 100V224h40v136h-40l-56-100v100z"/></svg><span>{appName}</span></span>
    <span class="status" data-state={callState} aria-live="polite">{statusText}</span>
  </header>
  <main class="split">
    <section class="call" aria-labelledby="heading">
      <h1 id="heading">Your frontend is ready. Call the C# backend.</h1>
      <form class="ask" bind:this={form} onsubmit={greet}>
        <label for="name">Name</label>
        <div class="row">
          <input id="name" bind:value={name} autocomplete="off" required />
          <button disabled={callState === 'calling'}>{callState === 'calling' ? 'Calling…' : 'Call greet'}</button>
        </div>
      </form>
      <div class="answer" data-state={answerState} aria-live="polite">
        <p class="route">commands.greet → Commands.Greet</p>
        {#if callState === 'failed'}
          <p class="message">C# did not answer</p>
          <p class="detail">In a browser tab, start the app with netwiv dev instead. In the window, the error below comes from Greet in src-netwiv/Program.cs.<code>{error}</code></p>
        {:else}
          {#key count}
            <p class="message" class:landed={count !== null}>{callState === 'calling' && count === null ? 'Waiting for C#…' : message}</p>
            <dl class="count"><dt>Calls counted in C#</dt><dd class:landed={count !== null}>{count ?? '—'}</dd></dl>
          {/key}
        {/if}
      </div>
    </section>
    <aside class="map" aria-labelledby="map-heading">
      <h2 id="map-heading">Make it yours</h2>
      <ul class="files">
        <li><code>src/App.svelte</code><span>This screen. Replace it with your interface.</span></li>
        <li><code>src-netwiv/Program.cs</code><span>Commands, state and the window. Greet is defined here.</span></li>
        <li><code>src/generated/commands.js</code><span>Typed calls to your C# commands, generated on each build.</span></li>
        <li><strong>netwiv.github.io</strong><span>Guides and the API reference.</span></li>
      </ul>
    </aside>
  </main>
</div>
