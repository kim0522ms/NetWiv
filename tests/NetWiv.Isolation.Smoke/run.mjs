import { createHash } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { compare } from './compare.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const [referenceExe, candidateExe, directory] = process.argv.slice(2);
if (!referenceExe || !candidateExe || !directory) throw new Error('usage: run.mjs <reference executable> <NativeAOT candidate executable> <output directory>');
const output = path.resolve(directory);
await mkdir(output, { recursive: true });
async function sourceFingerprint() {
  const files = execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root })
    .toString('utf8').split('\0').filter(Boolean);
  const hash = createHash('sha256');
  for (const file of [...new Set(files)].sort()) {
    const bytes = await readFile(path.join(root, file));
    hash.update(file.replaceAll('\\', '/')); hash.update('\0');
    hash.update(bytes.includes(0) ? bytes : bytes.toString('utf8').replaceAll('\r\n', '\n')); hash.update('\0');
  }
  return hash.digest('hex');
}
const sourceSha256 = await sourceFingerprint();
const runs = {};
for (const [host, executable] of [['reference', referenceExe], ['netwiv', candidateExe]]) {
  const report = path.join(output, host + '.json');
  await writeFile(report, ''); // A stale report cannot satisfy this run.
  const child = spawn(path.resolve(executable), [], { stdio: 'inherit', env: { ...process.env, NETWIV_ISOLATION_REPORT: report } });
  const timer = setTimeout(() => child.kill(), 70000);
  let exitCode;
  try { exitCode = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); }); }
  finally { clearTimeout(timer); }
  runs[host] = { exitCode, cases: JSON.parse(await readFile(report, 'utf8')) };
}
const comparison = compare(runs.reference.cases, runs.netwiv.cases);
if (sourceSha256 !== await sourceFingerprint()) throw new Error('Source changed during execution; rebuild and rerun a stable pair.');
if (Object.values(runs).some(run => run.exitCode !== 0)) comparison.passed = false;
console.log(JSON.stringify(comparison, null, 2));
process.exitCode = comparison.passed ? 0 : 1;
