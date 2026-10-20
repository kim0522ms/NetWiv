// Maintainer task: regenerate the checked-in starter frontends after changing this file.
// One starter screen is written five times: React (the base template in
// templates/react-app), Vue, Svelte and Angular (TypeScript overlays in
// templates/frontends) and Blazor. They share one stylesheet and the same
// markup, states and smoke-test behavior; templates/DESIGN.md records the design.
// Run `npm install` in each JavaScript template afterwards to refresh its lockfile.
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('templates/frontends');
const reactApp = path.resolve('templates/react-app');
const api = { '@netwiv/api': 'file:vendor/netwiv-api' };
const vite = { vite: '8.2.2' };
const typescript = { typescript: '6.0.3' };

// Ship the unpublished API with every generated project. This path is relative
// to that project, so copied templates do not depend on a maintainer checkout.
// Build packages/api before regenerating these package snapshots.
const apiPackage = path.resolve('packages/api');
const apiManifest = JSON.parse(await readFile(path.join(apiPackage, 'package.json'), 'utf8'));
const apiSnapshot = path.join(reactApp, 'vendor/netwiv-api');
await rm(apiSnapshot, { recursive: true, force: true });
await mkdir(apiSnapshot, { recursive: true });
for (const file of apiManifest.files) {
  await cp(path.join(apiPackage, file), path.join(apiSnapshot, file), { recursive: true });
}
// Local directory dependencies also install devDependencies. This snapshot is
// already built, so consumers need only its runtime dependencies and exports.
const { scripts: apiScripts, devDependencies: apiDevDependencies, overrides: apiOverrides, ...runtimeManifest } = apiManifest;
await writeFile(path.join(apiSnapshot, 'package.json'), JSON.stringify(runtimeManifest, null, 2) + '\n');
const hostingSnapshot = path.resolve('samples/NetWiv.HostingApp/vendor/netwiv-api');
await rm(hostingSnapshot, { recursive: true, force: true });
await cp(apiSnapshot, hostingSnapshot, { recursive: true });
// Blazor loads the API as standalone browser modules from wwwroot.
const standalone = path.join(root, 'shared/vendor/@netwiv/api');
await rm(standalone, { recursive: true, force: true });
await cp(path.join(apiPackage, 'lib'), standalone, {
  recursive: true, filter: source => !source.endsWith('.d.ts')
});
for (const file of apiManifest.files.filter(file => /^(LICENSE|NOTICE|THIRD-PARTY)/.test(file))) {
  await cp(path.join(apiPackage, file), path.join(standalone, file));
}

// ---------------------------------------------------------------------------
// The shared starter screen.

