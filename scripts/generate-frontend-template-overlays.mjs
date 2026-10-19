// Maintainer task: regenerate the checked-in frontends after changing this file.
// These small examples are NetWiv originals; the template names follow
// create-tauri-app v4.7.3 (e19fd21a802b995cae8d15e54b89772308af7efa).
import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('templates/frontends');
const api = { '@netwiv/api': 'file:vendor/netwiv-api' };
const vite = { vite: '8.2.2' };
const typescript = { typescript: '6.0.3' };

// Ship the unpublished API with every generated project. This path is relative
// to that project, so copied templates do not depend on a maintainer checkout.
// Build packages/api before regenerating these package snapshots.
const apiPackage = path.resolve('packages/api');
const apiManifest = JSON.parse(await readFile(path.join(apiPackage, 'package.json'), 'utf8'));
const apiSnapshot = path.resolve('templates/react-app/vendor/netwiv-api');
await rm(apiSnapshot, { recursive: true, force: true });
await mkdir(apiSnapshot, { recursive: true });
for (const file of apiManifest.files) {
  await cp(path.join(apiPackage, file), path.join(apiSnapshot, file), { recursive: true });
}
// Local directory dependencies also install devDependencies. This snapshot is
// already built, so consumers need only its runtime dependencies and exports.
const { scripts: apiScripts, devDependencies: apiDevDependencies, ...runtimeManifest } = apiManifest;
await writeFile(path.join(apiSnapshot, 'package.json'), JSON.stringify(runtimeManifest, null, 2) + '\n');
const core = await readFile(path.join(apiPackage, 'lib/core.js'), 'utf8');
const standaloneCore = path.join(root, 'shared/vendor/@netwiv/api/core.js');
await mkdir(path.dirname(standaloneCore), { recursive: true });
await writeFile(standaloneCore, core.replaceAll('@tauri-apps/api/core', '../../@tauri-apps/api/core.js'));

const style = `:root { font-family: system-ui, sans-serif; color: #e8edf8; background: #171c2b; }
body { margin: 0; min-height: 100vh; display: grid; place-items: center; }
main { width: min(32rem, calc(100vw - 3rem)); }
form { display: flex; gap: .5rem; }
input, button { font: inherit; padding: .7rem; border-radius: .4rem; }
input { flex: 1; min-width: 0; }
button { background: #72b5ff; border: 0; cursor: pointer; }
output { display: block; margin-top: 1.5rem; min-height: 3rem; }
`;
const html = `<html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>NetWiv</title></head><body><div id="app"></div><script type="module" src="/src/main.EXT"></script></body></html>\n`;

async function save(variant, files) {
  for (const [name, contents] of Object.entries(files)) {
    const destination = path.join(root, variant, name);
    await mkdir(path.dirname(destination), { recursive: true });
    await writeFile(destination, contents.endsWith('\n') ? contents : contents + '\n');
  }
}

function manifest(framework, ts, dependencies = {}, devDependencies = {}, build = 'vite build') {
  return JSON.stringify({
    name: `netwiv-${framework}${ts ? '-ts' : ''}-example`, version: '0.1.0', private: true,
    type: 'module', engines: { node: '^20.19.0 || >=22.12.0' },
    scripts: {
      bindings: 'node scripts/generate-bindings.mjs', predev: 'node scripts/generate-bindings.mjs',
      dev: 'vite --host 127.0.0.1 --port 5173 --strictPort',
      build: `node scripts/generate-bindings.mjs && ${build}`,
      'desktop:dev': 'dotnet tool restore && dotnet tool run netwiv -- dev -- --dev',
      'desktop:build': 'dotnet tool restore && dotnet tool run netwiv -- build --no-bundle',
    },
    dependencies: { ...api, ...dependencies },
    devDependencies: { ...vite, ...(ts ? typescript : {}), ...devDependencies },
  }, null, 2) + '\n';
}

function config(plugin = '') {
  return `import { defineConfig } from 'vite';\n${plugin}export default defineConfig({ ${plugin ? 'plugins: [plugin()],' : ''} base: './', server: { host: '127.0.0.1', port: 5173, strictPort: true }, build: { target: 'es2020' } });\n`;
}

function tsconfig(jsx = undefined, jsxImportSource = undefined) {
  return JSON.stringify({ compilerOptions: {
    target: 'ES2020', module: 'ESNext', moduleResolution: 'Bundler', lib: ['ES2020', 'DOM', 'DOM.Iterable'], types: ['vite/client'],
    strict: true, isolatedModules: true, noEmit: true, skipLibCheck: true,
    ...(jsx ? { jsx } : {}), ...(jsxImportSource ? { jsxImportSource } : {}),
  }, include: ['src'] }, null, 2) + '\n';
}

