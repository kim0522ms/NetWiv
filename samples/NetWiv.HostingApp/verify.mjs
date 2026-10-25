import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const [feedArgument, version, rid, mode = '--build-only'] = process.argv.slice(2);
if (!feedArgument || !version || !rid || !['--build-only', '--run'].includes(mode) || process.argv.length > 6) {
  console.error('Usage: node verify.mjs <feed> <actual-package-version> <host-rid> [--build-only|--run]');
  process.exit(1);
}
if (version.trim() !== version || !/^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/.test(version)) {
  console.error('Invalid package version.');
  process.exit(1);
}
const hostRid = `${process.platform === 'win32' ? 'win' : process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch}`;
if (rid !== hostRid || !['win-x64', 'linux-x64', 'osx-arm64'].includes(rid)) {
  console.error('Use a supported RID matching the host OS/architecture.');
  process.exit(1);
}
const feed = path.resolve(feedArgument);
const runRoot = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(here, 'artifacts'), `hosting-${rid}-${randomUUID()}`);
const consumer = path.join(runRoot, 'consumer');
const trap = path.join(runRoot, 'no-cargo');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const environment = { ...process.env, NETWIV_PACKAGE_VERSION: version, NUGET_PACKAGES: path.join(runRoot, 'packages'),
  PATH: [trap, process.env.PATH].join(path.delimiter), DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1' };
async function run(command, args, timeout = 300000, captureOutput = false) {
  const child = spawn(command, args, { cwd: consumer, env: environment, windowsHide: true,
    detached: process.platform !== 'win32', stdio: ['ignore', captureOutput ? 'pipe' : 'ignore', captureOutput ? 'pipe' : 'ignore'] });
  const output = [];
  if (captureOutput)
    for (const stream of [child.stdout, child.stderr]) stream.on('data', bytes => output.push(bytes));
  const timer = setTimeout(() => {
    if (process.platform === 'win32') spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
    else { try { process.kill(-child.pid, 'SIGTERM'); } catch {} }
  }, timeout);
  let code;
  try { code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); }); }
  finally { clearTimeout(timer); }
  assert.equal(code, 0, 'Verification process failed.');
  return Buffer.concat(output).toString('utf8');
}
async function npm(args) {
  // Only fixed npm test/build literals are passed to the Windows command shell.
  return process.platform === 'win32' ? run('cmd.exe', ['/d', '/c', `npm ${args.join(' ')}`]) : run('npm', args);
}
try {
  await mkdir(runRoot, { recursive: true });
  await mkdir(trap, { recursive: true });
  await mkdir(consumer, { recursive: true });
  const excluded = ['artifacts', 'node_modules', 'bin', 'obj', 'dist', 'generated', '.nuget'];
  // Copy entries separately: Node rejects cp(sample, sample/artifacts/consumer), even with a filter.
  for (const entry of await readdir(here)) {
    if (excluded.includes(entry)) continue;
    await cp(path.join(here, entry), path.join(consumer, entry), { recursive: true,
      filter: source => !path.relative(here, source).split(path.sep).some(part => excluded.includes(part)) });
  }
  await writeFile(path.join(trap, 'cargo.cmd'), '@echo Cargo must not run in a package consumer. 1>&2\r\n@exit /b 97\r\n');
  await writeFile(path.join(trap, 'cargo'), '#!/bin/sh\necho "Cargo must not run in a package consumer." >&2\nexit 97\n', { mode: 0o755 });
  await writeFile(path.join(consumer, 'NuGet.Config'), `<configuration>
  <packageSources><clear /><add key="netwiv" value="${xml(feed)}" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><packageSource key="netwiv"><package pattern="NetWiv.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>\n`);
  const project = await readFile(path.join(consumer, 'src-netwiv/HostingApp.csproj'), 'utf8');
  assert.ok(!project.includes('ProjectReference'), 'Sample SDK must come only from NuGet packages.');
  await npm(['ci', '--no-audit', '--no-fund']);
  await npm(['test']);
  await npm(['run', 'build']);
  const bindings = await readFile(path.join(consumer, 'frontend/generated/commands.js'), 'utf8');
  const factory = await readFile(path.join(consumer, 'frontend/generated/commands.factory.js'), 'utf8');
  assert.ok(bindings.includes('./commands.factory.js') && bindings.includes('createCommands(invoke)'));
  for (const command of ['greet', 'plugin:equipment|get_status', 'plugin:equipment|reset', 'plugin:equipment|unselected'])
    assert.ok(factory.includes(`invoke("${command}"`), `Missing generated wrapper: ${command}`);
  for (const deployment of ['managed', 'aot']) {
    const publish = path.join(runRoot, deployment);
    const output = await run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid, '--self-contained', 'true',
      `-p:PublishAot=${deployment === 'aot'}`, `-p:PublishTrimmed=${deployment === 'aot'}`, '-p:PublishSingleFile=false',
      '-p:UseAppHost=true', '--disable-build-servers', '-o', publish], 600000, true);
    assert.ok(!/\bwarning (?:IL\d+|SYSLIB\d+)/i.test(output), 'Trimming/AOT warnings must be investigated.');
    assert.ok(!(await readdir(publish)).includes('NetWiv.Build.Tasks.dll'), 'Build task leaked into the app.');
    if (mode === '--run') {
      const executable = path.join(publish, process.platform === 'win32' ? 'HostingApp.exe' : 'HostingApp');
      const launcher = process.env.NETWIV_GUI_LAUNCHER;
      const result = launcher ? await run(launcher, [executable, '--smoke-test'], 90000, true) : await run(executable, ['--smoke-test'], 90000, true);
      assert.ok(result.includes('PASS: Hosting sample frontend IPC, scoped disposal, ACL and lifecycle cleanup'), 'Frontend smoke did not finish.');
    }
  }
  console.log('Hosting sample verification passed.');
} catch {
  console.error('Hosting sample verification failed.');
  process.exitCode = 1;
}