const style = `/* NetWiv starter. Replace this file with your own styles. */
:root {
  color-scheme: light dark;
  --paper: #f6f5fb;
  --ink: #17142a;
  --muted: #5b5674;
  --hair: #d9d5ea;
  --rule: #17142a;
  --field: #512bd4;
  --field-ink: #ffffff;
  --field-muted: #ddd5fc;
  --field-hair: rgb(255 255 255 / 0.3);
  --stop: #17142a;
  --stop-ink: #ffffff;
  --stop-muted: #c9c4dc;
  --button: #17142a;
  --button-ink: #ffffff;
  --button-hover: #512bd4;
  --button-off: #5b5674;
  --button-off-ink: #ffffff;
  --focus: #512bd4;
  --signal: #512bd4;
  --mark-back: #b9a8f5;
  --mark-front: #512bd4;
  --mark-letter: #ffffff;
  --sans: system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", "Helvetica Neue", Arial, sans-serif;
  --mono: ui-monospace, "SF Mono", SFMono-Regular, "Cascadia Mono", Consolas, "Liberation Mono", Menlo, monospace;
  --ease: cubic-bezier(0.16, 1, 0.3, 1);
  font: 16px/1.5 var(--sans);
  color: var(--ink);
  background: var(--paper);
  font-synthesis: none;
  -webkit-font-smoothing: antialiased;
}

@media (prefers-color-scheme: dark) {
  :root {
    --paper: #17142a;
    --ink: #dcd9ea;
    --muted: #9a94b8;
    --hair: #3a3456;
    --rule: #7d77a0;
    --field: #3b2e96;
    --field-ink: #ffffff;
    --field-muted: #d4ccf8;
    --field-hair: rgb(255 255 255 / 0.25);
    --stop: #2f2a4a;
    --stop-ink: #ece9f8;
    --stop-muted: #aaa4c6;
    --button: #dcd9ea;
    --button-ink: #17142a;
    --button-hover: #b9a8f5;
    --button-off: #2f2a4a;
    --button-off-ink: #c9c4dc;
    --focus: #b9a8f5;
    --signal: #b9a8f5;
    --mark-back: #512bd4;
    --mark-front: #ffffff;
    --mark-letter: #512bd4;
  }
}

* { box-sizing: border-box; }
html, body { margin: 0; min-height: 100%; }
::selection { background: var(--field); color: var(--field-ink); }

.starter { min-height: 100vh; display: grid; grid-template-rows: auto 1fr; }

.bar {
  display: flex; align-items: center; justify-content: space-between; gap: 16px;
  padding: 14px clamp(20px, 4vw, 40px);
  border-bottom: 2px solid var(--rule);
}
.brand { display: flex; align-items: center; gap: 10px; min-width: 0; font-weight: 600; }
.brand span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mark { width: 24px; height: 24px; flex: none; }
.mark .back { fill: var(--mark-back); }
.mark .gap { fill: var(--paper); }
.mark .front { fill: var(--mark-front); }
.mark .letter { fill: var(--mark-letter); }
.status { display: flex; align-items: center; gap: 8px; font: 13px/1.4 var(--mono); font-variant-numeric: tabular-nums; color: var(--muted); white-space: nowrap; }
.status::before { content: ""; width: 8px; height: 8px; background: var(--hair); }
.status[data-state="calling"]::before { background: var(--mark-back); }
.status[data-state="answered"]::before { background: var(--signal); }
.status[data-state="failed"]::before { background: var(--muted); }

.split { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); }
.call, .map { padding: clamp(28px, 6vh, 48px) clamp(20px, 4vw, 40px); }
.call { display: flex; flex-direction: column; gap: 28px; border-right: 1px solid var(--hair); }

h1 {
  margin: 0; max-width: 17ch;
  font-size: clamp(26px, 3.6vw, 34px); line-height: 1.12; letter-spacing: -0.025em; font-weight: 700;
  text-wrap: balance;
}
.ask label { display: block; margin-bottom: 6px; font-size: 14px; font-weight: 600; }
.row { display: flex; gap: 12px; }
.ask input {
  flex: 1; min-width: 0; padding: 10px 0;
  font: inherit; font-size: 17px; color: var(--ink); caret-color: var(--focus);
  background: transparent; border: 0; border-bottom: 2px solid var(--rule); border-radius: 0;
}
.ask input:focus { outline: none; border-bottom-color: var(--focus); box-shadow: 0 1px 0 var(--focus); }
.ask button {
  min-height: 46px; min-width: 9.5em; padding: 0 22px;
  font: inherit; font-weight: 600; color: var(--button-ink);
  background: var(--button); border: 0; border-radius: 0; cursor: pointer;
  transition: background-color 160ms var(--ease);
}
.ask button:hover { background: var(--button-hover); }
.ask button:focus-visible { outline: 2px solid var(--focus); outline-offset: 3px; }
.ask button:disabled { cursor: progress; color: var(--button-off-ink); background: var(--button-off); }

.answer { margin-top: auto; padding: 20px 24px 18px; background: var(--field); color: var(--field-ink); }
.answer[data-state="failed"] { background: var(--stop); color: var(--stop-ink); }
.route { margin: 0; font: 13px/1.4 var(--mono); color: var(--field-muted); overflow-wrap: anywhere; }
.answer[data-state="failed"] .route { color: var(--stop-muted); }
.message {
  margin: 10px 0 16px; min-height: 1.15em;
  font-size: clamp(24px, 3.2vw, 30px); line-height: 1.15; letter-spacing: -0.02em; font-weight: 700;
  overflow-wrap: anywhere;
}
.answer[data-state="idle"] .message { color: var(--field-muted); font-weight: 500; }
.landed { animation: land 360ms var(--ease); }
/* Only movement: if the WebView pauses animations, the text still reads. */
@keyframes land { from { transform: translateY(6px); } }
.count {
  display: flex; justify-content: space-between; align-items: baseline; gap: 16px;
  margin: 0; padding-top: 12px; border-top: 1px solid var(--field-hair); font-size: 14px;
}
.count dt { color: var(--field-muted); }
.count dd { margin: 0; font: 600 20px/1 var(--mono); font-variant-numeric: tabular-nums; }
.detail { margin: 0; font-size: 14px; color: var(--stop-muted); }
.detail code { display: block; margin-top: 10px; font: 12px/1.5 var(--mono); overflow-wrap: anywhere; }

.map h2 { margin: 0 0 14px; font-size: 15px; font-weight: 700; }
.files { margin: 0; padding: 0; list-style: none; border-top: 2px solid var(--rule); }
.files li { display: grid; gap: 4px; padding: 14px 0; border-bottom: 1px solid var(--hair); }
.files code { font: 600 14px/1.4 var(--mono); overflow-wrap: anywhere; }
.files strong { font-size: 14px; }
.files span { font-size: 14px; color: var(--muted); }

@media (max-width: 720px) {
  .split { grid-template-columns: minmax(0, 1fr); align-content: start; }
  .call { border-right: 0; border-bottom: 1px solid var(--hair); }
  .answer { margin-top: 0; }
}
@media (prefers-reduced-motion: reduce) {
  .landed { animation: none; }
  .ask button { transition: none; }
}
`;

// The NetWiv mark from assets/brand, coloured from the theme tokens above.
const mark = `<svg class="mark" viewBox="76 88 348 348" aria-hidden="true"><rect class="back" x="160" y="88" width="264" height="264" rx="48"/><rect class="gap" x="76" y="148" width="288" height="288" rx="60"/><rect class="front" x="88" y="160" width="264" height="264" rx="48"/><path class="letter" d="M152 360V224h40l56 100V224h40v136h-40l-56-100v100z"/></svg>`;

