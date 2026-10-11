import {readFile, writeFile, mkdir, access, readdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const [output = 'artifacts/compatibility/api-report.json', reference, candidate] = process.argv.slice(2);
if (Boolean(reference) !== Boolean(candidate)) throw Error('Supply both reference and NetWiv JSONL paths, or neither.');
const load = async file => JSON.parse(await readFile(path.resolve(root, file), 'utf8'));
const manifest = await load('compatibility/generated/api-manifest.json');
const evidence = await load('compatibility/implementation-evidence.json');
const entries = new Map();
for (const declaration of manifest.declarations) {
  // Root SourceFile entries repeat the module exports; retain each declaration once.
  if (declaration.module === '.') continue;
  const base = `${declaration.module}.${declaration.name}`;
  const add = (id, kind, signatures) => entries.set(id, {
    id, module:declaration.module, kind, signatures, implementation:'unreviewed',
    sources:[], fixtures:[], automatic:'not-run', gui:'not-recorded', limits:[]
  });
  add(base, declaration.kind, declaration.signatures || []);
  if (declaration.constructors?.length) add(`${base}.constructor`, 'constructor', declaration.constructors);
  for (const item of declaration.instanceMembers || []) add(`${base}.${item.name}`, 'instance-member', item.signatures);
  for (const item of declaration.staticMembers || []) add(`${base}.static.${item.name}`, 'static-member', item.signatures);
}
let run = null;
let passed = new Set();
if (reference) {
  execFileSync(process.execPath, [path.join(root,'tests/conformance/scripts/compare-results.mjs'), path.resolve(reference), path.resolve(candidate)], {stdio:'inherit'});
  const rows = (await readFile(path.resolve(candidate), 'utf8')).trim().split(/\r?\n/).map(JSON.parse);
  run = rows.find(row => row.type === 'run');
  const suiteHash = createHash('sha256');
  const files = [];
  const collect = async directory => {
    for (const entry of await readdir(path.join(root,directory), {withFileTypes:true})) {
      const name = `${directory}/${entry.name}`;
      if (entry.isDirectory()) await collect(name); else if(entry.isFile()) files.push(name);
    }
  };
  await collect('tests/conformance/frontend');
  for (const name of files.sort()) {
    const bytes = await readFile(path.join(root,name));
    suiteHash.update(name); suiteHash.update('\0');
    suiteHash.update(bytes.includes(0) ? bytes : Buffer.from(bytes.toString('utf8').replaceAll('\r\n','\n')));
    suiteHash.update('\0');
  }
  if (suiteHash.digest('hex') !== run.suiteSha256) throw Error('Results use a different frontend suite; do not apply current API evidence to stale fixtures.');
  passed = new Set(rows.filter(row => row.type === 'case' && row.status === 'pass').map(row => row.name));
}
for (const group of evidence.groups) {
  for (const source of group.sources) await access(path.join(root,source));
  for (const id of group.apis) {
    const entry = entries.get(id);
    if (!entry) throw Error(`Evidence references an unknown public API: ${id}`);
    entry.implementation = group.implementation || 'present';
    entry.sources.push(...group.sources);
    entry.fixtures.push(group.fixture);
    if (group.limits) entry.limits.push(group.limits);
  }
}
for (const entry of entries.values()) {
  entry.sources = [...new Set(entry.sources)];
  entry.fixtures = [...new Set(entry.fixtures)];
  entry.limits = [...new Set(entry.limits)];
  if (entry.fixtures.length && entry.fixtures.every(fixture => passed.has(fixture))) entry.automatic = 'matched-listed-fixtures';
}
const rows = [...entries.values()].sort((a,b)=>a.id.localeCompare(b.id,'en'));
const report = {
  profile:manifest.generatedFrom,
  interpretation:'Fixture coverage only. Neither source presence nor matched fixtures establish complete API compatibility. GUI status is independent. Unreviewed includes implemented but unaudited APIs.',
  run,
  summary:{inventory:rows.length, withSourceEvidence:rows.filter(row=>row.implementation!=='unreviewed').length, withMatchedFixtures:rows.filter(row=>row.automatic==='matched-listed-fixtures').length},
  knownGaps:evidence.knownGaps,
  entries:rows
};
const destination = path.resolve(output);
await mkdir(path.dirname(destination), {recursive:true});
await writeFile(destination,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({output:destination,...report.summary}));
