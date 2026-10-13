import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const sentinel = 'SYNTHETIC-PRIVATE-USER-HOST';
const packageIds = ['NetWiv.Core', 'NetWiv.Generators', 'NetWiv.Build', 'NetWiv.Platform.Desktop', 'NetWiv.Templates', 'NetWiv.Cli'];

// Fake external tools exercise the real runner and initialized-app collaborator,
// including their filesystem assertions. This is report proof, not compiler/GUI proof.
const preload = String.raw`
import cp from 'node:child_process';
import fs from 'node:fs/promises';
import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import { mkdirSync, readFileSync, existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { syncBuiltinESMExports } from 'node:module';
const version = process.argv[3];
if (process.env.TEST_FAIL === 'report-write') {
  const writeFile = fs.writeFile;
  fs.writeFile = async (file, ...args) => {
    if (String(file).endsWith('report.json')) throw new Error(process.env.TEST_SENTINEL);
    return writeFile(file, ...args);
  };
}
const value = (args, key) => args[args.indexOf(key) + 1];
function write(file, contents) {
  mkdirSync(path.dirname(file), { recursive: true });
  if (!existsSync(file) || readFileSync(file, 'utf8') !== contents) writeFileSync(file, contents);
}
function app(directory, initialized = false) {
  write(path.join(directory, 'src-netwiv', initialized ? 'NetWiv.App.csproj' : 'BindingConsumer.csproj'),
    '<Project><NetWivVersion>' + version + '</NetWivVersion>NetWiv.Platform.Desktop NetWiv.Generators' +
    (initialized ? '' : '<ProjectReference Include="../BindingConsumer.Frontend.esproj" ReferenceOutputAssembly="false" Private="false" />') + '</Project>');
  write(path.join(directory, 'NuGet.Config'), process.argv[2]);
  write(path.join(directory, '.config/dotnet-tools.json'), JSON.stringify({ tools: { 'netwiv.cli': { version } } }));
  write(path.join(directory, 'src-netwiv/Program.cs'), '#if !DEBUG\nbuilder.InvokeHandler("greet").InvokeHandler("smoke_done");\n[Command]\npublic static Greeting Greet() {}\n[JsonSerializable(typeof(Greeting))]');
  write(path.join(directory, 'src/App.jsx'), "await commands.smoke_done({ result: 'PASS:");
  write(path.join(directory, 'src-netwiv/netwiv.conf.json'), '{}');
}
function bindings(directory, initialized = false) {
  const project = readFileSync(path.join(directory, 'src-netwiv', initialized ? 'NetWiv.App.csproj' : 'BindingConsumer.csproj'), 'utf8');
  const source = readFileSync(path.join(directory, 'src-netwiv/Program.cs'), 'utf8');
  const active = source.includes('[Command]') || source.includes('[Command("renamed_greet")]');
  const route = source.includes('renamed_greet') ? 'renamed_greet' : 'greet';
  write(path.join(directory, 'src/generated/commands.js'), active ? 'invoke("' + route + '", args, options)' : '');
  write(path.join(directory, 'src/generated/commands.d.ts'), initialized
    ? (project.includes('NetWiv.Plugin.LibraryCheck') ? '"plugin:library-check|profile" "package_echo" "count": number | string; "count": string;' : '')
    : (active ? '"message": string; "count": number; Promise<string>' : ''));
}
cp.spawn = (command, args, options) => {
  const child = new EventEmitter();
  child.stdout = new PassThrough(); child.stderr = new PassThrough();
  queueMicrotask(() => {
    try {
      if (process.env.TEST_FAIL === 'spawn') {
        child.emit('error', new Error(process.env.TEST_SENTINEL + ' ' + command)); return;
      }
      child.stdout.write(process.env.TEST_SENTINEL + ' tool output\n');
      if (process.env.TEST_FAIL === 'exit') {
        child.stderr.write(process.env.TEST_SENTINEL + ' tool failure\n'); child.emit('close', 7); return;
      }
      const cwd = options.cwd;
      if (args[0] === 'new' && args[1] === 'netwiv-react') app(value(args, '-o'));
      else if (args[0] === 'new' && args[1] === 'BindingConsumer') app(value(args, '--output'));
      else if (args.includes('init')) app(value(args, '--directory'), true);
      else if (args.includes('plugin') && args.includes('new'))
        write(path.join(value(args, '--directory'), 'NetWiv.Plugin.csproj'), '<Project Version="' + version + '" Version="' + version + '" />');
      else if (args.includes('add') || args.includes('remove')) {
        const file = path.join(cwd, 'src-netwiv/NetWiv.App.csproj');
        const contents = readFileSync(file, 'utf8');
        write(file, args.includes('add') ? contents + 'NetWiv.Plugin.LibraryCheck' : contents.replace('NetWiv.Plugin.LibraryCheck', ''));
      } else if (args[0] === 'publish') mkdirSync(value(args, '-o'), { recursive: true });
      else if (args[0] === 'build' && args.includes('-p:NetWivBindingsOnly=true')) bindings(cwd, true);
      else if (args.some(arg => arg.includes('npm'))) {
        write(path.join(cwd, 'node_modules/@tauri-apps/api/core.js'), '// synthetic API');
        write(path.join(cwd, 'node_modules/@tauri-apps/api/external/tslib/tslib.es6.js'), '// synthetic tslib');
        if (args.some(arg => arg.includes('build') || arg.includes('bindings'))) bindings(cwd);
      } else if (command.endsWith('npm')) {
        write(path.join(cwd, 'node_modules/@tauri-apps/api/core.js'), '// synthetic API');
        write(path.join(cwd, 'node_modules/@tauri-apps/api/external/tslib/tslib.es6.js'), '// synthetic tslib');
        if (args.includes('build') || args.includes('bindings')) bindings(cwd);
      }
      child.stdout.write('netwiv-cli ' + version + '\nPASS: React form\nPASS: init Wry + generated JavaScript\n');
      child.emit('close', 0);
    } catch (error) { child.emit('error', error); }
  });
  return child;
};
syncBuiltinESMExports();
`;