const copy = {
  app: 'NetWiv app',
  heading: 'Your frontend is ready. Call the C# backend.',
  label: 'Name',
  button: 'Call greet',
  calling: 'Calling…',
  idle: 'No call yet',
  pending: 'Waiting for C#…',
  count: 'Calls counted in C#',
  failed: 'C# did not answer',
  hint: 'In a browser tab, start the app with netwiv dev instead. In the window, the error below comes from Greet in src-netwiv/Program.cs.',
  mapHeading: 'Make it yours',
};
const status = {
  ready: 'No calls yet',
  calling: 'Calling C#',
  answered: 'Answered in {ms} ms',
  failed: 'No answer',
};
const screen = 'This screen. Replace it with your interface.';
const backend = ['src-netwiv/Program.cs', 'Commands, state and the window. Greet is defined here.'];
const bindings = ['src/generated/commands.js', 'Typed calls to your C# commands, generated on each build.'];
const docs = ['netwiv.github.io', 'Guides and the API reference.'];
const files = {
  react: [['src/App.tsx', screen], backend, bindings, docs],
  vue: [['src/App.vue', screen], backend, bindings, docs],
  svelte: [['src/App.svelte', screen], backend, bindings, docs],
  angular: [['src/app.ts', screen], backend, bindings, docs],
  blazor: [['frontend/Pages/Home.razor', screen], backend, ['contracts/Contracts.cs', 'Types this screen and the app share. Calls to C# are typed from them.'], docs],
};
const route = 'commands.greet → Commands.Greet';
const pass = framework => `PASS: ${framework} form → generated C# → JSON`;
const answered = expression => status.answered.replace('{ms}', expression);

// `netwiv new` replaces the app name literal with the project name (see NewCommand.cs).
// The file map is static markup, identical in every framework.
const fileMap = (framework, indent) => files[framework].map(([name, text], i, all) =>
  `${indent}<li>${i === all.length - 1 ? `<strong>${name}</strong>` : `<code>${name}</code>`}<span>${text}</span></li>`).join('\n');

async function save(base, files) {
  for (const [name, contents] of Object.entries(files)) {
    const destination = path.join(base, name);
    await mkdir(path.dirname(destination), { recursive: true });
    await writeFile(destination, contents.endsWith('\n') ? contents : contents + '\n');
  }
}

function manifest(framework, typecheck, dependencies, devDependencies) {
  return JSON.stringify({
    name: `netwiv-${framework}-example`, version: '0.1.0', private: true,
    type: 'module', engines: { node: '^20.19.0 || >=22.12.0' },
    scripts: {
      bindings: 'node scripts/generate-bindings.mjs',
      predev: 'node scripts/generate-bindings.mjs',
      dev: 'vite --host 127.0.0.1 --port 5173 --strictPort',
      build: `node scripts/generate-bindings.mjs && ${typecheck} && vite build`,
      'desktop:dev': 'dotnet tool restore && dotnet tool run netwiv -- dev -- --dev',
      'desktop:build': 'dotnet tool restore && dotnet tool run netwiv -- build --no-bundle',
    },
    dependencies: { ...api, ...dependencies },
    devDependencies: { ...vite, ...typescript, ...devDependencies },
  }, null, 2) + '\n';
}

function viteConfig(importLine, plugin) {
  return `import { defineConfig } from 'vite';
${importLine}

export default defineConfig({
  plugins: [${plugin}],
  base: './',
  server: { host: '127.0.0.1', port: 5173, strictPort: true },
  build: { target: 'es2020' },
});
`;
}

function tsconfig(extra = {}) {
  return JSON.stringify({ compilerOptions: {
    target: 'ES2020', module: 'ESNext', moduleResolution: 'Bundler', lib: ['ES2020', 'DOM', 'DOM.Iterable'], types: ['vite/client'],
    strict: true, isolatedModules: true, noEmit: true, skipLibCheck: true, ...extra,
  }, include: ['src'] }, null, 2) + '\n';
}

const html = (title, entry) => `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${title}</title>
  </head>
  <body>
    <div id="app"></div>
    <script type="module" src="${entry}"></script>
  </body>
</html>
`;

// ---------------------------------------------------------------------------
// React: the base template that `netwiv new` and `dotnet new netwiv-react` copy.