for (const ts of [false, true]) {
  const ext = ts ? 'ts' : 'js';
  await save(`vanilla${ts ? '-ts' : ''}`, {
    'package.json': manifest('vanilla', ts, {}, {}, ts ? 'tsc --noEmit && vite build' : 'vite build'),
    'vite.config.js': config(), 'index.html': html.replace('EXT', ext), 'src/style.css': style,
    ...(ts ? { 'tsconfig.json': tsconfig() } : {}),
    [`src/main.${ext}`]: `import { isNetWiv } from '@netwiv/api/core';\nimport { commands } from './generated/commands.js';\nimport './style.css';
document.querySelector('#app')${ts ? '!' : ''}.innerHTML = '<main><h1>NetWiv + Vanilla</h1><form><input aria-label="Name" value="World" required><button>Greet</button></form><output></output></main>';
const form = document.querySelector('form')${ts ? '!' : ''};
const output = document.querySelector('output')${ts ? '!' : ''};
form.querySelector('button')${ts ? '!' : ''}.disabled = !isNetWiv();
form.addEventListener('submit', async event => {
  event.preventDefault();
  try {
    const result = await commands.greet({ name: form.querySelector('input')${ts ? '!' : ''}.value });
    output.textContent = result.message + ' (#' + result.count + ')';
  } catch (error) { output.textContent = String(error); }
});`,
  });

  const vueExt = ts ? 'ts' : 'js';
  await save(`vue${ts ? '-ts' : ''}`, {
    'package.json': manifest('vue', ts, { vue: '3.5.13' }, { '@vitejs/plugin-vue': '6.0.7', ...(ts ? { 'vue-tsc': '3.1.0' } : {}) }, ts ? 'vue-tsc --noEmit && vite build' : 'vite build'),
    'vite.config.js': config("import plugin from '@vitejs/plugin-vue';\n"),
    'index.html': html.replace('EXT', vueExt), 'src/style.css': style,
    ...(ts ? { 'tsconfig.json': tsconfig() } : {}),
    [`src/main.${vueExt}`]: `import { createApp } from 'vue';\nimport App from './App.vue';\nimport './style.css';\ncreateApp(App).mount('#app');`,
    'src/App.vue': `<script setup${ts ? ' lang="ts"' : ''}>
import { ref } from 'vue';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
const name = ref('World');
const message = ref('');
async function greet() {
  try { const result = await commands.greet({ name: name.value }); message.value = result.message + ' (#' + result.count + ')'; }
  catch (error) { message.value = String(error); }
}
</script>
<template><main><h1>NetWiv + Vue</h1><form @submit.prevent="greet"><input v-model="name" aria-label="Name" required><button :disabled="!isNetWiv()">Greet</button></form><output>{{ message }}</output></main></template>`,
    ...(ts ? { 'src/env.d.ts': "/// <reference types=\"vite/client\" />\ndeclare module '*.vue' { import type { DefineComponent } from 'vue'; const component: DefineComponent; export default component; }" } : {}),
  });

  const svelteMain = ts ? 'ts' : 'js';
  await save(`svelte${ts ? '-ts' : ''}`, {
    'package.json': manifest('svelte', ts, { svelte: '5.56.3' }, { '@sveltejs/vite-plugin-svelte': '7.1.2', ...(ts ? { 'svelte-check': '4.6.0' } : {}) }, ts ? 'svelte-check --tsconfig ./tsconfig.json && vite build' : 'vite build'),
    'vite.config.js': config("import { svelte as plugin } from '@sveltejs/vite-plugin-svelte';\n"),
    'index.html': html.replace('EXT', svelteMain), 'src/style.css': style,
    ...(ts ? { 'tsconfig.json': tsconfig() } : {}),
    [`src/main.${svelteMain}`]: `import { mount } from 'svelte';\nimport App from './App.svelte';\nimport './style.css';\nmount(App, { target: document.getElementById('app')${ts ? '!' : ''} });`,
    'src/App.svelte': `<script${ts ? ' lang="ts"' : ''}>
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
let name = $state('World');
let message = $state('');
async function greet(event${ts ? ': SubmitEvent' : ''}) {
  event.preventDefault();
  try { const result = await commands.greet({ name }); message = result.message + ' (#' + result.count + ')'; }
  catch (error) { message = String(error); }
}


</script>
<main><h1>NetWiv + Svelte</h1><form onsubmit={greet}><input bind:value={name} aria-label="Name" required /><button disabled={!isNetWiv()}>Greet</button></form><output>{message}</output></main>`,
  });

  const reactExt = ts ? 'tsx' : 'jsx';
  if (ts) await save('react-ts', {
    'package.json': manifest('react', true, { react: '19.2.8', 'react-dom': '19.2.8' }, { '@vitejs/plugin-react': '6.1.1', '@types/react': '19.2.7', '@types/react-dom': '19.2.3' }, 'tsc --noEmit && vite build'),
    'vite.config.js': config("import plugin from '@vitejs/plugin-react';\n"),
    'index.html': html.replace('EXT', reactExt), 'tsconfig.json': tsconfig('react-jsx'), 'src/style.css': style,
    'src/main.tsx': `import { createRoot } from 'react-dom/client';\nimport App from './App';\nimport './style.css';\ncreateRoot(document.getElementById('app')!).render(<App />);`,
    'src/App.tsx': `import { useState, type FormEvent } from 'react';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = useState('World');
  const [message, setMessage] = useState('');
  async function greet(event: FormEvent) {
    event.preventDefault();
    try { const result = await commands.greet({ name }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + React</h1><form onSubmit={greet}><input aria-label="Name" value={name} onChange={event => setName(event.target.value)} required /><button disabled={!isNetWiv()}>Greet</button></form><output>{message}</output></main>;
}`, 
  });

  const solidExt = ts ? 'tsx' : 'jsx';
  await save(`solid${ts ? '-ts' : ''}`, {
    'package.json': manifest('solid', ts, { 'solid-js': '1.9.12' }, { 'vite-plugin-solid': '2.11.14' }, ts ? 'tsc --noEmit && vite build' : 'vite build'),
    'vite.config.js': config("import plugin from 'vite-plugin-solid';\n"),
    'index.html': html.replace('EXT', solidExt), 'src/style.css': style,
    ...(ts ? { 'tsconfig.json': tsconfig('preserve', 'solid-js') } : {}),
    [`src/main.${solidExt}`]: `import { render } from 'solid-js/web';\nimport App from './App';\nimport './style.css';\nrender(() => <App />, document.getElementById('app')${ts ? '!' : ''});`,
    [`src/App.${solidExt}`]: `import { createSignal } from 'solid-js';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = createSignal('World');
  const [message, setMessage] = createSignal('');
  async function greet(event${ts ? ': Event' : ''}) {
    event.preventDefault();
    try { const result = await commands.greet({ name: name() }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + Solid</h1><form onSubmit={greet}><input aria-label="Name" value={name()} onInput={event => setName(event.currentTarget.value)} required /><button disabled={!isNetWiv()}>Greet</button></form><output>{message()}</output></main>;
}`, 
  });

  const preactExt = ts ? 'tsx' : 'jsx';
  await save(`preact${ts ? '-ts' : ''}`, {
    'package.json': manifest('preact', ts, { preact: '10.28.0' }, { '@preact/preset-vite': '2.10.6' }, ts ? 'tsc --noEmit && vite build' : 'vite build'),
    'vite.config.js': config("import plugin from '@preact/preset-vite';\n"),
    'index.html': html.replace('EXT', preactExt), 'src/style.css': style,
    ...(ts ? { 'tsconfig.json': tsconfig('react-jsx', 'preact') } : {}),
    [`src/main.${preactExt}`]: `import { render } from 'preact';\nimport App from './App';\nimport './style.css';\nrender(<App />, document.getElementById('app')${ts ? '!' : ''});`,
    [`src/App.${preactExt}`]: `import { useState } from 'preact/hooks';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
export default function App() {
  const [name, setName] = useState('World');
  const [message, setMessage] = useState('');
  async function greet(event${ts ? ': Event' : ''}) {
    event.preventDefault();
    try { const result = await commands.greet({ name }); setMessage(result.message + ' (#' + result.count + ')'); }
    catch (error) { setMessage(String(error)); }
  }
  return <main><h1>NetWiv + Preact</h1><form onSubmit={greet}><input aria-label="Name" value={name} onInput={event => setName(event.currentTarget.value)} required /><button disabled={!isNetWiv()}>Greet</button></form><output>{message}</output></main>;
}`, 
  });
}

