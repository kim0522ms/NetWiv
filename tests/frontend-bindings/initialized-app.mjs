import assert from 'node:assert/strict';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

export async function checkInitializedApp({ consumer, dotnet, rid, version, run }) {
  const initialized = path.join(consumer, 'initialized');
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'init', '--ci', '--directory', initialized, '--app-name', 'InitializedStarter']);
  const projectFile = path.join(initialized, 'src-netwiv/NetWiv.App.csproj');
  const project = await readFile(projectFile, 'utf8');
  assert.ok(project.includes(`<NetWivVersion>${version}</NetWivVersion>`));
  assert.ok(project.includes('NetWiv.Platform.Desktop') && project.includes('NetWiv.Generators'));
  assert.ok(!project.includes('ProjectReference') && !project.includes('__'));
  const plugin = path.join(consumer, 'scaffolded-plugin');
  const pluginFeed = path.join(consumer, 'plugin-feed');
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'plugin', 'new', 'library-check', '--directory', plugin]);
  const pluginProject = await readFile(path.join(plugin, 'NetWiv.Plugin.csproj'), 'utf8');
  assert.ok(!pluginProject.includes('ProjectReference') && !pluginProject.includes('__'));
  assert.equal((pluginProject.match(new RegExp(`Version="${version.replaceAll('.', '\\.')}"`, 'g')) ?? []).length, 2);
  await run(dotnet, ['pack', plugin, '-c', 'Release', '-o', pluginFeed, '-p:TreatWarningsAsErrors=true', '--disable-build-servers']);
  const nugetFile = path.join(consumer, 'NuGet.Config');
  const escapedFeed = pluginFeed.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  await writeFile(nugetFile, (await readFile(nugetFile, 'utf8'))
    .replace('</packageSources>', `<add key="plugin-probe" value="${escapedFeed}" /></packageSources>`)
    .replace('</packageSourceMapping>', '<packageSource key="plugin-probe"><package pattern="NetWiv.Plugin.*" /></packageSource></packageSourceMapping>'));
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'add', 'library-check', '--version', '0.1.0-dev'], initialized);
  assert.ok((await readFile(projectFile, 'utf8')).includes('NetWiv.Plugin.LibraryCheck'));
  const pluginCapability = '.Capability(new("scaffold", ["library-check:default"], Windows: ["main"]))';
  const programFile = path.join(initialized, 'src-netwiv/Program.cs');
  await writeFile(programFile, (await readFile(programFile, 'utf8'))
    .replace('.InvokeHandler("greet")', '.InvokeHandler("greet").InvokeHandler("init_done").InvokeHandler("read_bundled").RegisterUriSchemeProtocol("nuget-probe", InitProbe.Protocol).Plugin(new NugetPermissionPlugin()).Capability(new("nuget", ["nuget:default"], Windows: ["main"]))' + pluginCapability) + `
public static class InitProbe {
  public static NetWiv.Runtime.AssetResponse Protocol(UriSchemeContext context, NetWiv.Runtime.AssetRequest request) =>
    new(200, "text/plain", "NuGet custom URI"u8.ToArray(), new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" });
  [Command] public static async Task<string> ReadBundled(AppHandle app) =>
    await File.ReadAllTextAsync(await PathApi.ResolveResourceAsync(app, "data/message.txt"));
  [Command] public static void InitDone(string result, AppHandle app) {
    Console.WriteLine(result);
    app.Dispatch(() => app.Exit(result == "PASS: init Wry + generated JavaScript + NativeAOT" ? 0 : 1));
  }
}
public sealed class NugetPermissionPlugin : IPlugin {
  public string Name => "nuget";
  public void Initialize(PluginContext context) {
    context.RegisterCommand("ping", (_, _, _) => ValueTask.FromResult(JsonValue.From("NuGet permission set")));
    context.Permission(new("allow-ping", "ping"));
    context.PermissionSet("default", "allow-ping");
  }
}
`);
  await writeFile(path.join(initialized, 'message.txt'), 'NuGet resource through NativeAOT and generated JavaScript');
  const configFile = path.join(initialized, 'src-netwiv/netwiv.conf.json');
  const config = JSON.parse(await readFile(configFile, 'utf8'));
  config.bundle ??= {};
  config.bundle.resources = { '../message.txt': 'data/message.txt' };
  await writeFile(configFile, JSON.stringify(config));
  const dist = path.join(initialized, 'dist');
  await mkdir(path.join(dist, 'vendor/external/tslib'), { recursive: true });
  const api = path.join(consumer, 'node_modules/@tauri-apps/api');
  await copyFile(path.join(api, 'core.js'), path.join(dist, 'vendor/core.js'));
  await copyFile(path.join(api, 'external/tslib/tslib.es6.js'), path.join(dist, 'vendor/external/tslib/tslib.es6.js'));
  await writeFile(path.join(dist, 'index.html'), `<!doctype html><html><body><output id="result">Starting</output>
<script type="importmap">{"imports":{"@tauri-apps/api/core":"./vendor/core.js"}}</script>
<script type="module">
import { commands } from './commands.js';
import { invoke, convertFileSrc } from '@tauri-apps/api/core';
try {
  const response = await commands.greet({ name: 'init' });
  if (response !== 'Hello, init!') throw new Error(JSON.stringify(response));
  if (await commands.read_bundled() !== 'NuGet resource through NativeAOT and generated JavaScript')
    throw new Error('Bundled resource did not reach the frontend');
  if (await invoke('plugin:nuget|ping') !== 'NuGet permission set') throw new Error('Plugin permission set failed');
  if (await invoke('plugin:library-check|ping') !== 'pong') throw new Error('Packaged generated plugin did not register');
  if (await (await fetch(convertFileSrc('probe', 'nuget-probe'))).text() !== 'NuGet custom URI') throw new Error('Custom URI handler failed');
  document.querySelector('#result').textContent = response;
  await commands.init_done({ result: 'PASS: init Wry + generated JavaScript + NativeAOT' });
} catch (error) { await commands.init_done({ result: 'FAIL: ' + error }); }
</script></body></html>`);
  await run(dotnet, ['build', 'src-netwiv', '-p:NetWivBindingsOnly=true', '--disable-build-servers'], initialized);
  await copyFile(path.join(initialized, 'src/generated/commands.js'), path.join(dist, 'commands.js'));
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'build', '--no-bundle', '--', '-p:TreatWarningsAsErrors=true'], initialized);
  const target = rid === 'win-x64' ? 'x86_64-pc-windows-msvc' : 'x86_64-unknown-linux-gnu';
  const executable = path.join(initialized, 'target', target, 'release', 'NetWiv.App' + (rid === 'win-x64' ? '.exe' : ''));
  const output = process.env.NETWIV_GUI_LAUNCHER
    ? await run('bash', [process.env.NETWIV_GUI_LAUNCHER, executable, process.env.NETWIV_GUI_BACKEND ?? 'x11'], initialized, 60000)
    : await run(executable, [], initialized, 60000);
  assert.ok(output.includes('PASS: init Wry + generated JavaScript + NativeAOT'));
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'remove', 'library-check'], initialized);
  assert.ok(!(await readFile(projectFile, 'utf8')).includes('NetWiv.Plugin.LibraryCheck'));
  await writeFile(programFile, (await readFile(programFile, 'utf8')).replace(pluginCapability, ''));
  const html = path.join(dist, 'index.html');
  await writeFile(html, (await readFile(html, 'utf8')).replace(
    "if (await invoke('plugin:library-check|ping') !== 'pong') throw new Error('Packaged generated plugin did not register');",
    "let removed = false; try { await invoke('plugin:library-check|ping'); } catch { removed = true; } if (!removed) throw new Error('Removed plugin is still registered');"));
  await run(dotnet, ['tool', 'run', 'netwiv', '--', 'build', '--no-bundle', '--', '-p:TreatWarningsAsErrors=true'], initialized);
  const removedOutput = process.env.NETWIV_GUI_LAUNCHER
    ? await run('bash', [process.env.NETWIV_GUI_LAUNCHER, executable, process.env.NETWIV_GUI_BACKEND ?? 'x11'], initialized, 60000)
    : await run(executable, [], initialized, 60000);
  assert.ok(removedOutput.includes('PASS: init Wry + generated JavaScript + NativeAOT'));
}
