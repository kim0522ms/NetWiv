#!/usr/bin/env node
'use strict';

const { spawnSync } = require('node:child_process');
const { existsSync, realpathSync } = require('node:fs');
const { delimiter, join } = require('node:path');

function executable() {
  if (process.env.NETWIV_CLI_PATH) return [process.env.NETWIV_CLI_PATH, []];

  const name = process.platform === 'win32' ? 'netwiv.exe' : 'netwiv';
  const local = join(__dirname, '..', 'native', `${process.platform}-${process.arch}`, name);
  if (existsSync(local)) return [local, []];

  const dll = join(__dirname, '..', 'native', 'netwiv.dll');
  if (existsSync(dll)) return ['dotnet', [dll]];

  for (const directory of (process.env.PATH || '').split(delimiter)) {
    const candidate = join(directory, name);
    if (existsSync(candidate) && realpathSync(candidate) !== realpathSync(process.argv[1])) return [candidate, []];
  }

  throw new Error('NetWiv CLI binary is missing. Reinstall @netwiv/cli or set NETWIV_CLI_PATH.');
}

function configureSidecars(command) {
  if (process.env.NETWIV_MINISIGN_PATH) return;
  const directory = require('node:path').dirname(command);
  const minisign = join(directory, process.platform === 'win32' ? 'minisign.exe' : 'minisign');
  if (existsSync(minisign)) process.env.NETWIV_MINISIGN_PATH = minisign;
}

try {
  const [command, prefix] = executable();
  configureSidecars(command);
  const result = spawnSync(command, [...prefix, ...process.argv.slice(2)], { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exitCode = result.status === null ? 1 : result.status;
} catch (error) {
  console.error(`netwiv: ${error.message}`);
  process.exitCode = 1;
}