const reactPackage = JSON.parse(await readFile(path.join(reactApp, 'package.json'), 'utf8'));
reactPackage.scripts.build = 'npm run bindings && tsc --noEmit && vite build';
reactPackage.dependencies = { ...api, react: '19.2.8', 'react-dom': '19.2.8' };
reactPackage.devDependencies = { '@types/react': '19.2.7', '@types/react-dom': '19.2.3', '@vitejs/plugin-react': '6.1.1', ...typescript, ...vite };
await rm(path.join(reactApp, 'src/App.jsx'), { force: true });
await rm(path.join(reactApp, 'src/main.jsx'), { force: true });
await save(reactApp, {
  'package.json': JSON.stringify(reactPackage, null, 2),
  'vite.config.js': viteConfig("import react from '@vitejs/plugin-react';", 'react()'),
  'tsconfig.json': tsconfig({ jsx: 'react-jsx' }),
  'index.html': html('NetWiv + React', '/src/main.tsx'),
  'src/style.css': style,
  'src/main.tsx': `import { createRoot } from 'react-dom/client';
import App from './App';
import './style.css';

createRoot(document.getElementById('app')!).render(<App />);`,
  'src/App.tsx': `import { useEffect, useRef, useState, type FormEvent } from 'react';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');
const appName = '${copy.app}';

export default function App() {
  const [name, setName] = useState('React');
  const [state, setState] = useState<CallState>('idle');
  const [message, setMessage] = useState('${copy.idle}');
  const [count, setCount] = useState<number | null>(null);
  const [ms, setMs] = useState(0);
  const [error, setError] = useState('');
  const form = useRef<HTMLFormElement>(null);

  useEffect(() => {
    // Smoke tests open the window with ?smoke=1 and expect one automatic call.
    if (smoke) form.current?.requestSubmit();
  }, []);

  async function greet(event: FormEvent) {
    event.preventDefault();
    setState('calling');
    const started = performance.now();
    try {
      const result = await commands.greet({ name });
      setMs(Math.round(performance.now() - started));
      setMessage(result.message);
      setCount(result.count);
      setState('answered');
      if (smoke) {
        if (result.message !== 'Hello, React!' || result.count !== 1)
          throw new Error(\`Unexpected C# response: \${JSON.stringify(result)}\`);
        await commands.smoke_done({ result: '${pass('React')}' });
      }
    } catch (cause) {
      setError(String(cause));
      setState('failed');
      if (smoke) await commands.smoke_done({ result: \`FAIL: \${cause}\` });
    }
  }

  const statusText = state === 'calling' ? '${status.calling}'
    : state === 'answered' ? \`${answered('${ms}')}\`
    : state === 'failed' ? '${status.failed}'
    : '${status.ready}';

  return (
    <div className="starter">
      <header className="bar">
        <span className="brand">${mark.replaceAll('class=', 'className=')}<span>{appName}</span></span>
        <span className="status" data-state={state} aria-live="polite">{statusText}</span>
      </header>
      <main className="split">
        <section className="call" aria-labelledby="heading">
          <h1 id="heading">${copy.heading}</h1>
          <form className="ask" ref={form} onSubmit={greet}>
            <label htmlFor="name">${copy.label}</label>
            <div className="row">
              <input id="name" value={name} onChange={event => setName(event.target.value)} autoComplete="off" required />
              <button disabled={state === 'calling'}>{state === 'calling' ? '${copy.calling}' : '${copy.button}'}</button>
            </div>
          </form>
          <div className="answer" data-state={state === 'calling' ? (count === null ? 'idle' : 'answered') : state} aria-live="polite">
            <p className="route">${route}</p>
            {state === 'failed' ? (
              <>
                <p className="message">${copy.failed}</p>
                <p className="detail">${copy.hint}<code>{error}</code></p>
              </>
            ) : (
              <>
                <p className={count === null ? 'message' : 'message landed'} key={count ?? 0}>{state === 'calling' && count === null ? '${copy.pending}' : message}</p>
                <dl className="count"><dt>${copy.count}</dt><dd className={count === null ? undefined : 'landed'} key={count ?? 0}>{count ?? '—'}</dd></dl>
              </>
            )}
          </div>
        </section>
        <aside className="map" aria-labelledby="map-heading">
          <h2 id="map-heading">${copy.mapHeading}</h2>
          <ul className="files">
${fileMap('react', '            ')}
          </ul>
        </aside>
      </main>
    </div>
  );
}`,
});

// ---------------------------------------------------------------------------
// Vue

await save(path.join(root, 'vue'), {
  'package.json': manifest('vue', 'vue-tsc --noEmit', { vue: '3.5.13' }, { '@vitejs/plugin-vue': '6.0.7', 'vue-tsc': '3.1.0' }),
  'vite.config.js': viteConfig("import vue from '@vitejs/plugin-vue';", 'vue()'),
  'tsconfig.json': tsconfig(),
  'index.html': html('NetWiv + Vue', '/src/main.ts'),
  'src/style.css': style,
  'src/env.d.ts': `/// <reference types="vite/client" />
declare module '*.vue' { import type { DefineComponent } from 'vue'; const component: DefineComponent; export default component; }`,
  'src/main.ts': `import { createApp } from 'vue';
import App from './App.vue';
import './style.css';

createApp(App).mount('#app');`,
  'src/App.vue': `<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

const appName = '${copy.app}';
const name = ref('Vue');
const state = ref<CallState>('idle');
const message = ref('${copy.idle}');
const count = ref<number | null>(null);
const ms = ref(0);
const error = ref('');
const form = ref<HTMLFormElement>();

onMounted(() => {
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
        throw new Error(\`Unexpected C# response: \${JSON.stringify(result)}\`);
      await commands.smoke_done({ result: '${pass('Vue')}' });
    }
  } catch (cause) {
    error.value = String(cause);
    state.value = 'failed';
    if (smoke) await commands.smoke_done({ result: \`FAIL: \${cause}\` });
  }
}

const statusText = computed(() =>
  state.value === 'calling' ? '${status.calling}'
    : state.value === 'answered' ? \`${answered('${ms.value}')}\`
    : state.value === 'failed' ? '${status.failed}'
    : '${status.ready}');
const answerState = computed(() => (state.value === 'calling' ? (count.value === null ? 'idle' : 'answered') : state.value));
</script>

<template>
  <div class="starter">
    <header class="bar">
      <span class="brand">${mark}<span>{{ appName }}</span></span>
      <span class="status" :data-state="state" aria-live="polite">{{ statusText }}</span>
    </header>
    <main class="split">
      <section class="call" aria-labelledby="heading">
        <h1 id="heading">${copy.heading}</h1>
        <form ref="form" class="ask" @submit.prevent="greet">
          <label for="name">${copy.label}</label>
          <div class="row">
            <input id="name" v-model="name" autocomplete="off" required />
            <button :disabled="state === 'calling'">{{ state === 'calling' ? '${copy.calling}' : '${copy.button}' }}</button>
          </div>
        </form>
        <div class="answer" :data-state="answerState" aria-live="polite">
          <p class="route">${route}</p>
          <template v-if="state === 'failed'">
            <p class="message">${copy.failed}</p>
            <p class="detail">${copy.hint}<code>{{ error }}</code></p>
          </template>
          <template v-else>
            <p :key="count ?? 0" :class="['message', { landed: count !== null }]">{{ state === 'calling' && count === null ? '${copy.pending}' : message }}</p>
            <dl class="count"><dt>${copy.count}</dt><dd :key="count ?? 0" :class="{ landed: count !== null }">{{ count ?? '—' }}</dd></dl>
          </template>
        </div>
      </section>
      <aside class="map" aria-labelledby="map-heading">
        <h2 id="map-heading">${copy.mapHeading}</h2>
        <ul class="files">
${fileMap('vue', '          ')}
        </ul>
      </aside>
    </main>
  </div>
</template>`,
});

