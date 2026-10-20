<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { getName } from '@netwiv/api/app';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

const appName = ref('NetWiv app');
const bridge = ref(true);
const name = ref('Vue');
const state = ref<CallState>('idle');
const message = ref('No call yet');
const count = ref<number | null>(null);
const ms = ref(0);
const error = ref('');
const form = ref<HTMLFormElement>();

onMounted(() => {
  getName().then(value => (appName.value = value), () => (bridge.value = false));
  // Smoke tests open the window with ?smoke=1 and expect one automatic call.
  if (smoke) form.value?.requestSubmit();
});

async function greet() {
  state.value = 'calling';
  const started = performance.now();
  try {
    const result = await commands.greet({ name: name.value });
    ms.value = Math.round(performance.now() - started);
    message.value = result.message;
    count.value = result.count;
    state.value = 'answered';
    if (smoke) {
      if (result.message !== 'Hello, Vue!' || result.count !== 1)
        throw new Error(`Unexpected C# response: ${JSON.stringify(result)}`);
      await commands.smoke_done({ result: 'PASS: Vue form → generated C# → JSON' });
    }
  } catch (cause) {
    error.value = String(cause);
    state.value = 'failed';
    if (smoke) await commands.smoke_done({ result: `FAIL: ${cause}` });
  }
}

const statusText = computed(() =>
  state.value === 'calling' ? 'Calling C#'
    : state.value === 'answered' ? `Answered in ${ms.value} ms`
    : state.value === 'failed' ? 'No answer'
    : bridge.value ? 'Ready' : 'Not in a NetWiv window');
const statusState = computed(() => (state.value === 'idle' && !bridge.value ? 'offline' : state.value));
const answerState = computed(() => (state.value === 'calling' ? (count.value === null ? 'idle' : 'answered') : state.value));
</script>

<template>
  <div class="starter">
    <header class="bar">
      <span class="brand"><svg class="mark" viewBox="76 88 348 348" aria-hidden="true"><rect class="back" x="160" y="88" width="264" height="264" rx="48"/><rect class="gap" x="76" y="148" width="288" height="288" rx="60"/><rect class="front" x="88" y="160" width="264" height="264" rx="48"/><path class="letter" d="M152 360V224h40l56 100V224h40v136h-40l-56-100v100z"/></svg><span>{{ appName }}</span></span>
      <span class="status" :data-state="statusState" aria-live="polite">{{ statusText }}</span>
    </header>
    <main class="split">
      <section class="call" aria-labelledby="heading">
        <h1 id="heading">{{ bridge ? 'Your window is running. Call the C# backend.' : 'This page is outside the NetWiv window.' }}</h1>
        <form ref="form" class="ask" @submit.prevent="greet">
          <label for="name">Name</label>
          <div class="row">
            <input id="name" v-model="name" autocomplete="off" required />
            <button :disabled="state === 'calling'">{{ state === 'calling' ? 'Calling…' : 'Call greet' }}</button>
          </div>
        </form>
        <div class="answer" :data-state="answerState" aria-live="polite">
          <p class="route">commands.greet → Commands.Greet</p>
          <template v-if="state === 'failed'">
            <p class="message">C# did not answer</p>
            <p class="detail">{{ bridge ? 'The greet command failed in C#. Greet is in src-netwiv/Program.cs.' : 'Start the app with netwiv dev so this page runs inside the NetWiv window.' }}<code>{{ error }}</code></p>
          </template>
          <template v-else>
            <p :key="count ?? 0" :class="['message', { landed: count !== null }]">{{ state === 'calling' && count === null ? 'Waiting for C#…' : message }}</p>
            <dl class="count"><dt>Calls counted in C#</dt><dd :key="count ?? 0" :class="{ landed: count !== null }">{{ count ?? '—' }}</dd></dl>
          </template>
        </div>
      </section>
      <aside class="map" aria-labelledby="map-heading">
        <h2 id="map-heading">Make it yours</h2>
        <ul class="files">
          <li><code>src/App.vue</code><span>This screen. Replace it with your interface.</span></li>
          <li><code>src-netwiv/Program.cs</code><span>Commands, state and the window. Greet is defined here.</span></li>
          <li><code>src/generated/commands.js</code><span>Typed calls to your C# commands, generated on each build.</span></li>
          <li><strong>netwiv.github.io</strong><span>Guides and the API reference.</span></li>
        </ul>
      </aside>
    </main>
  </div>
</template>
