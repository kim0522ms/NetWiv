import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

// Functional fixture assertions execute through the unchanged guest invoke API.
export function polymorphismGuestChecks(route, quotedNumbers = false) {
  return `
  {
    const call = commands[${JSON.stringify(route)}];
    const text = await call({ value: { kind: 'text', name: 'fixture', text: 'hello', next: { kind: -7, name: 'nested', count: 3 } } });
    if (text.kind !== 'text' || text.name !== 'fixture' || text.text !== 'hello' || text.next.kind !== -7 || text.next.count !== ${quotedNumbers ? "'3'" : '3'})
      throw new Error('Polymorphic derived JSON did not roundtrip');
    if (await call({ value: null }) !== null) throw new Error('Polymorphic nullable root failed');
    for (const value of [
      { kind: 'text', name: 'fixture' }, { kind: 'text', name: 'fixture', text: null },
      { kind: -7, name: 'fixture', count: 1.5 }, { kind: -7, name: 'fixture', count: null },
      { kind: 'unknown', name: 'fixture' }, { kind: true, name: 'fixture' },
      { kind: 'text', name: 'fixture', text: 'hello', next: { kind: -7, name: 'nested', count: null } }
    ]) {
      let rejected = false;
      try { await call({ value }); } catch (error) { rejected = String(error).includes('invalid args'); }
      if (!rejected) throw new Error('Invalid polymorphic input bypassed command validation');
    }
    const count = await call({ value: { kind: -7, name: 'fixture', count: 4 } });
    if (count.kind !== -7 || count.count !== ${quotedNumbers ? "'4'" : '4'}) throw new Error('Valid call after polymorphic rejection failed');
  }
`;
}

export const polymorphismCalls = `
type PolyInput = import('./src/generated/commands.js').PolyWireInput;
type PolyOutput = import('./src/generated/commands.js').PolyWireOutput;
const polyInput: PolyInput = { kind: 'text', name: 'fixture', text: 'hello', next: null };
const polyOutput: PolyOutput | null = await commands.polymorphic_profile({ value: polyInput });
await commands.polymorphic_profile({ value: { kind: -7, name: 'fixture', count: 3 } });
await commands.polymorphic_profile({ value: null });
if (polyOutput?.kind === 'text') { const text: string = polyOutput.text; void text; }
if (polyOutput?.kind === -7) { const count: number = polyOutput.count; void count; }
// @ts-expect-error The text variant requires its derived property.
commands.polymorphic_profile({ value: { kind: 'text', name: 'fixture' } });
// @ts-expect-error A discriminator is mandatory for an abstract root.
commands.polymorphic_profile({ value: { name: 'fixture', text: 'hello' } });
// @ts-expect-error Unknown discriminators have no supported variant.
commands.polymorphic_profile({ value: { kind: 'unknown', name: 'fixture' } });
// @ts-expect-error Integer and string discriminators are distinct.
commands.polymorphic_profile({ value: { kind: '-7', name: 'fixture', count: 3 } });
// @ts-expect-error Derived non-nullable properties reject null.
commands.polymorphic_profile({ value: { kind: 'text', name: 'fixture', text: null } });
`;

export async function configurePolymorphism(consumer) {
  const source = await readFile(new URL('./PolymorphicWire.cs', import.meta.url), 'utf8');
  const file = path.join(consumer, 'src-netwiv/PolymorphicWire.cs');
  await writeFile(file, source);
  const programFile = path.join(consumer, 'src-netwiv/Program.cs');
  await writeFile(programFile, (await readFile(programFile, 'utf8'))
    .replace('builder.InvokeHandler("smoke_done")', 'builder.InvokeHandler("smoke_done").InvokeHandler("polymorphic_profile")')
    .replace('[JsonSerializable(typeof(Greeting))]', '[JsonSerializable(typeof(Greeting))]\n[JsonSerializable(typeof(PolyWire))]'));
  const reactFile = path.join(consumer, 'src/App.jsx');
  await writeFile(reactFile, (await readFile(reactFile, 'utf8'))
    .replace('await commands.smoke_done({ result:', polymorphismGuestChecks('polymorphic_profile') + '\nawait commands.smoke_done({ result:'));
  return { file, source };
}

export async function addReferencedPolymorphism(pluginFile) {
  const source = (await readFile(new URL('./PolymorphicWire.cs', import.meta.url), 'utf8'))
    .replace(/^using .*;\r?\n/gm, '').replaceAll('PolyWire', 'PackagePolyWire')
    .replaceAll('PolymorphicProfile', 'PackagePolymorphicProfile');
  await writeFile(pluginFile, (await readFile(pluginFile, 'utf8'))
    .replace('[JsonSerializable(typeof(PackageProfile))]', '[JsonSerializable(typeof(PackageProfile))]\n[JsonSerializable(typeof(PackagePolyWire))]') + '\n' + source);
}

export const referencedPolymorphismCalls = `
const poly = await commands.package_polymorphic_profile({ value: { kind: -7, name: 'fixture', count: '3' } });
if (poly?.kind === -7) { const count: string = poly.count; void count; }
await commands.package_polymorphic_profile({ value: { kind: 'text', name: 'fixture', text: 'hello' } });
// @ts-expect-error Producer's numeric output policy remains in force for variants.
if (poly?.kind === -7) { const count: number = poly.count; void count; }
// @ts-expect-error Referenced variants require their derived properties too.
commands.package_polymorphic_profile({ value: { kind: 'text', name: 'fixture' } });
`;