// ---------------------------------------------------------------------------
// Svelte

await save(path.join(root, 'svelte'), {
  'package.json': manifest('svelte', 'svelte-check --tsconfig ./tsconfig.json', { svelte: '5.56.3' }, { '@sveltejs/vite-plugin-svelte': '7.1.2', 'svelte-check': '4.6.0' }),
  'vite.config.js': viteConfig("import { svelte } from '@sveltejs/vite-plugin-svelte';", 'svelte()'),
  'tsconfig.json': tsconfig(),
  'index.html': html('NetWiv + Svelte', '/src/main.ts'),
  'src/style.css': style,
  'src/main.ts': `import { mount } from 'svelte';
import App from './App.svelte';
import './style.css';

mount(App, { target: document.getElementById('app')! });`,
  'src/App.svelte': `<script lang="ts">
import { onMount } from 'svelte';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

const appName = '${copy.app}';
let name = $state('Svelte');
let callState = $state<CallState>('idle');
let message = $state('${copy.idle}');
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
        throw new Error(\`Unexpected C# response: \${JSON.stringify(result)}\`);
      await commands.smoke_done({ result: '${pass('Svelte')}' });
    }
  } catch (cause) {
    error = String(cause);
    callState = 'failed';
    if (smoke) await commands.smoke_done({ result: \`FAIL: \${cause}\` });
  }
}

const statusText = $derived(callState === 'calling' ? '${status.calling}'
  : callState === 'answered' ? \`${answered('${ms}')}\`
  : callState === 'failed' ? '${status.failed}'
  : '${status.ready}');
const answerState = $derived(callState === 'calling' ? (count === null ? 'idle' : 'answered') : callState);
</script>

<div class="starter">
  <header class="bar">
    <span class="brand">${mark}<span>{appName}</span></span>
    <span class="status" data-state={callState} aria-live="polite">{statusText}</span>
  </header>
  <main class="split">
    <section class="call" aria-labelledby="heading">
      <h1 id="heading">${copy.heading}</h1>
      <form class="ask" bind:this={form} onsubmit={greet}>
        <label for="name">${copy.label}</label>
        <div class="row">
          <input id="name" bind:value={name} autocomplete="off" required />
          <button disabled={callState === 'calling'}>{callState === 'calling' ? '${copy.calling}' : '${copy.button}'}</button>
        </div>
      </form>
      <div class="answer" data-state={answerState} aria-live="polite">
        <p class="route">${route}</p>
        {#if callState === 'failed'}
          <p class="message">${copy.failed}</p>
          <p class="detail">${copy.hint}<code>{error}</code></p>
        {:else}
          {#key count}
            <p class="message" class:landed={count !== null}>{callState === 'calling' && count === null ? '${copy.pending}' : message}</p>
            <dl class="count"><dt>${copy.count}</dt><dd class:landed={count !== null}>{count ?? '—'}</dd></dl>
          {/key}
        {/if}
      </div>
    </section>
    <aside class="map" aria-labelledby="map-heading">
      <h2 id="map-heading">${copy.mapHeading}</h2>
      <ul class="files">
${fileMap('svelte', '        ')}
      </ul>
    </aside>
  </main>
</div>`,
});

// ---------------------------------------------------------------------------
// Angular

