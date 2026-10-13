import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const [feedArgument, version, rid, mode = '--build-only'] = process.argv.slice(2);
if (!feedArgument || !version || !rid || !['--build-only', '--run'].includes(mode) || process.argv.length > 6)
  throw new Error('Usage: node verify.mjs <feed> <actual-package-version> <host-rid> [--build-only|--run]');
assert.ok(version.trim() === version && /^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/.test(version), 'Invalid package version');
const hostRid = `${process.platform === 'win32' ? 'win' : process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch}`;
assert.equal(rid, hostRid, 'Build and execute on the target OS/architecture; cross-publish is outside this sample check.');
assert.ok(['win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'].includes(rid), 'Unsupported desktop RID');
const feed = path.resolve(feedArgument);
const runId = `hosting-${rid}-${randomUUID()}`;
const runRoot = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(here, 'artifacts'), runId);
const consumer = path.join(runRoot, 'consumer');
const trap = path.join(runRoot, 'no-cargo');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const checks = [];
const revision = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: here, encoding: 'utf8', windowsHide: true });
const sourceRevision = revision.status === 0 ? revision.stdout.trim() : '';
// Only allowlisted identifiers and results belong in the shareable report.
// Package versions may contain personal prerelease labels; hashes identify the bytes.
const report = { formatVersion: 2, runId, rid, mode,
  sourceRevision: /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(sourceRevision) ? sourceRevision : null,
  checks, packages: {}, executedModes: [], stage: 'prepare-consumer', failureCode: null };
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const environment = { ...process.env, NETWIV_PACKAGE_VERSION: version, NUGET_PACKAGES: path.join(runRoot, 'packages'),
  PATH: [trap, process.env.PATH].join(path.delimiter), DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1' };
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
  report.stage = 'package-validation';
  const project = await readFile(path.join(consumer, 'src-netwiv/HostingApp.csproj'), 'utf8');
  assert.ok(!project.includes('ProjectReference'), 'Sample SDK must come only from NuGet packages.');
  for (const name of ['NetWiv.Core', 'NetWiv.Hosting', 'NetWiv.Platform.Desktop', 'NetWiv.Build', 'NetWiv.Generators']) {
    const file = `${name}.${version}.nupkg`;
    report.packages[name] = sha(await readFile(path.join(feed, file)));
  }
  report.stage = 'dotnet-info';
  await run(dotnet, ['--info']);
  report.stage = 'frontend-install';
  await npm(['ci', '--no-audit', '--no-fund']);
  report.stage = 'frontend-test';
  await npm(['test']);
  report.stage = 'frontend-build';
  await npm(['run', 'build']);
  report.stage = 'generated-bindings';
  const bindings = await readFile(path.join(consumer, 'frontend/generated/commands.js'), 'utf8');
  for (const command of ['greet', 'plugin:equipment|get_status', 'plugin:equipment|reset', 'plugin:equipment|unselected'])
    assert.ok(bindings.includes(`invoke("${command}"`), `Missing generated wrapper: ${command}`);
  checks.push('package-only restore, generated application/plugin frontend bindings and production frontend build');
  for (const deployment of ['managed', 'aot']) {
    report.stage = `${deployment}-publish`;
    const publish = path.join(runRoot, deployment);
    const output = await run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid, '--self-contained', 'true',
      `-p:PublishAot=${deployment === 'aot'}`, `-p:PublishTrimmed=${deployment === 'aot'}`, '-p:PublishSingleFile=false',
      '-p:UseAppHost=true', '--disable-build-servers', '-o', publish], 600000);
    report.stage = `${deployment}-output`;
    assert.ok(!/\bwarning (?:IL\d+|SYSLIB\d+)/i.test(output), 'Trimming/AOT warnings must be investigated.');
    assert.ok(!(await readdir(publish)).includes('NetWiv.Build.Tasks.dll'), 'Build task leaked into the app.');
    checks.push(`${deployment} self-contained publication without Cargo`);
    if (mode === '--run') {
      report.stage = `${deployment}-run`;
      const executable = path.join(publish, process.platform === 'win32' ? 'HostingApp.exe' : 'HostingApp');
      const launcher = process.env.NETWIV_GUI_LAUNCHER;
      const result = launcher ? await run(launcher, [executable, '--smoke-test'], 90000) : await run(executable, ['--smoke-test'], 90000);
      assert.ok(result.includes('PASS: Hosting sample frontend IPC, scoped disposal, ACL and lifecycle cleanup'), 'Frontend smoke did not finish.');
      checks.push(`${deployment} embedded WebView IPC: constructor options, sequential/concurrent scopes, disposal, deny precedence, unselected command and lifecycle cleanup`);
      report.executedModes.push(deployment);
    }
  }
  report.stage = 'complete';
  report.outcome = 'passed';
} catch (error) {
  report.outcome = 'failed';
  report.failureCode = 'verification-failed';
  // Raw errors and subprocess logs are private local diagnostics, never report fields.
  await writeFile(path.join(runRoot, 'private-error.log'), error instanceof Error ? error.stack : String(error));
  throw error;
} finally {
  await writeFile(path.join(runRoot, 'report.json'), JSON.stringify(report, null, 2));
  console.log(`Shareable report: ${runId}/report.json (all other output is private)`);
}
