import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const inventory = JSON.parse(await readFile(path.join(root, 'compatibility/generated/upstream-tests.json'), 'utf8'));
const file = 'crates/tauri-utils/src/resources.rs';
const entry = inventory.tests.find(test => test.id === `runtime:${file}:tests::resource_paths_errors`);
if (!entry) throw Error('Pinned resource tests are missing from the upstream inventory');
const source = await readFile(path.join(root, process.argv[2] ?? `artifacts/upstream-runtime/tauri-${entry.sourceCommit ?? inventory.sources.find(source => source.role === 'runtime').commit}/${file}`));
const hash = createHash('sha256').update(source).digest('hex');
if (hash !== entry.sourceSha256) throw Error('Resource fixture source differs from the pinned inventory');
const text = source.toString('utf8');
const strings = block => [...block.matchAll(/"([^"\\]*)"/g)].map(match => match[1]);
const pairs = block => [...block.matchAll(/\(\s*"([^"\\]*)",\s*"([^"\\]*)",?\s*\)/g)].map(match => [match[1], match[2]]);
const files = strings(text.match(/let paths = \[([\s\S]*?)\];/)[1]);
const cases = [];
for (const name of ['resource_paths_iter_slice_allow_walk', 'resource_paths_iter_slice_no_walk', 'resource_paths_iter_map_allow_walk', 'resource_paths_iter_map_no_walk', 'resource_paths_errors']) {
  const block = text.slice(text.indexOf(`fn ${name}()`)).split(/\n  #\[test\]/)[0];
  const isMap = block.includes('ResourcePaths::from_map');
  const input = isMap ? Object.fromEntries(pairs(block.match(/&resources_map\(&\[([\s\S]*?)\]\)/)[1])) :
    [...block.match(/ResourcePaths::new\(\s*&\[([\s\S]*?)\],/)[1].matchAll(/"([^"\\]*)"\.into\(\)/g)].map(match => match[1]);
  const expected = block.match(/expected_resources\(&\[([\s\S]*?)\]\)/);
  cases.push({name, allowWalk:name.endsWith('allow_walk'), input,
    expected: expected ? pairs(expected[1]) : [], errorsOnly:name === 'resource_paths_errors'});
}
if (files.length !== 26 || cases.some(test => Object.keys(test.input).length === 0 || !test.errorsOnly && test.expected.length === 0))
  throw Error('Pinned resource fixture extraction is incomplete; review the upstream syntax');
const output = path.join(root, 'tests/NetWiv.Cli.Tests/fixtures/upstream-resources.json');
await mkdir(path.dirname(output), {recursive:true});
await writeFile(output, JSON.stringify({notice:'Copyright 2019-2024 Tauri Programme within The Commons Conservancy; Apache-2.0 OR MIT',
  source:file, sourceSha256:hash, files, cases}, null, 2) + '\n');
console.log(`Extracted ${cases.length} source-verified resource cases with ${files.length} fixture files`);