await save(path.join(root, 'angular'), {
  'package.json': JSON.stringify({
    name: 'netwiv-angular-example', version: '0.1.0', private: true,
    scripts: {
      bindings: 'node scripts/generate-bindings.mjs',
      dev: 'ng serve --host 127.0.0.1 --port 5173',
      build: 'node scripts/generate-bindings.mjs && ng build',
      'desktop:dev': 'dotnet tool restore && dotnet tool run netwiv -- dev -- --dev',
      'desktop:build': 'dotnet tool restore && dotnet tool run netwiv -- build --no-bundle',
    },
    dependencies: { ...api, '@angular/common': '21.2.0', '@angular/compiler': '21.2.0', '@angular/core': '21.2.0', '@angular/forms': '21.2.0', '@angular/platform-browser': '21.2.0', rxjs: '7.8.2', tslib: '2.8.1' },
    devDependencies: { '@angular/build': '21.2.0', '@angular/cli': '21.2.0', '@angular/compiler-cli': '21.2.0', typescript: '5.9.3' },
  }, null, 2),
  'angular.json': JSON.stringify({
    $schema: './node_modules/@angular/cli/lib/config/schema.json', version: 1,
    cli: { analytics: false },
    projects: { app: { projectType: 'application', root: '', sourceRoot: 'src', prefix: 'app',
      architect: {
        build: { builder: '@angular/build:application', options: {
          browser: 'src/main.ts', tsConfig: 'tsconfig.app.json',
          outputPath: { base: 'dist', browser: '' }, styles: ['src/style.css'],
        } },
        serve: { builder: '@angular/build:dev-server', options: { buildTarget: 'app:build', host: '127.0.0.1', port: 5173 } },
      },
    } },
  }, null, 2),
  'tsconfig.json': JSON.stringify({ compilerOptions: {
    target: 'ES2022', module: 'ES2022', moduleResolution: 'Bundler',
    lib: ['ES2022', 'DOM'], strict: true, skipLibCheck: true,
  } }, null, 2),
  'tsconfig.app.json': JSON.stringify({ extends: './tsconfig.json', compilerOptions: { outDir: './out-tsc/app', types: [] }, files: ['src/main.ts'], include: ['src/**/*.d.ts'] }, null, 2),
  'src/index.html': `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>NetWiv + Angular</title>
    <base href="./" />
  </head>
  <body>
    <app-root></app-root>
  </body>
</html>`,
  'src/main.ts': `import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app';

bootstrapApplication(App).catch(console.error);`,
  'src/style.css': style,
  'src/app.ts': `import { Component, ElementRef, computed, signal, viewChild, type AfterViewInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  template: \`
    <div class="starter">
      <header class="bar">
        <span class="brand">${mark}<span>{{ appName }}</span></span>
        <span class="status" [attr.data-state]="state()" aria-live="polite">{{ statusText() }}</span>
      </header>
      <main class="split">
        <section class="call" aria-labelledby="heading">
          <h1 id="heading">${copy.heading}</h1>
          <form #form class="ask" (ngSubmit)="greet()">
            <label for="name">${copy.label}</label>
            <div class="row">
              <input id="name" name="name" [(ngModel)]="name" autocomplete="off" required />
              <button [disabled]="state() === 'calling'">{{ state() === 'calling' ? '${copy.calling}' : '${copy.button}' }}</button>
            </div>
          </form>
          <div class="answer" [attr.data-state]="answerState()" aria-live="polite">
            <p class="route">${route}</p>
            @if (state() === 'failed') {
              <p class="message">${copy.failed}</p>
              <p class="detail">${copy.hint}<code>{{ error() }}</code></p>
            } @else {
              @for (n of [count()]; track n) {
                <p class="message" [class.landed]="n !== null">{{ state() === 'calling' && n === null ? '${copy.pending}' : message() }}</p>
                <dl class="count"><dt>${copy.count}</dt><dd [class.landed]="n !== null">{{ n ?? '—' }}</dd></dl>
              }
            }
          </div>
        </section>
        <aside class="map" aria-labelledby="map-heading">
          <h2 id="map-heading">${copy.mapHeading}</h2>
          <ul class="files">
${fileMap('angular', '            ')}
          </ul>
        </aside>
      </main>
    </div>\`,
})
export class App implements AfterViewInit {
  readonly appName = '${copy.app}';
  name = 'Angular';
  readonly state = signal<CallState>('idle');
  readonly message = signal('${copy.idle}');
  readonly count = signal<number | null>(null);
  readonly ms = signal(0);
  readonly error = signal('');
  private readonly form = viewChild.required<ElementRef<HTMLFormElement>>('form');

  readonly statusText = computed(() => {
    const state = this.state();
    return state === 'calling' ? '${status.calling}'
      : state === 'answered' ? \`${answered('${this.ms()}')}\`
      : state === 'failed' ? '${status.failed}'
      : '${status.ready}';
  });
  readonly answerState = computed(() => (this.state() === 'calling' ? (this.count() === null ? 'idle' : 'answered') : this.state()));

  ngAfterViewInit() {
    // Smoke tests open the window with ?smoke=1 and expect one automatic call.
    if (smoke) this.form().nativeElement.requestSubmit();
  }

  async greet() {
    this.state.set('calling');
    const started = performance.now();
    try {
      const result = await commands.greet({ name: this.name });
      this.ms.set(Math.round(performance.now() - started));
      this.message.set(result.message);
      this.count.set(result.count);
      this.state.set('answered');
      if (smoke) {
        if (result.message !== 'Hello, Angular!' || result.count !== 1)
          throw new Error(\`Unexpected C# response: \${JSON.stringify(result)}\`);
        await commands.smoke_done({ result: '${pass('Angular')}' });
      }
    } catch (cause) {
      this.error.set(String(cause));
      this.state.set('failed');
      if (smoke) await commands.smoke_done({ result: \`FAIL: \${cause}\` });
    }
  }
}`,
});

// ---------------------------------------------------------------------------
// Blazor: `netwiv new --template blazor` runs `dotnet new blazorwasm`, removes its sample
// pages and copies this overlay over the project root (see NewCommand.cs). The screen calls
// C# through NetWivCommands, a typed client the app build generates from its [Command]
// methods into frontend/Generated; the types both sides share live in contracts/.
// The window opens index.html, so Home answers that route as well as "/".

