import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

if (!process.env.NETWIV_PACKAGE_VERSION) throw new Error('Set NETWIV_PACKAGE_VERSION to the actual package feed version.');
const project = fileURLToPath(new URL('../src-netwiv/', import.meta.url));
const intermediate = fileURLToPath(new URL('../src-netwiv/obj/bindings/', import.meta.url));
const output = fileURLToPath(new URL('../src-netwiv/bin/bindings/', import.meta.url));
const result = spawnSync(process.env.NETWIV_DOTNET ?? 'dotnet', ['build', project,
  '-p:NetWivBindingsOnly=true', '-p:PublishAot=false',
  `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`,
  `-p:OutputPath=${output}`, '--disable-build-servers'], { stdio: 'inherit', windowsHide: true });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
