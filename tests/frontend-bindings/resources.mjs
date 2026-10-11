import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile, rm, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = process.env.NETWIV_SOURCE_ROOT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const root = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(repo, 'artifacts'), `resources-${process.platform}-${Date.now()}`);
const rid = process.platform === 'win32' ? 'win-x64' : 'linux-x64';
const cli = path.join(repo, 'src/NetWiv.Cli/bin/Release/net10.0/netwiv.dll');
await mkdir(root, { recursive: true });
const checks = [];
function run(command, args, cwd = root, expected = 0) {
  const result = spawnSync(command, args, { cwd, env: { ...process.env, DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1' }, encoding: 'utf8', timeout: 300000 });
  process.stdout.write(result.stdout ?? ''); process.stderr.write(result.stderr ?? '');
  assert.equal(result.status, expected, result.error?.message ?? `Failed: ${command} ${args.join(' ')}`);
  return result.stdout + result.stderr;
}
const read = async file => readFile(file, 'utf8');
async function absent(file) { await assert.rejects(access(file), { code: 'ENOENT' }); }
run(dotnet, ['build', 'src/NetWiv.Cli', '-c', 'Release', '--disable-build-servers'], repo);
run(dotnet, ['build', 'src/NetWiv.Build', '-c', 'Debug', '--disable-build-servers'], repo);
run(dotnet, [cli, 'init', '--ci', '--directory', root, '--app-name', 'ResourceProbe', '--netwiv-source', repo]);
const backend = path.join(root, 'src-netwiv');
const configPath = path.join(backend, 'netwiv.conf.json');
const config = JSON.parse(await read(configPath));
await mkdir(path.join(root, 'dist'), { recursive: true });
await writeFile(path.join(root, 'dist/index.html'), '<!doctype html><title>Resource probe</title>');
await mkdir(path.join(root, 'assets/nested'), { recursive: true });
await writeFile(path.join(root, 'assets/readme.txt'), 'resource-v1');
await writeFile(path.join(root, 'assets/nested/data.txt'), 'nested');
await writeFile(path.join(root, 'assets/.hidden.txt'), 'hidden');
await writeFile(path.join(backend, 'Program.cs'), `
using NetWiv;
[assembly: GenerateFrontendBindings]
internal static class Program {
  static void Main(string[] args) {
    var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, args[0]));
    if (text != args[1]) throw new Exception("Resource payload mismatch: " + text);
    Console.WriteLine("PASS executable reads staged resource: " + args[0]);
  }
}`);
config.bundle = { active: false, resources: ['../assets'] };
await writeFile(configPath, JSON.stringify(config));
const managed = path.join(root, 'managed');
const build = ['build', backend, '-c', 'Release', '-p:PublishAot=false', '--disable-build-servers', '-o', managed];
run(dotnet, build);
run(dotnet, [path.join(managed, 'NetWiv.App.dll'), '_up_/assets/readme.txt', 'resource-v1']);
assert.equal(await read(path.join(managed, '_up_/assets/nested/data.txt')), 'nested');
checks.push('managed build stages recursive list paths with parent encoding');
config.bundle.resources = { '../assets/*.txt': 'flat', '../assets/nested': 'tree', '../assets/readme.txt': 'exact/name.txt' };
await writeFile(configPath, JSON.stringify(config));
run(dotnet, build);
await absent(path.join(managed, '_up_/assets/readme.txt'));
assert.equal(await read(path.join(managed, 'flat/.hidden.txt')), 'hidden');
assert.equal(await read(path.join(managed, 'tree/data.txt')), 'nested');
run(dotnet, [path.join(managed, 'NetWiv.App.dll'), 'exact/name.txt', 'resource-v1']);
checks.push('mapping, glob flattening, hidden files and removal of previous list destinations');
const publish = path.join(root, 'publish');
const publishArgs = ['publish', backend, '-c', 'Release', '-r', rid, '-p:PublishAot=true', '-p:TreatWarningsAsErrors=true', '--disable-build-servers', '-o', publish];
run(dotnet, publishArgs);
const executable = 'NetWiv.App' + (process.platform === 'win32' ? '.exe' : '');
run(path.join(publish, executable), ['flat/readme.txt', 'resource-v1']);
await writeFile(path.join(root, 'assets/readme.txt'), 'resource-v2-updated');
await rm(path.join(root, 'assets/.hidden.txt'));
run(dotnet, [...publishArgs, '--no-build']);
run(path.join(publish, executable), ['flat/readme.txt', 'resource-v2-updated']);
await absent(path.join(publish, 'flat/.hidden.txt'));
checks.push('NativeAOT publish and --no-build refresh changed and deleted resources');
const overlay = path.join(root, 'overlay.json');
await writeFile(overlay, JSON.stringify({ bundle: { resources: ['../assets/readme.txt'] } }));
run(dotnet, [cli, 'build', '--no-bundle', '--config', overlay, '--', '-p:TreatWarningsAsErrors=true'], backend);
const triple = process.platform === 'win32' ? 'x86_64-pc-windows-msvc' : 'x86_64-unknown-linux-gnu';
const cliOutput = path.join(root, 'target', triple, 'release');
run(path.join(cliOutput, executable), ['_up_/assets/readme.txt', 'resource-v2-updated']);
checks.push('CLI NativeAOT build resolves overlay resources against original config directory');
config.bundle.resources = ['../missing-resource'];
await writeFile(configPath, JSON.stringify(config));
assert.match(run(dotnet, build, root, 1), /DTOXBUILD004/);
checks.push('missing resource fails the build with DTOXBUILD004');
await writeFile(path.join(root, 'report.json'), JSON.stringify({ status: 'passed', rid, checks }, null, 2));
console.log('Report: ' + path.join(root, 'report.json'));
