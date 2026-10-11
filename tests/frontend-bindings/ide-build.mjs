import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Tests the IDE's ordinary MSBuild entry point, starting with a packaged, empty app.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const [feedArg, version] = process.argv.slice(2);
if (!feedArg || !version) throw new Error('Usage: node tests/frontend-bindings/ide-build.mjs <feed> <version>');
const runRoot = path.resolve(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(root, 'artifacts'), `ide-build-${process.platform}-${Date.now()}`);
const app = path.join(runRoot, 'app with spaces');
const tool = path.join(runRoot, 'tool');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const rid = process.platform === 'win32' ? 'win-x64' : 'linux-x64';
const exe = process.platform === 'win32' ? 'IdeApp.exe' : 'IdeApp';
const checks = [];
await mkdir(runRoot, { recursive: true });
let sequence = 0;
async function run(command, args, cwd = app, expectFailure = false) {
  const chunks = [];
  const child = spawn(command, args, { cwd, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  for (const stream of [child.stdout, child.stderr]) stream.on('data', chunk => chunks.push(chunk));
  const timer = setTimeout(() => {
    if (process.platform === 'win32') spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true });
    else child.kill('SIGKILL');
  }, 300000);
  let code;
  try { code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); }); }
  finally { clearTimeout(timer); }
  const output = Buffer.concat(chunks).toString('utf8');
  const log = path.join(runRoot, `${++sequence}.log`);
  await writeFile(log, output);
  console.log(`${expectFailure ? 'Expected failure' : 'Run'}: ${command} ${args.join(' ')} → ${code} (${log})`);
  assert.ok(expectFailure ? code !== 0 : code === 0, output);
  return output;
}
async function smoke(binary) {
  const output = process.platform === 'win32'
    ? await run(binary, ['--smoke-test'])
    : await run('bash', [path.join(root, 'scripts/run-linux-smoke-desktop.sh'), binary, process.env.NETWIV_TEST_BACKEND ?? 'x11', '--smoke-test']);
  assert.ok(output.includes('PASS: React form'), output);
}
function singleFrontendBuild(output) {
  assert.equal((output.match(/building client environment/g) ?? []).length, 1, 'Frontend must build exactly once per entry point');
}
try {
  await run(dotnet, ['tool', 'install', 'NetWiv.Cli', '--version', version, '--tool-path', tool, '--add-source', path.resolve(feedArg)], root);
  await run(path.join(tool, process.platform === 'win32' ? 'netwiv.exe' : 'netwiv'), ['new', 'IdeApp', '--output', app, '--source', path.resolve(feedArg)], root);
  for (const directory of ['dist', 'node_modules', 'src/generated'])
    await assert.rejects(stat(path.join(app, directory)), { code: 'ENOENT' });
  const reactPath = path.join(app, 'src/App.jsx');
  const htmlPath = path.join(app, 'index.html');
  const react = (await readFile(reactPath, 'utf8')).replace('if (smoke) {', 'if (smoke) {\n        if (document.querySelector(\'meta[name="ide-check"]\')?.content !== "first") throw new Error("Stale embedded assets");');
  const html = (await readFile(htmlPath, 'utf8')).replace('</head>', '<meta name="ide-check" content="first"></head>');
  await writeFile(reactPath, react);
  await writeFile(htmlPath, html);
  const solution = path.join(app, 'IdeApp.sln');
  const projects = await run(dotnet, ['sln', solution, 'list']);
  assert.ok(projects.includes('IdeApp.csproj') && projects.includes('IdeApp.Frontend.esproj'));
  singleFrontendBuild(await run(dotnet, ['build', solution, '-c', 'Debug', '-m:2', '-warnaserror']));
  await smoke(path.join(app, 'src-netwiv/bin/Debug/net8.0', exe));
  checks.push('fresh parallel SLNX build restores npm, generates bindings, embeds assets and runs IPC with one frontend build');
  const restoreTime = (await stat(path.join(app, 'node_modules/.netwiv-restore'))).mtimeMs;
  const oldAssets = await readdir(path.join(app, 'dist/assets'));
  await writeFile(reactPath, react.replace('!== "first"', '!== "second"'));
  await writeFile(htmlPath, html.replace('content="first"', 'content="second"'));
  singleFrontendBuild(await run(dotnet, ['build', 'src-netwiv', '-c', 'Debug', '-warnaserror']));
  assert.equal((await stat(path.join(app, 'node_modules/.netwiv-restore'))).mtimeMs, restoreTime);
  assert.notDeepEqual(await readdir(path.join(app, 'dist/assets')), oldAssets);
  await smoke(path.join(app, 'src-netwiv/bin/Debug/net8.0', exe));
  checks.push('frontend-only edits refresh hashed embedded assets without reinstalling unchanged dependencies');
  singleFrontendBuild(await run(dotnet, ['build', 'IdeApp.Frontend.esproj', '-warnaserror']));
  const items = JSON.parse(await run(dotnet, ['msbuild', 'IdeApp.Frontend.esproj', '-getItem:None']));
  const files = items.Items.None.map(item => item.Identity.replaceAll('\\', '/'));
  assert.ok(files.includes('src/App.jsx') && files.includes('package.json'));
  assert.ok(!files.some(file => file.startsWith('src-netwiv/') || file.startsWith('node_modules/') || file.startsWith('dist/')));
  checks.push('standalone ESProj builds once and exposes only frontend files to the IDE');
  await writeFile(reactPath, 'this is deliberately invalid JSX !!!');
  await run(dotnet, ['msbuild', 'src-netwiv/IdeApp.csproj', '-t:BuildReactFrontend', '-p:DesignTimeBuild=true']);
  await run(dotnet, ['build', 'src-netwiv', '-p:NetWivBindingsOnly=true']);
  await run(dotnet, ['build', 'src-netwiv', '-p:NetWivDevelopment=true']);
  await run(dotnet, ['build', solution], app, true);
  checks.push('design-time, bindings-only and dev-server paths skip React build; real build propagates frontend errors');
  await writeFile(reactPath, react);
  await writeFile(htmlPath, html);
  singleFrontendBuild(await run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid, '-p:PublishAot=true', '-warnaserror', '-o', path.join(app, 'publish')]));
  await smoke(path.join(app, 'publish', exe));
  checks.push('direct NativeAOT publish embeds matching frontend assets and runs real IPC');
  await run(dotnet, ['tool', 'restore']);
  singleFrontendBuild(await run(dotnet, ['tool', 'run', 'netwiv', '--', 'build', '--no-bundle']));
  checks.push('netwiv build uses the same project graph without a duplicate frontend hook');
  await writeFile(path.join(runRoot, 'report.json'), JSON.stringify({ version, platform: process.platform, checks, status: 'passed' }, null, 2));
  console.log(`PASS: ${checks.length} IDE build scenarios; ${runRoot}`);
} catch (error) {
  await writeFile(path.join(runRoot, 'report.json'), JSON.stringify({ version, checks, status: 'failed', error: String(error) }, null, 2));
  throw error;
}