function fixture(t) {
  const directory = mkdtempSync(path.join(os.tmpdir(), `netwiv-${sentinel}-`));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const source = path.join(directory, 'private-checkout/tests/frontend-bindings');
  mkdirSync(source, { recursive: true });
  for (const file of ['run.mjs', 'initialized-app.mjs', 'WireContract.cs', 'calls.ts', 'ReferencedCommands.cs'])
    copyFileSync(path.join(root, 'tests/frontend-bindings', file), path.join(source, file));
  const feed = path.join(directory, 'private-feed');
  const artifacts = path.join(directory, 'private-artifacts');
  mkdirSync(feed);
  writeFileSync(path.join(directory, 'preload.mjs'), preload);
  return {
    feed, artifacts, directory,
    packages(version, templateVersion = version) {
      for (const id of packageIds)
        writeFileSync(path.join(feed, `${id}.${id === 'NetWiv.Templates' ? templateVersion : version}.nupkg`), id);
    },
    run(version = '1.2.3', templateVersion = version, fail = '', creator, reportExpected = true) {
      const result = spawnSync(process.execPath, ['--import', pathToFileURL(path.join(directory, 'preload.mjs')).href, path.join(source, 'run.mjs'),
        feed, version, templateVersion, ...(creator ? [creator] : [])], {
        cwd: directory, encoding: 'utf8', timeout: 30_000,
        env: { ...process.env, NETWIV_TEST_ARTIFACTS: artifacts, NETWIV_DOTNET: path.join(directory, 'private-tools/dotnet'),
          NETWIV_GUI_LAUNCHER: '', TEST_FAIL: fail, TEST_SENTINEL: sentinel },
      });
      assert.ifError(result.error);
      assert.doesNotMatch(result.stdout + result.stderr, new RegExp(sentinel));
      if (!reportExpected) return { result };
      const runRoot = path.join(artifacts, readdirSync(artifacts)[0]);
      const text = readFileSync(path.join(runRoot, 'report.json'), 'utf8');
      assert.doesNotMatch(text + result.stdout + result.stderr, new RegExp(sentinel));
      assert.doesNotMatch(text, /private-checkout|private-feed|private-artifacts|private-tools|stack|\.log|file:\/\//);
      const report = JSON.parse(text);
      assert.equal(report.formatVersion, 2);
      assert.equal(report.source, 'tests/frontend-bindings');
      assert.equal(report.consumer, 'consumer');
      assert.deepEqual(Object.keys(report).sort(), ['formatVersion', 'sdkVersion', 'templateVersion', 'platform', 'rid',
        'source', 'consumer', 'checks', 'packages', 'status', ...(report.status === 'passed' ? ['bindingsSha256'] : ['failure'])].sort());
      assert.ok(!('error' in report) && !('startedAt' in report) && !('finishedAt' in report));
      assert.ok(Object.keys(report.packages).every(id => packageIds.includes(id)));
      return { result, report, runRoot };
    },
  };
}

test('missing packages keep private file paths and errors out of the public report', t => {
  const f = fixture(t);
  const { result, report, runRoot } = f.run();
  assert.equal(result.status, 1);
  assert.deepEqual(report.failure, { stage: 'package-hashes', code: 'verification-failed' });
  assert.match(readFileSync(path.join(runRoot, 'local-diagnostics/failure.log'), 'utf8'), new RegExp(sentinel));
});

for (const fail of ['spawn', 'exit']) {
  test(`tool ${fail} failure retains raw diagnostics only locally`, t => {
    const f = fixture(t);
    f.packages('1.2.3');
    const { result, report, runRoot } = f.run('1.2.3', '1.2.3', fail);
    assert.equal(result.status, 1);
    assert.deepEqual(report.failure, { stage: 'create-consumer', code: 'verification-failed' });
    const diagnostic = fail === 'spawn' ? 'failure.log' : '1.log';
    assert.match(readFileSync(path.join(runRoot, 'local-diagnostics', diagnostic), 'utf8'), new RegExp(sentinel));
    assert.equal(Object.keys(report.packages).length, packageIds.length);
  });
}

for (const creator of [undefined, '--new']) {
  test(`successful ${creator ?? 'dotnet new'} run publishes only allowlisted metadata`, t => {
    const f = fixture(t);
    // Private version labels must not survive in version fields or package keys.
    const version = `1.2.3-${sentinel}`;
    f.packages(version);
    const { result, report, runRoot } = f.run(version, version, '', creator);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(report.status, 'passed');
    assert.equal(report.sdkVersion, '1.2.3');
    assert.equal(report.templateVersion, '1.2.3');
    assert.ok(!('failure' in report));
    assert.ok(report.checks.some(check => check.includes('NativeAOT')));
    for (const [id, record] of Object.entries(report.packages))
      assert.deepEqual(record, { version: '1.2.3', sha256: createHash('sha256').update(id).digest('hex') });
    assert.match(report.bindingsSha256.js, /^[a-f0-9]{64}$/);
    assert.match(report.bindingsSha256.dts, /^[a-f0-9]{64}$/);
    assert.match(readFileSync(path.join(runRoot, 'local-diagnostics/1.log'), 'utf8'), new RegExp(sentinel));
  });
}

test('malformed version metadata is withheld even when package lookup fails', t => {
  const f = fixture(t);
  const { result, report } = f.run(sentinel, `1.2.3+${sentinel}/private`);
  assert.equal(result.status, 1);
  assert.equal(report.sdkVersion, null);
  assert.equal(report.templateVersion, null);
});

test('unwritable setup suppresses private filesystem exception details', t => {
  const f = fixture(t);
  writeFileSync(f.artifacts, 'not a directory');
  const { result } = f.run('1.2.3', '1.2.3', '', undefined, false);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /setup; verification-failed/);
  assert.match(result.stderr, /report-write-failed/);
});

test('report write failures suppress raw exception text', t => {
  const f = fixture(t);
  f.packages('1.2.3');
  const { result } = f.run('1.2.3', '1.2.3', 'report-write', undefined, false);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /report-write-failed/);
});