await save('angular', {
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
    lib: ['ES2022', 'DOM'], strict: true, experimentalDecorators: true, skipLibCheck: true,
  } }, null, 2),
  'tsconfig.app.json': JSON.stringify({ extends: './tsconfig.json', compilerOptions: { outDir: './out-tsc/app', types: [] }, files: ['src/main.ts'], include: ['src/**/*.d.ts'] }, null, 2),
  'src/index.html': `<html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>NetWiv</title><base href="./"></head><body><app-root></app-root></body></html>`,
  'src/main.ts': `import { bootstrapApplication } from '@angular/platform-browser';\nimport { App } from './app';\nbootstrapApplication(App).catch(console.error);`,
  'src/app.ts': `import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';
@Component({ selector: 'app-root', standalone: true, imports: [FormsModule], template: \`
<main><h1>NetWiv + Angular</h1><form (ngSubmit)="greet()"><input name="name" aria-label="Name" [(ngModel)]="name" required><button [disabled]="!desktop">Greet</button></form><output>{{ message }}</output></main>\` })
export class App {
  name = 'World';
  message = '';
  desktop = isNetWiv();
  async greet() {
    try { const result = await commands.greet({ name: this.name }); this.message = result.message + ' (#' + result.count + ')'; }
    catch (error) { this.message = String(error); }
  }
}`,
  'src/style.css': style,
});
