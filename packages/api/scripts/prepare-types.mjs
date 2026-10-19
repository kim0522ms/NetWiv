import { readFile, writeFile, mkdir, readdir, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const upstream = dirname(fileURLToPath(import.meta.resolve('@tauri-apps/api/core')));
const metadata = JSON.parse(await readFile(join(upstream, 'package.json'), 'utf8'));
if (metadata.version !== '2.11.1') throw new Error('Expected @tauri-apps/api@2.11.1.');

// The pinned declarations use extensionless relative imports. A declaration-only
// copy with explicit .js specifiers supports NodeNext without changing upstream.
const required = new Set();
const pending = [];
for (const file of await readdir(join(root, 'lib'))) {
  if (!file.endsWith('.d.ts')) continue;
  const destination = join(root, 'lib', file);
  const text = await readFile(destination, 'utf8');
  await writeFile(destination, text.replace(
    /@tauri-apps\/api\/([^'"]+)/g,
    (_, module) => {
      // Core has no relative imports; retain its private class identities across
      // official and facade imports (notably Channel and Resource).
      if (module === 'core') return '@tauri-apps/api/core';
      pending.push(join(upstream, `${module}.d.ts`));
      return `./upstream/${module}.js`;
    }
  ));
}
// Only declarations reachable from the facade are distributed (no mocks/index).
const typeOutput = join(root, 'lib/upstream');
await rm(typeOutput, { recursive: true, force: true });
while (pending.length) {
  const source = pending.shift();
  if (required.has(source)) continue;
  required.add(source);
  const text = await readFile(source, 'utf8');
  const output = text.replace(
    /(from\s+['"])(\.[^'"]+)(['"])/g,
    (_, start, specifier, end) => {
      const module = specifier.replace(/\.js$/, '');
      const dependency = join(dirname(source), `${module}.d.ts`);
      if (dependency === join(upstream, 'core.d.ts')) {
        return `${start}@tauri-apps/api/core${end}`;
      }
      pending.push(dependency);
      return `${start}${module}.js${end}`;
    }
  );
  const destination = join(typeOutput, relative(upstream, source));
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, output);
}
