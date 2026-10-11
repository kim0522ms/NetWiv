import { spawnSync } from 'node:child_process';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const project = fileURLToPath(new URL('../src-netwiv/', import.meta.url));
const intermediate = fileURLToPath(new URL('../src-netwiv/obj/bindings/', import.meta.url));
const output = fileURLToPath(new URL('../src-netwiv/bin/bindings/', import.meta.url));
// Isolate the inner build's restore and outputs from the IDE / NativeAOT build.
// MSBuildProjectExtensionsPath must be absolute for NuGet-generated imports.
const result = spawnSync('dotnet', ['build', project,
  '-p:NetWivBindingsOnly=true', '-p:PublishAot=false',
  `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`,
  `-p:OutputPath=${output}`, '--disable-build-servers'], { stdio: 'inherit', windowsHide: true });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
