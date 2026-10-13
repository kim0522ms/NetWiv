import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { copyFile, mkdir, readFile, readdir, stat, utimes, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkInitializedApp } from './initialized-app.mjs';

// Every run starts with an empty consumer and a private template/NuGet cache.
const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const [feedArgument, version, templateVersion = version, creator] = process.argv.slice(2);
if (!feedArgument || !version || creator && creator !== '--new')
  throw new Error('Usage: node tests/frontend-bindings/run.mjs <feed> <SDK-version> [template-version] [--new]');
const feed = path.resolve(feedArgument);
const runRoot = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(root, 'artifacts'), `frontend-bindings-${process.platform}-${Date.now()}`);
const consumer = path.join(runRoot, 'consumer');
const hive = path.join(runRoot, 'template-hive');
const trap = path.join(runRoot, 'no-cargo');
const diagnostics = path.join(runRoot, 'local-diagnostics');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const rid = process.platform === 'win32' ? 'win-x64' : 'linux-x64';
const exe = process.platform === 'win32' ? 'BindingConsumer.exe' : 'BindingConsumer';
const environment = { ...process.env, PATH: [trap, ...(path.isAbsolute(dotnet) ? [path.dirname(dotnet)] : []), process.env.PATH].join(path.delimiter),
  DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1',
  DOTNET_ROOT: path.isAbsolute(dotnet) ? path.dirname(dotnet) : process.env.DOTNET_ROOT };
let sequence = 0;
const checks = [];
// Only numeric version components are public; local prerelease/build labels may identify a tester.
const publicVersion = value => /^(\d+\.\d+\.\d+)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.exec(value)?.[1] ?? null;
const report = { formatVersion: 2, sdkVersion: publicVersion(version), templateVersion: publicVersion(templateVersion),
  platform: process.platform, rid, source: 'tests/frontend-bindings', consumer: 'consumer', checks, packages: {} };
let stage = 'setup';
const xml = text => text.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const sha = bytes => createHash('sha256').update(bytes).digest('hex');

async function run(command, args, cwd = consumer, timeout = 300000) {
  const id = ++sequence;
  console.log(`[${id}] Running verification command`);
  const child = spawn(command, args, { cwd, env: environment, windowsHide: true, detached: process.platform !== 'win32', stdio: ['ignore', 'pipe', 'pipe'] });
  const out = [], err = [];
  child.stdout.on('data', chunk => { out.push(chunk); });
  child.stderr.on('data', chunk => { err.push(chunk); });
  const timer = setTimeout(() => {
    if (process.platform === 'win32') spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true });
    else { try { process.kill(-child.pid, 'SIGTERM'); } catch {} }
  }, timeout);
  let code;
  try {
    code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); });
  } finally { clearTimeout(timer); }
  const stdout = Buffer.concat(out).toString('utf8'), stderr = Buffer.concat(err).toString('utf8');
  await writeFile(path.join(diagnostics, `${id}.log`), `${command} ${args.join(' ')}\n${stdout}${stderr}`);
  assert.equal(code, 0, `Child failed; see local-diagnostics/${id}.log`);
  return stdout;
}

async function npm(args) {
  // Commands are fixed test literals; no interpolated paths enter a shell command.
  if (process.platform === 'win32') return run('cmd.exe', ['/d', '/c', `npm ${args.join(' ')}`]);
  return run(process.env.NETWIV_NPM ?? 'npm', args);
}

