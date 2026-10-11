import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

export function validateReviewedPort(port, sourceSha256, id) {
  if (port.upstreamSourceSha256 !== sourceSha256) throw new Error(`Re-review changed upstream test: ${id}`);
  if (!['business-logic', 'language-specification', 'outside-desktop-milestone'].includes(port.classification))
    throw new Error(`Invalid reviewed classification for ${id}`);
  if (!['ported', 'excluded', 'deferred'].includes(port.status)) throw new Error(`Invalid reviewed status for ${id}`);
  if (typeof port.note !== 'string' || !port.note.trim()) throw new Error(`Missing review rationale for ${id}`);
  if (port.status === 'ported' && (!Array.isArray(port.netwivEvidence) || !port.netwivEvidence.length))
    throw new Error(`Ported test has no executable evidence for ${id}`);
  if (port.status === 'excluded' && port.classification === 'business-logic')
    throw new Error(`Business logic cannot be excluded for ${id}`);
  if (port.status === 'deferred' && port.classification !== 'outside-desktop-milestone')
    throw new Error(`In-scope tests cannot be deferred for ${id}`);
  for (const evidence of port.netwivEvidence ?? [])
    if (![evidence.file, evidence.symbol, evidence.testCase].every(value => typeof value === 'string' && value.trim()))
      throw new Error(`Incomplete executable evidence for ${id}`);
}

export async function generateUpstreamTestInventory({ lock, root, generated, download, untarGzip }) {
  const repository = path.resolve(root, '..');
  const sha = value => createHash('sha256').update(value).digest('hex');
  const ports = JSON.parse(await readFile(path.join(root, 'upstream-test-ports.json'), 'utf8'));
  const sources = [
    { role: 'runtime', pin: lock.runtime, url: lock.runtime.archive, sha256: lock.runtime.archiveSha256,
      directories: ['crates/tauri/', 'crates/tauri-utils/', 'crates/tauri-runtime/', 'crates/tauri-runtime-wry/', 'crates/tauri-codegen/', 'crates/tauri-macros/', 'crates/tests/'] },
    { role: 'cli', pin: lock.cli, url: lock.cli.sourceArchive, sha256: lock.cli.sourceArchiveSha256,
      directories: ['crates/tauri-cli/', 'crates/tauri-bundler/'] },
    { role: 'build', pin: lock.build, url: lock.build.sourceArchive, sha256: lock.build.sourceArchiveSha256,
      directories: ['crates/tauri-build/', 'crates/tauri-plugin/'] }
  ];
  execFileSync('cargo', ['+1.95.0', 'build', '--quiet', '--locked', '--manifest-path',
    path.join(root, 'oracles/upstream-tests/Cargo.toml'), '--target-dir', path.join(repository, 'artifacts/upstream-test-inventory-target')],
    { cwd: repository, stdio: 'inherit' });
  const scanner = path.join(repository, 'artifacts/upstream-test-inventory-target/debug',
    process.platform === 'win32' ? 'netwiv-upstream-test-inventory.exe' : 'netwiv-upstream-test-inventory');
  const tests = [], macros = [], parseErrors = [], templateParsing = [], sourceRecords = [];
  const matchedPorts = new Set();
  for (const source of sources) {
    if (!/^[a-f0-9]{64}$/.test(source.sha256 ?? '')) throw new Error(`Missing source checksum for ${source.role}`);
    const cache = path.join(repository, 'artifacts', `upstream-${source.role}`);
    await mkdir(cache, { recursive: true });
    const archivePath = path.join(cache, 'source.tar.gz');
    let archive;
    try { archive = await readFile(archivePath); }
    catch (error) { if (error.code !== 'ENOENT') throw error; archive = await download(source.url); }
    if (sha(archive) !== source.sha256) throw new Error(`Upstream ${source.role} archive checksum mismatch`);
    await writeFile(archivePath, archive);
    const files = untarGzip(archive);
    const prefix = `tauri-${source.pin.commit}/`;
    const directory = path.join(cache, prefix);
    const selected = relative => source.directories.some(value => relative.startsWith(value));
    for (const [name, bytes] of files) {
      if (!name.startsWith(prefix) || !name.endsWith('.rs')) continue;
      const relative = name.slice(prefix.length);
      const target = path.join(directory, relative);
      if (path.relative(directory, target).startsWith('..')) throw new Error(`Unsafe archive entry: ${name}`);
      await mkdir(path.dirname(target), { recursive: true });
      await writeFile(target, bytes);
    }
    const scan = JSON.parse(execFileSync(scanner, [directory], { maxBuffer: 16 * 1024 * 1024, encoding: 'utf8' }));
    sourceRecords.push({ role: source.role, version: source.pin.version, commit: source.pin.commit,
      archive: source.url, archiveSha256: source.sha256, directories: source.directories });
    for (const test of scan.tests.filter(test => selected(test.file))) {
      const id = `${source.role}:${test.file}:${test.name}`;
      const bytes = files.get(prefix + test.file);
      if (!bytes) throw new Error(`Scanned file is outside verified archive: ${test.file}`);
      const sourceSha256 = sha(bytes);
      const port = ports.tests[id];
      if (port) {
        validateReviewedPort(port, sourceSha256, id);
        for (const evidence of port.netwivEvidence ?? []) {
          const contents = await readFile(path.join(repository, evidence.file), 'utf8');
          if (!contents.includes(evidence.symbol)) throw new Error(`Missing port evidence ${evidence.file}: ${evidence.symbol}`);
        }
        matchedPorts.add(id);
      }
      tests.push({ ...test, id, sourceRole: source.role, upstreamCommit: source.pin.commit, sourceSha256,
        ...(port ?? {}), status: port?.status ?? 'unreviewed' });
    }
    macros.push(...scan.macroReview.filter(value => selected(value.file)).map(value => ({ sourceRole: source.role, ...value })));
    parseErrors.push(...scan.parseErrors.filter(value => selected(value.file)).map(value => ({ sourceRole: source.role, ...value })));
    templateParsing.push(...(scan.templateParsing ?? []).filter(value => selected(value.file)).map(value => ({
      sourceRole: source.role, sourceSha256: sha(files.get(prefix + value.file)), ...value
    })));
  }
  for (const id of Object.keys(ports.tests)) if (!matchedPorts.has(id)) throw new Error(`Port refers to missing upstream test: ${id}`);
  tests.sort((a, b) => a.id.localeCompare(b.id));
  const counts = {};
  for (const test of tests) counts[test.status] = (counts[test.status] ?? 0) + 1;
  const report = { formatVersion: 1, sources: sourceRecords, tests, macroReview: macros, parseErrors, templateParsing,
    summary: { total: tests.length, statuses: counts, unresolvedParseErrors: parseErrors.length,
      complete: false, note: 'Ported source is not a recorded test run. Unreviewed tests, unaccounted macros and parse errors block a coverage parity claim.' } };
  await writeFile(path.join(generated, 'upstream-tests.json'), JSON.stringify(report, null, 2) + '\n');
  console.log(`Inventoried ${tests.length} pinned upstream tests: ${JSON.stringify(counts)}; ${parseErrors.length} source parse errors remain visible.`);
}
