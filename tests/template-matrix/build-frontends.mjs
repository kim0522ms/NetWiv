import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const templates = process.argv.slice(2);
if (!templates.length) throw Error('Specify one or more frontend template names');

for (const name of templates) {
  if (!/^[a-z-]+$/.test(name)) throw Error(`Invalid template name: ${name}`);
  const source = name === 'react' ? path.join(root, 'templates/react-app') : path.join(root, 'templates/frontends', name);
  const temporary = await mkdtemp(path.join(os.tmpdir(), `netwiv-template-${name}-`));
  let passed = false;
  try {
    await cp(source, temporary, { recursive: true });
    if (name !== 'react') await cp(path.join(root, 'templates/react-app/vendor'), path.join(temporary, 'vendor'), { recursive: true });
    const generated = path.join(temporary, 'src/generated');
    await mkdir(generated, { recursive: true });
    await writeFile(path.join(generated, 'commands.js'), 'import { invoke } from "@netwiv/api/core";\nexport const commands = { greet: args => invoke("greet", args) };\n');
    await writeFile(path.join(generated, 'commands.d.ts'), 'export type Greeting = { message: string; count: number };\nexport declare const commands: { greet(args: { name: string }): Promise<Greeting> };\n');
    const packagePath = path.join(temporary, 'package.json');
    const manifest = JSON.parse(await readFile(packagePath, 'utf8'));
    assert.equal(manifest.dependencies['@netwiv/api'], 'file:vendor/netwiv-api');
    assert.equal(manifest.dependencies['@tauri-apps/api'], undefined);
    manifest.scripts.build = manifest.scripts.build.replace(/^(?:node scripts\/generate-bindings\.mjs|npm run bindings) && /, '');
    await writeFile(packagePath, JSON.stringify(manifest, null, 2) + '\n');
    for (const args of [['ci', '--no-audit', '--no-fund'], ['run', 'build']]) {
      const command = process.platform === 'win32' ? 'cmd.exe' : 'npm';
      const commandArgs = process.platform === 'win32' ? ['/d', '/s', '/c', `npm ${args.join(' ')}`] : args;
      const result = spawnSync(command, commandArgs, { cwd: temporary, encoding: 'utf8', windowsHide: true });
      if (result.status !== 0) throw Error(`${name}: npm ${args.join(' ')} failed:\n${result.stdout}\n${result.stderr}\nSource copy: ${temporary}`);
    }
    process.stdout.write(`PASS ${name}: npm ci and frontend build\n`);
    passed = true;
  } finally {
    // The only recursive removal is the unique temporary directory just created above.
    if (passed && path.basename(temporary).startsWith(`netwiv-template-${name}-`) && path.dirname(temporary) === os.tmpdir())
      await rm(temporary, { recursive: true, force: true });
  }
}