try {
  await mkdir(trap, { recursive: true });
  await mkdir(diagnostics, { recursive: true });
  await writeFile(path.join(trap, 'cargo.cmd'), '@echo Cargo must not run in a NuGet consumer. 1>&2\r\n@exit /b 97\r\n');
  await writeFile(path.join(trap, 'cargo'), '#!/bin/sh\necho "Cargo must not run in a NuGet consumer." >&2\nexit 97\n', { mode: 0o755 });
  stage = 'package-hashes';
  for (const name of ['NetWiv.Core', 'NetWiv.Generators', 'NetWiv.Build', 'NetWiv.Platform.Desktop',
    ...(creator === '--new' ? [] : ['NetWiv.Templates']), 'NetWiv.Cli']) {
    const file = `${name}.${name === 'NetWiv.Templates' ? templateVersion : version}.nupkg`;
    report.packages[name] = { version: publicVersion(name === 'NetWiv.Templates' ? templateVersion : version),
      sha256: sha(await readFile(path.join(feed, file))) };
  }
  stage = 'create-consumer';
  if (creator === '--new') {
    const bootstrap = path.join(runRoot, 'bootstrap-tool');
    await run(dotnet, ['tool', 'install', 'NetWiv.Cli', '--version', version, '--tool-path', bootstrap, '--add-source', feed], root);
    await run(path.join(bootstrap, process.platform === 'win32' ? 'netwiv.exe' : 'netwiv'),
      ['new', 'BindingConsumer', '--output', consumer, '--source', feed], root);
    const sources = await readFile(path.join(consumer, 'NuGet.Config'), 'utf8');
    assert.ok(sources.includes(xml(feed)), 'netwiv new did not configure the package source');
    checks.push('packaged .NET CLI creates the React app and configures its NuGet source without Cargo');
  } else {
    await run(dotnet, ['new', 'install', path.join(feed, `NetWiv.Templates.${templateVersion}.nupkg`), '--debug:custom-hive', hive], root);
    await run(dotnet, ['new', 'netwiv-react', '-n', 'BindingConsumer', '-o', consumer,
      ...(templateVersion === version ? [] : ['--NetWivVersion', version]), '--debug:custom-hive', hive], root);
  }
  stage = 'consumer-contract';
  const projectFile = path.join(consumer, 'src-netwiv/BindingConsumer.csproj');
  const project = await readFile(projectFile, 'utf8');
  assert.ok(project.includes(`<NetWivVersion>${version}</NetWivVersion>`));
  const sourceReferences = [...project.matchAll(/<ProjectReference\b[^>]*\/>/g)].map(match => match[0]);
  assert.deepEqual(sourceReferences, ['<ProjectReference Include="../BindingConsumer.Frontend.esproj" ReferenceOutputAssembly="false" Private="false" />'],
    'Only the build-only frontend project may be referenced; the SDK must come from NuGet');
  await writeFile(path.join(consumer, 'Directory.Build.props'), '<Project />\n');
  await writeFile(path.join(consumer, 'NuGet.Config'), `<configuration>
  <config><add key="globalPackagesFolder" value=".nuget/packages" /></config>
  <packageSources><clear /><add key="netwiv" value="${xml(feed)}" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><packageSource key="netwiv"><package pattern="NetWiv.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>\n`);
  checks.push('template creates a package-only project with the selected SDK version');
  const manifest = JSON.parse(await readFile(path.join(consumer, '.config/dotnet-tools.json'), 'utf8'));
  assert.equal(manifest.tools['netwiv.cli'].version, version);
  stage = 'cli-restore';
  await run(dotnet, ['tool', 'restore']);
  assert.ok((await run(dotnet, ['tool', 'run', 'netwiv', '--', '--version'])).includes(`netwiv-cli ${version}`));
  checks.push('local CLI tool restores with the matching SDK version and reports its package version');
  stage = 'frontend-build';
  const wirePath = path.join(consumer, 'src-netwiv/WireContract.cs');
  const wireSource = await readFile(path.join(here, 'WireContract.cs'), 'utf8');
  await writeFile(wirePath, wireSource);
  const programPath = path.join(consumer, 'src-netwiv/Program.cs');
  assert.ok((await readFile(programPath, 'utf8')).includes('#if !DEBUG'), 'Template processing removed the C# development guard');
  await writeFile(programPath, (await readFile(programPath, 'utf8'))
    .replace('builder.InvokeHandler("smoke_done")', 'builder.InvokeHandler("smoke_done").InvokeHandler("wire_profile").InvokeHandler("constructor_profile").InvokeHandler("number_profile").InvokeHandler("extension_profile")')
    .replace('[JsonSerializable(typeof(Greeting))]', '[JsonSerializable(typeof(Greeting))]\n[JsonSerializable(typeof(WireContract))]\n[JsonSerializable(typeof(ConstructorWire))]\n[JsonSerializable(typeof(NumberWire))]\n[JsonSerializable(typeof(ExtensibleWire))]'));
  const reactPath = path.join(consumer, 'src/App.jsx');
  await writeFile(reactPath, (await readFile(reactPath, 'utf8')).replace("await commands.smoke_done({ result: 'PASS:", `const wire = await commands.wire_profile({ value: { displayName: 'typed', status: 'in-progress', note: null, omittedCount: 0, 'exact-name': 9, input: 17, inputOnly: 19, outputOnly: { arbitrary: true }, computed: [], ignored: { arbitrary: true } } });
        if (wire.displayName !== 'typed' || wire.status !== 'in-progress' || wire['exact-name'] !== 9 || 'note' in wire || 'omittedCount' in wire)
          throw new Error('Generated wire contract differs from NativeAOT serialization: ' + JSON.stringify(wire));
        if (wire.output !== 17 || wire.computed !== 42 || 'input' in wire || 'ignored' in wire)
          throw new Error('Property direction differs from NativeAOT serialization: ' + JSON.stringify(wire));
        if (wire.inputOnlyResult !== 19 || wire.outputOnly !== 23 || 'inputOnly' in wire)
          throw new Error('Directional ignore differs from NativeAOT serialization: ' + JSON.stringify(wire));
        const fields = await commands.wire_profile({ value: { 'included-field': 31, ignoredField: { arbitrary: true } } });
        if (fields['included-field'] !== 31 || 'ignoredField' in fields) throw new Error('JsonInclude field mapping differs from NativeAOT');
        for (const nullableStatus of [null, 'in-progress', 23]) {
          const nullable = await commands.wire_profile({ value: { nullableStatus } });
          if (nullable.nullableStatus !== nullableStatus) throw new Error('Nullable enum converter changed its wire value');
        }
        const constructor = await commands.constructor_profile({ value: { name: 'Ada', count: 2 } });
        if (constructor.name !== 'Ada' || constructor.count !== 2 || constructor.note !== null || constructor.retry !== 3)
          throw new Error('Constructor defaults differ from NativeAOT serialization: ' + JSON.stringify(constructor));
        let constructorRejected = false;
        try { await commands.constructor_profile({ value: { count: 2 } }); }
        catch (error) { constructorRejected = String(error).includes('missing field \`name\`'); }
        if (!constructorRejected) throw new Error('Missing required constructor input did not produce its expected rejection');
        const numberInput = { count: '42', strict: 2, values: ['1', null], nested: { count: 3, ratio: 'NaN' } };
        const numbers = await commands.number_profile({ value: numberInput });
        if (numbers.count !== '42' || numbers.strict !== 2 || numbers.values[0] !== '1' || numbers.values[1] !== null ||
            numbers.nested.count !== 3 || numbers.nested.ratio !== 'NaN')
          throw new Error('Number policy differs from NativeAOT serialization: ' + JSON.stringify(numbers));
        for (const invalid of [{ ...numberInput, strict: '2' }, { ...numberInput, count: 'bad' }]) {
          let rejected = false;
          try { await commands.number_profile({ value: invalid }); }
          catch (error) { rejected = String(error).includes('invalid args'); }
          if (!rejected) throw new Error('Invalid numeric input was accepted');
        }
        const extensionInput = { name: 'Ada', extra: 42, nested: { value: null }, list: [1, null] };
        const extension = await commands.extension_profile({ value: extensionInput });
        if (JSON.stringify(extension) !== JSON.stringify(extensionInput)) throw new Error('Extension data did not flatten and roundtrip');
        for (const invalid of [{ extra: 42 }, { name: 42, extra: null }]) {
          let rejected = false;
          try { await commands.extension_profile({ value: invalid }); }
          catch (error) { rejected = String(error).includes('invalid args'); }
          if (!rejected) throw new Error('Extension data bypassed known property validation');
        }
        await commands.smoke_done({ result: 'PASS:`));
  await npm(['ci', '--no-audit', '--no-fund']);
  await npm(['run', 'build']);
  const jsFile = path.join(consumer, 'src/generated/commands.js');
  const dtsFile = path.join(consumer, 'src/generated/commands.d.ts');
  const js = await readFile(jsFile, 'utf8'), dts = await readFile(dtsFile, 'utf8');
  assert.ok(js.includes('invoke("greet", args, options)'));
  assert.ok(dts.includes('"message": string') && dts.includes('"count": number'));
  checks.push('clean frontend build automatically exports JavaScript and TypeScript from C#');
  stage = 'typescript-contract';
  await copyFile(path.join(here, 'calls.ts'), path.join(consumer, 'calls.ts'));
  await writeFile(path.join(consumer, 'tsconfig.json'), JSON.stringify({ compilerOptions: {
    strict: true, noEmit: true, module: 'ESNext', target: 'ES2022', moduleResolution: 'Bundler', skipLibCheck: false }, files: ['calls.ts'] }));
  await run(process.execPath, [path.join(here, 'node_modules/typescript/bin/tsc'), '-p', path.join(consumer, 'tsconfig.json')]);
  checks.push('strict TypeScript accepts valid calls and rejects wrong types, names, casing and injected state');
  stage = 'export-invalidation';
  const timestamp = new Date('2000-01-01T00:00:00Z');
  await utimes(jsFile, timestamp, timestamp);
  await utimes(dtsFile, timestamp, timestamp);
  await npm(['run', 'bindings']);
  assert.equal((await stat(jsFile)).mtimeMs, timestamp.getTime());
  assert.equal((await stat(dtsFile)).mtimeMs, timestamp.getTime());
  checks.push('unchanged builds preserve generated timestamps');
  const sourcePath = path.join(consumer, 'src-netwiv/Program.cs');
  const source = await readFile(sourcePath, 'utf8');
  await writeFile(sourcePath, source.replace(/\[Command\](\s+public static Greeting Greet)/, '[Command("renamed_greet")]$1'));
  await npm(['run', 'bindings']);
  assert.ok((await readFile(jsFile, 'utf8')).includes('invoke("renamed_greet"'));
  assert.ok(!(await readFile(jsFile, 'utf8')).includes('invoke("greet"'));
  await writeFile(sourcePath, source.replaceAll('[Command]', ''));
  await writeFile(wirePath, wireSource.replaceAll('[Command]', ''));
  await npm(['run', 'bindings']);
  assert.ok(!(await readFile(dtsFile, 'utf8')).includes('Promise<'));
  await writeFile(sourcePath, source);
  await writeFile(wirePath, wireSource);
  await npm(['run', 'build']);
  checks.push('renaming or removing every command invalidates stale exports');
  stage = 'nativeaot-publish';
  const publish = path.join(consumer, 'publish', rid);
  const published = await run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid,
    '-p:PublishAot=true', '-p:TreatWarningsAsErrors=true', '--disable-build-servers', '-o', publish]);
  assert.ok(!/\bwarning (?:IL\d+|SYSLIB\d+)/i.test(published), 'NativeAOT emitted interop/trimming warnings');
  assert.ok(!(await readdir(publish)).includes('NetWiv.Build.Tasks.dll'), 'MSBuild task leaked into product output');
  checks.push('NativeAOT publish excludes MSBuild task binaries');
  stage = 'nativeaot-ipc';
  const executable = path.join(publish, exe);
  const output = process.env.NETWIV_GUI_LAUNCHER
    ? await run('bash', [process.env.NETWIV_GUI_LAUNCHER, executable, process.env.NETWIV_GUI_BACKEND ?? 'x11', '--smoke-test'], consumer, 60000)
    : await run(executable, ['--smoke-test'], consumer, 60000);
  assert.ok(output.includes('PASS: React form'), 'Native WebView did not report the React IPC result');
  checks.push('rendered React form calls generated wrappers and validates a real C# stateful response');
  checks.push('NativeAOT DTO serialization matches generated string enums, inheritance, explicit JSON names and conditional omission');
  checks.push('constructor-bound required inputs agree between strict TypeScript and NativeAOT invocation, while nullable/default fields may be omitted');
  checks.push('nullable property enum converters preserve null, explicit names and unnamed numeric values in strict TypeScript and NativeAOT');
  checks.push('JsonInclude public fields preserve explicit wire names while ordinary unconfigured fields remain absent');
  stage = 'initialized-app';
  await checkInitializedApp({ consumer, dotnet, rid, version, run });
  checks.push('packaged CLI init creates a matching Wry SDK/generator project and runs generated IPC in managed and NativeAOT modes without Cargo');
  checks.push('NuGet referenced application/plugin bindings retain producer JSON policies, pass strict TypeScript, and execute through managed/NativeAOT WebView IPC; unregistered and denied routes reject, and package removal invalidates exports');
  report.status = 'passed';
  report.bindingsSha256 = { js: sha(await readFile(jsFile)), dts: sha(await readFile(dtsFile)) };
} catch (error) {
  report.status = 'failed'; report.failure = { stage, code: 'verification-failed' }; process.exitCode = 1;
  try { await writeFile(path.join(diagnostics, 'failure.log'), String(error.stack ?? error) + '\n'); } catch {}
  console.error(`Frontend verification failed (${stage}; verification-failed). Raw diagnostics are local only.`);
} finally {
  try {
    await writeFile(path.join(runRoot, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    console.log('Frontend report saved as report.json. Publish only this file; local-diagnostics and the consumer are private.');
  } catch {
    process.exitCode = 1;
    console.error('Frontend report could not be saved (report-write-failed).');
  }
}
