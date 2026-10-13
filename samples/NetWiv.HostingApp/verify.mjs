import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const [feedArgument, version, rid, mode = '--build-only'] = process.argv.slice(2);
if (!feedArgument || !version || !rid || !['--build-only', '--run'].includes(mode) || process.argv.length > 6)
  throw new Error('Usage: node verify.mjs <feed> <actual-package-version> <host-rid> [--build-only|--run]');
const hostRid = `${process.platform === 'win32' ? 'win' : process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch}`;
assert.equal(rid, hostRid, 'Build and execute on the target OS/architecture; cross-publish is outside this sample check.');
assert.ok(['win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'].includes(rid), 'Unsupported desktop RID');
const feed = path.resolve(feedArgument);
const runRoot = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(here, 'artifacts'), `hosting-${rid}-${Date.now()}`);
const consumer = path.join(runRoot, 'consumer');
const trap = path.join(runRoot, 'no-cargo');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const checks = [];
const revision = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: here, encoding: 'utf8', windowsHide: true });
const report = { formatVersion: 1, version, rid, mode, sourceRevision: revision.status === 0 ? revision.stdout.trim() : null,
  startedAt: new Date().toISOString(), runRoot, checks, packages: {}, executedModes: [] };
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
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
const environment = { ...process.env, NETWIV_PACKAGE_VERSION: version, NUGET_PACKAGES: path.join(runRoot, 'packages'),
  PATH: [trap, process.env.PATH].join(path.delimiter), DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1' };
await writeFile(path.join(consumer, 'NuGet.Config'), `<configuration>
  <packageSources><clear /><add key="netwiv" value="${xml(feed)}" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><packageSource key="netwiv"><package pattern="NetWiv.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>\n`);
let sequence = 0;
async function run(command, args, timeout = 300000) {
  const id = ++sequence;
  console.log(`[${id}] ${command} ${args.join(' ')}`);
  const child = spawn(command, args, { cwd: consumer, env: environment, windowsHide: true,
    detached: process.platform !== 'win32', stdio: ['ignore', 'pipe', 'pipe'] });
  const output = [];
  for (const stream of [child.stdout, child.stderr]) stream.on('data', bytes => { output.push(bytes); process.stdout.write(bytes); });
  const timer = setTimeout(() => {
    if (process.platform === 'win32') spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true });
    else { try { process.kill(-child.pid, 'SIGTERM'); } catch {} }
  }, timeout);
  let code;
  try { code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); }); }
  finally { clearTimeout(timer); }
  const text = Buffer.concat(output).toString('utf8');
  await writeFile(path.join(runRoot, `${id}.log`), text);
  assert.equal(code, 0, `Process failed; see ${runRoot}/${id}.log`);
  return text;
}
async function npm(args) {
  // Only fixed npm test/build literals are passed to the Windows command shell.
  return process.platform === 'win32' ? run('cmd.exe', ['/d', '/c', `npm ${args.join(' ')}`]) : run('npm', args);
}
try {
  const project = await readFile(path.join(consumer, 'src-netwiv/HostingApp.csproj'), 'utf8');
  assert.ok(!project.includes('ProjectReference'), 'Sample SDK must come only from NuGet packages.');
  for (const name of ['NetWiv.Core', 'NetWiv.Hosting', 'NetWiv.Platform.Desktop', 'NetWiv.Build', 'NetWiv.Generators']) {
    const file = `${name}.${version}.nupkg`;
    report.packages[file] = sha(await readFile(path.join(feed, file)));
  }
  await run(dotnet, ['--info']);
  await npm(['ci', '--no-audit', '--no-fund']);
  await npm(['test']);
  await npm(['run', 'build']);
  const bindings = await readFile(path.join(consumer, 'frontend/generated/commands.js'), 'utf8');
  for (const command of ['greet', 'plugin:equipment|get_status', 'plugin:equipment|reset', 'plugin:equipment|unselected'])
    assert.ok(bindings.includes(`invoke("${command}"`), `Missing generated wrapper: ${command}`);
  checks.push('package-only restore, generated application/plugin frontend bindings and production frontend build');
  for (const deployment of ['managed', 'aot']) {
    const publish = path.join(runRoot, deployment);
    const output = await run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid, '--self-contained', 'true',
      `-p:PublishAot=${deployment === 'aot'}`, `-p:PublishTrimmed=${deployment === 'aot'}`, '-p:PublishSingleFile=false',
      '-p:UseAppHost=true', '--disable-build-servers', '-o', publish], 600000);
    assert.ok(!/\bwarning (?:IL\d+|SYSLIB\d+)/i.test(output), 'Trimming/AOT warnings must be investigated.');
    assert.ok(!(await readdir(publish)).includes('NetWiv.Build.Tasks.dll'), 'Build task leaked into the app.');
    checks.push(`${deployment} self-contained publication without Cargo`);
    if (mode === '--run') {
      const executable = path.join(publish, process.platform === 'win32' ? 'HostingApp.exe' : 'HostingApp');
      const launcher = process.env.NETWIV_GUI_LAUNCHER;
      const result = launcher ? await run(launcher, [executable, '--smoke-test'], 90000) : await run(executable, ['--smoke-test'], 90000);
      assert.ok(result.includes('PASS: Hosting sample frontend IPC, scoped disposal, ACL and lifecycle cleanup'), 'Frontend smoke did not finish.');
      checks.push(`${deployment} embedded WebView IPC: constructor options, sequential/concurrent scopes, disposal, deny precedence, unselected command and lifecycle cleanup`);
      report.executedModes.push(deployment);
    }
  }
  report.outcome = 'passed';
} catch (error) {
  report.outcome = 'failed';
  report.error = String(error);
  throw error;
} finally {
  report.finishedAt = new Date().toISOString();
  await writeFile(path.join(runRoot, 'report.json'), JSON.stringify(report, null, 2));
  console.log(`Report: ${runRoot}/report.json`);
}
