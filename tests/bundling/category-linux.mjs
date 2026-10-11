import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

// Use an already-published NetWiv consumer. This checks package contents, not installation or GUI acceptance.
assert.equal(process.platform, 'linux', 'Run this check on the Linux package host');
const [cli, consumer] = process.argv.slice(2).map(value => path.resolve(value));
assert.ok(cli && consumer, 'Usage: node tests/bundling/category-linux.mjs <netwiv executable> <consumer directory>');
const output = execFileSync(cli, ['bundle', '--no-sign', '--bundles', 'deb', '--config', '{"bundle":{"category":"Developer Tool"}}'], { cwd: consumer, encoding: 'utf8' });
const artifact = output.match(/^Bundled deb: (.+)$/m)?.[1].trim();
assert.ok(artifact, output);
const staging = await mkdtemp(path.join(os.tmpdir(), 'netwiv-category-deb-'));
try {
  execFileSync('dpkg-deb', ['--extract', artifact, staging]);
  const applications = path.join(staging, 'usr/share/applications');
  const desktop = (await readdir(applications)).filter(file => file.endsWith('.desktop'));
  assert.equal(desktop.length, 1);
  const entry = await readFile(path.join(applications, desktop[0]), 'utf8');
  assert.match(entry, /^Categories=Development;$/m);
  console.log(`PASS: actual deb category metadata: ${artifact}`);
} finally {
  await rm(staging, { recursive: true, force: true });
}
try {
  execFileSync(cli, ['bundle', '--no-sign', '--bundles', 'deb', '--config', '{"bundle":{"category":"strategery"}}'], { cwd: consumer, encoding: 'utf8', stdio: 'pipe' });
  assert.fail('Misspelled category was accepted');
} catch (error) {
  assert.equal(error.status, 1);
  assert.match(error.stderr, /did you mean "Strategy Game"/);
  console.log('PASS: misspelled category returns CLI exit 1 with upstream suggestion');
}