const blazorRoute = 'NetWivCommands.GreetAsync → Commands.Greet';
await rm(path.join(root, 'blazor'), { recursive: true, force: true });
await save(path.join(root, 'blazor'), {
  'contracts/Contracts.csproj': `<Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="AssemblyName">
  <!-- Types the desktop app and the Blazor frontend share. Both reference this project. -->
  <PropertyGroup>
    <!-- netwiv build names the app (mainBinaryName) with a global AssemblyName; keep this one. -->
    <AssemblyName>Contracts</AssemblyName>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>`,
  'contracts/Contracts.cs': `using System.Text.Json.Serialization;

// The app (src-netwiv) and the frontend serialize with the same types and options, so a
// renamed property or command changes both builds instead of silently breaking the wire.
public sealed record Greeting(string Message, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Greeting))]
public partial class AppJsonContext : JsonSerializerContext;`,
  'Directory.Build.props': `<Project>
  <!-- Keep the settings of a Directory.Build.props further up, such as a repository's. -->
  <PropertyGroup>
    <_NetWivParentBuildProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_NetWivParentBuildProps>
  </PropertyGroup>
  <Import Project="$(_NetWivParentBuildProps)" Condition="'$(_NetWivParentBuildProps)' != ''" />
  <!-- The frontend refreshes its typed client with a bindings-only build of the app and of
       contracts/. Give that build its own restore, obj and bin folders in every project so it
       never shares files with a normal, IDE or NativeAOT build running at the same time.
       obj/ stays the base folder, so neither build compiles the other's generated sources. -->
  <PropertyGroup Condition="'$(NetWivBindingsOnly)' == 'true'">
    <MSBuildProjectExtensionsPath>$(MSBuildProjectDirectory)/obj/bindings/</MSBuildProjectExtensionsPath>
    <IntermediateOutputPath>$(MSBuildProjectDirectory)/obj/bindings/$(Configuration)/</IntermediateOutputPath>
    <OutputPath>$(MSBuildProjectDirectory)/bin/bindings/$(Configuration)/</OutputPath>
  </PropertyGroup>
</Project>`,
  'frontend/NetWiv.targets': `<Project>
  <!-- The types shared with the desktop app, and the typed client generated from its commands. -->
  <ItemGroup>
    <ProjectReference Include="../contracts/Contracts.csproj" />
    <_NetWivApp Include="$(MSBuildProjectDirectory)/../src-netwiv/*.csproj" />
    <_NetWivCommandSources Include="$(MSBuildProjectDirectory)/../src-netwiv/**/*.cs;$(MSBuildProjectDirectory)/../src-netwiv/*.csproj;$(MSBuildProjectDirectory)/../contracts/**/*.cs;$(MSBuildProjectDirectory)/../contracts/*.csproj;$(MSBuildProjectDirectory)/../Directory.Build.props"
                           Exclude="$(MSBuildProjectDirectory)/../src-netwiv/obj/**;$(MSBuildProjectDirectory)/../src-netwiv/bin/**;$(MSBuildProjectDirectory)/../contracts/obj/**;$(MSBuildProjectDirectory)/../contracts/bin/**" />
  </ItemGroup>
  <PropertyGroup>
    <!-- The SDK that runs this build, also when MSBuild is hosted by an IDE. -->
    <_NetWivDotnet>$(DOTNET_HOST_PATH)</_NetWivDotnet>
    <_NetWivDotnet Condition="'$(_NetWivDotnet)' == ''">dotnet</_NetWivDotnet>
  </PropertyGroup>
  <!-- Lists the C# sources so that deleting or renaming a file also counts as a change:
       the list is rewritten only when it differs, which makes it newer than the stamp. -->
  <Target Name="NetWivTrackCommandSources" Condition="'$(DesignTimeBuild)' != 'true'">
    <PropertyGroup>
      <_NetWivCommandSourceList>$(IntermediateOutputPath)netwiv-command-sources.txt</_NetWivCommandSourceList>
      <_NetWivCommandsStamp>$(IntermediateOutputPath)netwiv-commands.stamp</_NetWivCommandsStamp>
    </PropertyGroup>
    <MakeDir Directories="$(IntermediateOutputPath)" />
    <WriteLinesToFile File="$(_NetWivCommandSourceList)" Lines="@(_NetWivCommandSources->'%(FullPath)')" Overwrite="true" WriteOnlyWhenDifferent="true" />
    <Delete Files="$(_NetWivCommandsStamp)" Condition="!Exists('Generated/NetWivCommands.g.cs')" />
  </Target>
  <!-- Every app build rewrites Generated/NetWivCommands.g.cs when its commands change. When the
       C# sources changed since the last check, refresh it with a bindings-only app build of the
       same configuration before this project compiles. The stamp records the check, so an edit
       that leaves the commands as they were does not recompile the frontend. -->
  <Target Name="NetWivGenerateCommands" BeforeTargets="CoreCompile" DependsOnTargets="NetWivTrackCommandSources" Condition="'$(DesignTimeBuild)' != 'true'"
          Inputs="@(_NetWivCommandSources);$(_NetWivCommandSourceList)" Outputs="$(_NetWivCommandsStamp)">
    <Exec Command="&quot;$(_NetWivDotnet)&quot; build &quot;@(_NetWivApp->'%(FullPath)')&quot; -c &quot;$(Configuration)&quot; -p:NetWivBindingsOnly=true -p:PublishAot=false --disable-build-servers -nologo -v:q" />
    <Touch Files="$(_NetWivCommandsStamp)" AlwaysCreate="true" />
    <ItemGroup>
      <Compile Include="Generated/NetWivCommands.g.cs" Exclude="@(Compile)" />
    </ItemGroup>
  </Target>
</Project>`,
  'frontend/Layout/MainLayout.razor': `@inherits LayoutComponentBase
@Body`,
  'frontend/wwwroot/css/app.css': style + `
/* Blazor's own loading and error elements. */
#app > .loading-progress, #app > .loading-progress-text { display: none; }
#blazor-error-ui { display: none; position: fixed; inset: auto 0 0 0; padding: 12px 20px; background: var(--stop); color: var(--stop-ink); font-size: 14px; }
#blazor-error-ui .reload { color: inherit; }
#blazor-error-ui .dismiss { float: right; cursor: pointer; }
/* Blazor focuses the heading after navigation; that focus is for screen readers. */
h1:focus { outline: none; }
`,
  'frontend/wwwroot/netwiv.js': `// The generated C# client (Generated/NetWivCommands.g.cs) calls C# commands through this
// module, which forwards them to @netwiv/api as JSON.
import { invoke } from './vendor/@netwiv/api/core.js';

export const invokeJson = async (command, args) => JSON.stringify((await invoke(command, JSON.parse(args))) ?? null);`,
  'frontend/Pages/Home.razor': `@page "/"
@page "/index.html"
@using System.Diagnostics
@inject NetWiv.Generated.NetWivCommands Commands
@inject NavigationManager Navigation

<PageTitle>@AppName</PageTitle>
<div class="starter">
    <header class="bar">
        <span class="brand">${mark}<span>@AppName</span></span>
        <span class="status" data-state="@StatusState" aria-live="polite">@StatusText</span>
    </header>
    <main class="split">
        <section class="call" aria-labelledby="heading">
            <h1 id="heading">${copy.heading}</h1>
            <form class="ask" @onsubmit="Greet" @onsubmit:preventDefault="true">
                <label for="name">${copy.label}</label>
                <div class="row">
                    <input id="name" @bind="Name" autocomplete="off" required />
                    <button disabled="@(State == CallState.Calling)">@(State == CallState.Calling ? "${copy.calling}" : "${copy.button}")</button>
                </div>
            </form>
            <div class="answer" data-state="@AnswerState" aria-live="polite">
                <p class="route">${blazorRoute}</p>
                @if (State == CallState.Failed)
                {
                    <p class="message">${copy.failed}</p>
                    <p class="detail">${copy.hint}<code>@Error</code></p>
                }
                else
                {
                    <p @key="Count" class="@(Count is null ? "message" : "message landed")">@(State == CallState.Calling && Count is null ? "${copy.pending}" : Message)</p>
                    <dl class="count"><dt>${copy.count}</dt><dd @key="Count" class="@(Count is null ? null : "landed")">@(Count?.ToString() ?? "—")</dd></dl>
                }
            </div>
        </section>
        <aside class="map" aria-labelledby="map-heading">
            <h2 id="map-heading">${copy.mapHeading}</h2>
            <ul class="files">
${fileMap('blazor', '                ').replaceAll('@', '@@')}
            </ul>
        </aside>
    </main>
</div>

@code {
    private enum CallState { Idle, Calling, Answered, Failed }

    private const string AppName = "${copy.app}";
    private string Name = "Blazor";
    private CallState State = CallState.Idle;
    private string Message = "${copy.idle}";
    private int? Count;
    private long Ms;
    private string Error = "";
    private bool Smoke;

    private string StatusText => State switch
    {
        CallState.Calling => "${status.calling}",
        CallState.Answered => $"${answered('{Ms}')}",
        CallState.Failed => "${status.failed}",
        _ => "${status.ready}",
    };
    private string StatusState => State.ToString().ToLowerInvariant();
    private string AnswerState => State == CallState.Calling ? (Count is null ? "idle" : "answered") : State.ToString().ToLowerInvariant();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        // Smoke tests open the window with ?smoke=1 and expect one automatic call.
        Smoke = new Uri(Navigation.Uri).Query.Contains("smoke", StringComparison.Ordinal);
        if (!Smoke) return;
        await Greet();
        StateHasChanged();
    }

    private async Task Greet()
    {
        State = CallState.Calling;
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Generated from Commands.Greet: renaming the method or its parameters breaks this build.
            var result = await Commands.GreetAsync(Name);
            Ms = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Message = result.Message;
            Count = result.Count;
            State = CallState.Answered;
            if (Smoke)
            {
                if (result.Message != "Hello, Blazor!" || result.Count != 1)
                    throw new InvalidOperationException($"Unexpected C# response: {result}");
                await Commands.SmokeDoneAsync("${pass('Blazor')}");
            }
        }
        catch (Exception error) when (error is JSException or InvalidOperationException or NotSupportedException)
        {
            Error = error.Message;
            State = CallState.Failed;
            if (Smoke) await Commands.SmokeDoneAsync($"FAIL: {error.Message}");
        }
    }
}`,
});

console.log('Regenerated the React, Vue, Svelte, Angular and Blazor starters.');
