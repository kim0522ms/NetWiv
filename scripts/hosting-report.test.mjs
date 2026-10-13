import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import test from 'node:test';

const script = fileURLToPath(new URL('../samples/NetWiv.HostingApp/verify.mjs', import.meta.url));
const rid = `${process.platform === 'win32' ? 'win' : process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch}`;
const supported = ['win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'].includes(rid);
const names = ['NetWiv.Core', 'NetWiv.Hosting', 'NetWiv.Platform.Desktop', 'NetWiv.Build', 'NetWiv.Generators'];
const sentinels = ['PRIVATE-ACCOUNT-SENTINEL', 'PRIVATE-HOST-SENTINEL', 'PRIVATE-STDERR-SENTINEL'];
const version = '0.1.0-PRIVATE-ACCOUNT-SENTINEL';

// Replace only external tools and one setup failure, while executing the actual
// verification CLI, filesystem preparation, assertions and report writer.
const preload = `
import childProcess from 'node:child_process';
import fs from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import path from 'node:path';
const fail = process.env.FIXTURE_FAILURE;
const privateText = process.env.FIXTURE_PRIVATE_TEXT;
childProcess.spawnSync = () => ({ status: 0, stdout: process.env.FIXTURE_REVISION });
if (fail === 'prepare-consumer') fs.cp = async () => { throw new Error(privateText); };
childProcess.spawn = (command, args, options) => {
  const child = new EventEmitter();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  queueMicrotask(async () => {
    try {
      const text = args.join(' ');
      const deployment = args.includes('-p:PublishAot=true') || path.basename(path.dirname(command)) === 'aot' ? 'aot' : 'managed';
      const stage = args[0] === '--info' ? 'dotnet-info'
        : args[0] === 'publish' ? deployment + '-publish'
        : args.includes('--smoke-test') ? deployment + '-run'
        : text.includes('ci --no-audit') ? 'frontend-install'
        : text.includes('run build') ? 'frontend-build' : 'frontend-test';
      if (fail === 'spawn-error') { child.emit('error', new Error(privateText)); return; }
      child.stderr.write(privateText);
      if (stage === fail) { child.emit('close', 42); return; }
      if (stage === 'frontend-build') {
        const generated = path.join(options.cwd, 'frontend/generated');
        await fs.mkdir(generated, { recursive: true });
        const commands = fail === 'generated-bindings' ? [] : ['greet', 'plugin:equipment|get_status', 'plugin:equipment|reset', 'plugin:equipment|unselected'];
        await fs.writeFile(path.join(generated, 'commands.js'), commands.map(c => 'invoke("' + c + '")').join('\\n'));
      }
      if (args[0] === 'publish') {
        const publish = args[args.indexOf('-o') + 1];
        await fs.mkdir(publish, { recursive: true });
        if (fail === deployment + '-output') await fs.writeFile(path.join(publish, 'NetWiv.Build.Tasks.dll'), privateText);
      }
      if (args.includes('--smoke-test')) child.stdout.write('PASS: Hosting sample frontend IPC, scoped disposal, ACL and lifecycle cleanup');
      child.emit('close', 0);
    } catch (error) { child.emit('error', error); }
  });
  return child;
};
syncBuiltinESMExports();
`;

