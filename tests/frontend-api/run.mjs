import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { npm, packApi } from './packed-package.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const temporary = await mkdtemp(path.join(os.tmpdir(), 'netwiv-api-consumer-'));
async function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, `Package consumer command failed:\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
}
try {
  const { tarball, packed, manifest } = await packApi(run, temporary);
  await run(process.execPath, ['--test', path.join(root, 'tests/frontend-api/json-transport.test.mjs')], root);
  assert.ok(packed.files.some(file => file.path === 'lib/index.js'));
  assert.ok(packed.files.some(file => file.path === 'lib/index.d.ts'));
  assert.ok(!packed.files.some(file => /^(?:src|tests|node_modules)\/|\.tgz$|package-lock\.json$/.test(file.path)),
    'The tarball must contain the public package, without sources, tests or dependency archives');
  const pins = JSON.parse(await readFile(path.join(root, 'compatibility/upstream-lock.json'), 'utf8'));
  const npmLock = JSON.parse(await readFile(path.join(root, 'packages/api/package-lock.json'), 'utf8'));
  for (const pin of [pins.api, ...Object.values(pins.plugins)]) {
    assert.equal(manifest.devDependencies[pin.name], pin.version);
    assert.equal(npmLock.packages[`node_modules/${pin.name}`].integrity, pin.integrity);
  }
  const snapshot = path.join(root, 'templates/react-app/vendor/netwiv-api');
  for (const { path: file } of packed.files) {
    if (file === 'package.json') {
      const { scripts, devDependencies, overrides, ...consumerManifest } = manifest;
      assert.deepEqual(JSON.parse(await readFile(path.join(snapshot, file), 'utf8')), consumerManifest,
        'The template snapshot must keep the public package metadata without maintainer build dependencies');
      continue;
    }
    assert.equal((await readFile(path.join(snapshot, file), 'utf8')).replaceAll('\r\n', '\n'),
      (await readFile(path.join(temporary, 'package', file), 'utf8')).replaceAll('\r\n', '\n'),
      `Regenerate the frontend template API snapshot: ${file}`);
  }
  for (const { path: file } of packed.files) {
    assert.equal((await readFile(path.join(root, 'samples/NetWiv.HostingApp/vendor/netwiv-api', file), 'utf8')).replaceAll('\r\n', '\n'),
      (await readFile(path.join(snapshot, file), 'utf8')).replaceAll('\r\n', '\n'), `Hosting sample snapshot: ${file}`);
    if (file.startsWith('lib/') && file.endsWith('.js')) {
      assert.equal((await readFile(path.join(root, 'templates/frontends/shared/vendor/@netwiv/api', file.slice(4)), 'utf8')).replaceAll('\r\n', '\n'),
        (await readFile(path.join(snapshot, file), 'utf8')).replaceAll('\r\n', '\n'), `Standalone snapshot: ${file}`);
    }
  }
  const standalone = await readFile(path.join(root, 'templates/frontends/shared/vendor/@netwiv/api/core.js'), 'utf8');
  assert.equal(standalone.replaceAll('\r\n', '\n'), (await readFile(path.join(temporary, 'package/lib/core.js'), 'utf8'))
    .replaceAll('\r\n', '\n'));
  const consumer = path.join(temporary, 'consumer');
  await mkdir(consumer);
  await copyFile(tarball, path.join(consumer, 'netwiv-api.tgz'));
  await writeFile(path.join(consumer, 'package.json'), JSON.stringify({ name: 'netwiv-api-packed-consumer', private: true,
    type: 'module', dependencies: { '@netwiv/api': 'file:./netwiv-api.tgz' },
    devDependencies: { typescript: manifest.devDependencies.typescript } }));
  await npm(run, ['install', '--no-audit', '--no-fund'], consumer);
  await npm(run, ['ci', '--no-audit', '--no-fund'], consumer);
  const pluginNames = Object.keys(pins.plugins);
  const types = await readFile(new URL('./public-types.ts', import.meta.url), 'utf8');
  await writeFile(path.join(consumer, 'public-types.ts'), types + pluginNames.map((name, index) =>
    `\nimport * as plugin${index} from '@netwiv/api/plugins/${name}';\nvoid plugin${index};`).join(''));
  assert.ok(!(await readdir(path.join(consumer, 'node_modules'))).includes('@tauri-apps'));

  for (const [module, resolution] of [['NodeNext', 'NodeNext'], ['ESNext', 'Bundler']]) {
    await run(process.execPath, [path.join(consumer, 'node_modules/typescript/bin/tsc'), '--strict', '--noEmit',
      '--module', module, '--moduleResolution', resolution, '--target', 'ES2022', 'public-types.ts'], consumer);
  }
  await writeFile(path.join(consumer, 'runtime.mjs'), `import assert from 'node:assert/strict';
import * as api from '@netwiv/api';
assert.equal('isNetWiv' in api, false);
assert.equal('isNetWiv' in api.core, false);
assert.equal('mocks' in api, false);
assert.equal('isTauri' in api.core, false);
for (const name of ${JSON.stringify(pluginNames)}) {
  assert.ok(Object.keys(await import('@netwiv/api/plugins/' + name)).length > 0);
  await assert.rejects(import('@tauri-apps/plugin-' + name), { code: 'ERR_MODULE_NOT_FOUND' });
}
await assert.rejects(import('@netwiv/api/plugins/updater'), { code: 'ERR_PACKAGE_PATH_NOT_EXPORTED' });
await assert.rejects(import('@tauri-apps/api/core'), { code: 'ERR_MODULE_NOT_FOUND' });
await assert.rejects(import('@netwiv/api/lib/upstream/core.js'), { code: 'ERR_PACKAGE_PATH_NOT_EXPORTED' });
await assert.rejects(import('@netwiv/api/mocks'), { code: 'ERR_PACKAGE_PATH_NOT_EXPORTED' });
await assert.rejects(import('@netwiv/api/lib/core.js'), { code: 'ERR_PACKAGE_PATH_NOT_EXPORTED' });
`);
  await run(process.execPath, ['runtime.mjs'], consumer);
  assert.ok((await readdir(path.join(consumer, 'node_modules/@netwiv/api'))).includes('lib'));
  console.log('PASS packed @netwiv/api install, public types, export boundaries and template snapshots');
} finally {
  // Remove only the absolute, unique directory returned by mkdtemp above.
  assert.equal(path.dirname(temporary), os.tmpdir());
  assert.ok(path.basename(temporary).startsWith('netwiv-api-consumer-'));
  await rm(temporary, { recursive: true, force: true });
}
