import assert from 'node:assert/strict';
import { cp, mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('../../packages/api', import.meta.url));

export async function npm(run, args, cwd) {
  // These callers use fixed test literals; paths are passed through cwd, never a shell.
  return process.platform === 'win32'
    ? run('cmd.exe', ['/d', '/c', `npm ${args.join(' ')}`], cwd)
    : run(process.env.NETWIV_NPM ?? 'npm', args, cwd);
}

export async function packApi(run, directory) {
  const packageDirectory = path.join(directory, 'package');
  await mkdir(directory, { recursive: true });
  await cp(source, packageDirectory, { recursive: true,
    filter: file => !['node_modules', 'lib'].includes(path.relative(source, file).split(path.sep)[0]) });
  await npm(run, ['ci', '--no-audit', '--no-fund'], packageDirectory);
  const [packed] = JSON.parse(await npm(run, ['pack', '--json'], packageDirectory));
  assert.equal(packed.name, '@netwiv/api');
  assert.equal(path.basename(packed.filename), packed.filename);
  const manifest = JSON.parse(await readFile(path.join(packageDirectory, 'package.json'), 'utf8'));
  assert.equal(manifest.devDependencies['@tauri-apps/api'], '2.11.1');
  assert.equal(manifest.dependencies?.['@tauri-apps/api'], undefined);
  return { tarball: path.join(packageDirectory, packed.filename), packed, manifest };
}