async function fixture(t, failure = '', revision = 'a'.repeat(40), argumentVersion = version, mode = '--run') {
  const root = await mkdtemp(path.join(os.tmpdir(), 'netwiv-hosting-report-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const privateRoot = path.join(root, sentinels[0], sentinels[1]);
  const sample = path.join(privateRoot, 'sample');
  const feed = path.join(privateRoot, 'feed');
  const artifacts = path.join(privateRoot, 'artifacts');
  await mkdir(path.join(sample, 'src-netwiv'), { recursive: true });
  await mkdir(feed, { recursive: true });
  await copyFile(script, path.join(sample, 'verify.mjs'));
  await writeFile(path.join(sample, 'src-netwiv/HostingApp.csproj'), '<Project />');
  for (const name of names) {
    if (failure === 'package-validation' && name === names[0]) continue;
    await writeFile(path.join(feed, `${name}.${version}.nupkg`), sentinels.join(' '));
  }
  const mock = path.join(privateRoot, 'mock.mjs');
  await writeFile(mock, preload);
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(mock).href, path.join(sample, 'verify.mjs'), feed, argumentVersion, rid, mode], {
    encoding: 'utf8', timeout: 30_000,
    env: { ...process.env, NETWIV_TEST_ARTIFACTS: artifacts, NETWIV_DOTNET: path.join(privateRoot, 'dotnet'),
      NETWIV_GUI_LAUNCHER: '', FIXTURE_FAILURE: failure, FIXTURE_REVISION: revision,
      FIXTURE_PRIVATE_TEXT: privateRoot + ' ' + sentinels.join(' ') },
  });
  assert.ifError(result.error);
  if (argumentVersion !== version) {
    assert.notEqual(result.status, 0);
    await assert.rejects(readdir(artifacts), { code: 'ENOENT' });
    return;
  }
  const runs = await readdir(artifacts).catch(() => { throw new Error(result.stderr.replaceAll(root, '<fixture>')); });
  assert.equal(runs.length, 1);
  const runRoot = path.join(artifacts, runs[0]);
  const text = await readFile(path.join(runRoot, 'report.json'), 'utf8');
  const report = JSON.parse(text);
  for (const sentinel of [...sentinels, root, privateRoot, version]) assert.ok(!text.includes(sentinel), 'Private fixture value reached the report');
  assert.deepEqual(Object.keys(report).sort(), ['formatVersion', 'runId', 'rid', 'mode', 'sourceRevision', 'checks', 'packages', 'executedModes', 'stage', 'failureCode', 'outcome'].sort());
  assert.equal(report.formatVersion, 2);
  assert.match(report.runId, /^hosting-(?:win-x64|linux-x64|osx-arm64|osx-x64)-[0-9a-f-]{36}$/);
  assert.equal(report.runId, runs[0]);
  assert.equal(report.rid, rid);
  assert.equal(report.mode, mode);
  assert.ok(report.sourceRevision === null || /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(report.sourceRevision));
  for (const [name, hash] of Object.entries(report.packages)) {
    assert.ok(names.includes(name));
    assert.match(hash, /^[0-9a-f]{64}$/);
  }
  assert.equal(report.outcome, failure ? 'failed' : 'passed');
  assert.equal(report.failureCode, failure ? 'verification-failed' : null);
  assert.equal(report.stage, failure === 'spawn-error' ? 'dotnet-info' : failure || 'complete');
  if (failure) {
    assert.notEqual(result.status, 0);
    const error = await readFile(path.join(runRoot, 'private-error.log'), 'utf8');
    assert.ok(error.includes(sentinels[0]), 'Failure diagnostics must remain available privately');
  } else {
    assert.equal(result.status, 0);
    assert.deepEqual(report.executedModes, mode === '--run' ? ['managed', 'aot'] : []);
    assert.deepEqual(Object.keys(report.packages), names);
    assert.equal(report.checks.length, mode === '--run' ? 5 : 3);
    const log = await readFile(path.join(runRoot, '1.log'), 'utf8');
    assert.ok(log.includes(sentinels[2]), 'Subprocess output must remain available privately');
  }
  return report;
}

test('Hosting verification success report excludes paths, prerelease labels and tool output', { skip: !supported }, async t => {
  const report = await fixture(t);
  assert.equal(report.sourceRevision, 'a'.repeat(40));
});

test('Hosting verification rejects arbitrary revision output in the public report', { skip: !supported }, async t => {
  const report = await fixture(t, '', sentinels.join(' '));
  assert.equal(report.sourceRevision, null);
});

test('Hosting build-only report does not claim execution', { skip: !supported }, t => fixture(t, '', 'a'.repeat(40), version, '--build-only'));

for (const stage of ['prepare-consumer', 'package-validation', 'spawn-error', 'dotnet-info', 'frontend-install', 'frontend-test',
  'frontend-build', 'generated-bindings', 'managed-publish', 'managed-output', 'managed-run', 'aot-publish', 'aot-output', 'aot-run']) {
  test(`Hosting verification keeps ${stage} failure diagnostics private`, { skip: !supported }, t => fixture(t, stage));
}

for (const value of ['../PRIVATE_ACCOUNT_SENTINEL', '0.1.0/PRIVATE_HOST_SENTINEL', '0.1.0\nPRIVATE_HOST_SENTINEL', '0.1.0\n']) {
  test('Hosting verification rejects a package version containing path or control characters', { skip: !supported }, t => fixture(t, '', '', value));
}
