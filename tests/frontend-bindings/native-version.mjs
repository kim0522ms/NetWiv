import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = process.env.NETWIV_SOURCE_ROOT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const dotnet = process.env.NETWIV_DOTNET ?? 'dotnet';
const root = path.join(process.env.NETWIV_TEST_ARTIFACTS ?? path.join(repo, 'artifacts'), `native-version-${process.platform}-${Date.now()}`);
const rid = process.platform === 'win32' ? 'win-x64' : 'linux-x64';
await mkdir(root, { recursive: true });
function run(command, args, cwd = repo) {
  const result = spawnSync(command, args, { cwd, env: { ...process.env, DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1' }, stdio: 'ignore', timeout: 300000 });
  assert.equal(result.status, 0, 'Child verification command failed');
}
run(dotnet, ['build', 'src/NetWiv.Cli', '-c', 'Release', '--disable-build-servers']);
run(dotnet, [path.join(repo, 'src/NetWiv.Cli/bin/Release/net8.0/netwiv.dll'), 'init', '--ci', '--directory', root, '--app-name', 'VersionProbe', '--netwiv-source', repo]);
const configPath = path.join(root, 'src-netwiv/netwiv.conf.json');
const config = JSON.parse(await readFile(configPath, 'utf8'));
config.version = '../package.json';
await writeFile(configPath, JSON.stringify(config));
await writeFile(path.join(root, 'package.json'), '{"version":"1.2.3+42"}');
await writeFile(path.join(root, 'src-netwiv/Program.cs'), `
using NetWiv;
using NetWiv.Platform.Desktop;
using System.Reflection;
using System.Diagnostics;
[assembly: GenerateFrontendBindings]
internal static class Program {
  [STAThread] static void Main() {
    using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedConfig().Build();
    if (app.Handle.Metadata.Version != "1.2.3+42") throw new Exception("Embedded app version: " + app.Handle.Metadata.Version);
    var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
    if (version != "1.2.3.42") throw new Exception("Assembly file version: " + version);
    if (OperatingSystem.IsWindows()) {
      var native = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
      if (native.FileMajorPart != 1 || native.FileMinorPart != 2 || native.FileBuildPart != 3 || native.FilePrivatePart != 42)
        throw new Exception("Native PE resource: " + native.FileVersion);
    }
    Console.WriteLine("PASS NativeAOT package.json version, C# metadata and Windows PE version when applicable");
  }
}`);
// Direct publish uses the raw path-valued configuration, not CLI normalization.
run(dotnet, ['publish', 'src-netwiv', '-c', 'Release', '-r', rid, '-p:PublishAot=true', '-p:TreatWarningsAsErrors=true', '--disable-build-servers', '-o', path.join(root, 'publish')], root);
run(path.join(root, 'publish', 'NetWiv.App' + (process.platform === 'win32' ? '.exe' : '')), [], root);
console.log('Native version verification passed.');
