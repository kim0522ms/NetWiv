import { readFile, writeFile, mkdir, readdir, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const upstream = dirname(fileURLToPath(import.meta.resolve('@tauri-apps/api/core')));
const manifest = JSON.parse(await readFile(join(root, 'package.json'), 'utf8'));
const metadata = JSON.parse(await readFile(join(upstream, 'package.json'), 'utf8'));
if (metadata.version !== manifest.devDependencies['@tauri-apps/api']) throw new Error('Expected @tauri-apps/api@2.11.1.');

// Keep the implementation behind the facade. Consumers need neither an upstream
// npm package nor declarations that depend on its public module identity.
const pending = [];
const output = join(root, 'lib/upstream');
await rm(output, { recursive: true, force: true });
for (const file of await readdir(join(root, 'lib'))) {
  if (!file.endsWith('.js') && !file.endsWith('.d.ts')) continue;
  const destination = join(root, 'lib', file);
  const extension = file.endsWith('.d.ts') ? '.d.ts' : '.js';
  const text = await readFile(destination, 'utf8');
  await writeFile(destination, text.replace(/@tauri-apps\/api\/([^'"]+)/g, (_, module) => {
    pending.push(join(upstream, `${module}${extension}`));
    return `./upstream/${module}.js`;
  }));
}

// Copy only reachable modules. JavaScript stays byte-for-byte unchanged;
// declarations normalize relative extensions for NodeNext resolution.
const copied = new Set();
while (pending.length) {
  const source = pending.shift();
  if (copied.has(source)) continue;
  copied.add(source);
  const declaration = source.endsWith('.d.ts');
  const text = await readFile(source, 'utf8');
  const normalized = text.replace(/(from\s+['"])(\.[^'"]+)(['"])/g, (_, start, specifier, end) => {
    const module = specifier.replace(/\.js$/, '');
    pending.push(join(dirname(source), `${module}${declaration ? '.d.ts' : '.js'}`));
    return `${start}${module}.js${end}`;
  });
  const destination = join(output, relative(upstream, source));
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, declaration ? normalized : text);
}

// Plugin guests share the facade's core objects and private wire implementation.
await mkdir(join(output, 'plugins'), { recursive: true });
for (const [name, version] of Object.entries(manifest.devDependencies)) {
  if (!name.startsWith('@tauri-apps/plugin-')) continue;
  const plugin = name.slice('@tauri-apps/plugin-'.length);
  const entry = fileURLToPath(import.meta.resolve(name));
  const metadata = JSON.parse(await readFile(join(dirname(entry), '../package.json'), 'utf8'));
  if (metadata.version !== version) throw new Error(`Unexpected version of ${name}.`);
  for (const extension of ['.js', '.d.ts']) {
    const text = await readFile(join(dirname(entry), `index${extension}`), 'utf8');
    const normalized = text.replace(/@tauri-apps\/api\/([^'"\s]+)/g, '../../$1.js');
    await writeFile(join(output, 'plugins', `${plugin}${extension}`), normalized);
    const facade = join(root, 'lib/plugins', `${plugin}${extension}`);
    await writeFile(facade, (await readFile(facade, 'utf8')).replaceAll(name, `../upstream/plugins/${plugin}.js`));
  }
}
