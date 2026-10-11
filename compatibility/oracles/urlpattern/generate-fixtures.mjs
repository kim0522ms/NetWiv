// Maintainer-only regeneration; requires the explicit Rust oracle build.
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';

const [oracle, upstreamData] = process.argv.slice(2);
if (!oracle || !upstreamData) throw new Error('Usage: node generate-fixtures.mjs <oracle-executable> <urlpattern-0.3.0/src/testdata/urlpatterntestdata.json>');
const urls = ['https://example.com/', 'https://example.com/a', 'https://example.com/a/b', 'https://example.com/a?role=admin#ok',
  'https://example.com/a?role=user#bad', 'http://example.com/a', 'https://example.com:8443/a', 'https://sub.example.com/a',
  'https://a.b.example.com/a', 'https://example.com.evil/a', 'https://user:pass@example.com/a', 'https://example.com/%61',
  'https://example.com/b', 'https://example.com/A', 'https://example.com/42', 'https://example.com/a.txt'];
const patterns = ['https://example.com', 'https://example.com/', 'https://example.com/*', 'https://example.com/a',
  'https://example.com/a?role=admin', 'https://example.com/a#ok', 'https://example.com/a?role=admin#ok',
  'https://example.com/a?', 'https://example.com/a#', 'https://example.com/a?#', '*://example.com/*',
  'https://*.example.com/*', 'https://:sub.example.com/*', 'https://example.com:*/*', 'https://example.com:443/*',
  'https://example.com:8443/*', 'https://example.com/:id', 'https://example.com/:id?', 'https://example.com/:id+',
  'https://example.com/:id*', 'https://example.com/{a}?', 'https://example.com/{a}+', 'https://example.com/{a}*',
  'https://example.com/:id(\\d+)', 'https://example.com/(a|b)', 'https://example.com/{:id}.txt',
  'https://user:pass@example.com/*', 'https://user\\:pass@example.com/*', 'https://example.com/%61',
  'https://EXAMPLE.COM/*', 'HTTPS://EXAMPLE.COM:443/*', 'https://example.com/a/../b', 'https://example.com/a/%2e%2e/b',
  'https://example.com/:id/:id', 'https://example.com/(', 'https://example.com/:', '/relative', '*',
  'https://example.com/((a))', 'https://example.com/(a(?=b))', 'https://example.com/(a)\\1',
  'https://example.com/{a', 'https://example.com/a}', 'https://example.com/\\', 'https://example.com:99999/*',
  'https://example.com/([a-z&&a-c]+)', 'https://example.com/([a-z--b-z]+)', 'https://example.com/([a[b]c]+)',
  'https://example.com/([a-z~~b-z]+)', 'https://example.com/(\\x{61})', 'https://example.com/(\\u0061)',
  'https://example.com/(\\p{ASCII}+)', 'https://example.com/(a\\Z)', 'https://example.com/(a(?:b)?)',
  '*://example.com/:id\\?role=admin#approved', '*://example.com/:id?role=admin#approved',
  'https://example.com/([[:alpha:]]+)', 'https://example.com/([[:^alpha:]]+)', 'https://example.com/([[:digit:]]+)',
  'https://example.com/(\\p{IsBasicLatin}+)', 'https://example.com/([\\b])'];
const rows = patterns.map(pattern => ({pattern, urls}));
// Capability boundaries beyond the constructor/path corpus: canonical hosts,
// credentials, default ports, escaped separators and optional/repeated groups.
const boundaryUrls = [
  'https://example.com/', 'https://EXAMPLE.com:443/a', 'https://example.com:8443/a',
  'http://example.com/a', 'https://example.com.evil/a', 'https://sub.example.com/a',
  'https://example.com./a', 'https://user:pass@example.com/a', 'https://user:wrong@example.com/a',
  'https://example.com@evil.test/a', 'https://evil.test@example.com/a',
  'https://example.com/a%2Fb', 'https://example.com/a/b', 'https://example.com/a/../b',
  'https://example.com/%2e%2e/b', 'https://example.com/a?role=admin#approved',
  'https://example.com/a?role=user#approved', 'https://example.com/a?role=admin#other',
  'https://example.com/a?role=admin&extra=true#approved',
  'https://example.com:99999/a', 'https://[::1]/a', 'not an absolute URL',
  'https://127.0.0.1/a', 'https://127.1/a', 'https://0x7f000001/a',
  'https://例え.テスト/a', 'https://xn--r8jz45g.xn--zckzah/a',
];
for (const protocol of ['http', 'https', '*', '(https?)'])
  for (const host of ['example.com', '*.example.com', ':sub.example.com', '127.0.0.1', '例え.テスト'])
    for (const port of ['', ':*', ':443', ':8443'])
      rows.push({pattern: `${protocol}://${host}${port}/*`, urls: boundaryUrls});
for (const pattern of [
  'https://example.com/:id\\?role=admin#approved',
  'https://example.com/{:id}*', 'https://example.com/{:id}+',
  'https://user\\:pass@example.com/*', 'https://:user\\::password@example.com/*',
  'https://example.com./*', 'https://example.com/:a/:b?',
  'https://example.com/+literal', 'https://example.com:65536/*',
  'https://example.com/(a(?:b|c)+)',
]) rows.push({pattern, urls: boundaryUrls});
rows.push(...[
  ['https://例え.テスト/*', ['https://例え.テスト/a', 'https://xn--r8jz45g.xn--zckzah/a', 'https://example.com/a']],
  ['https://example.com/한글', ['https://example.com/한글', 'https://example.com/%ED%95%9C%EA%B8%80']],
  ['https://example.com/:한글', ['https://example.com/한글', 'https://example.com/42']],
  ['http://[\\:\\:1]/*', ['http://[::1]/a', 'http://[::2]/a']],
  ['http://127.1/*', ['http://127.0.0.1/a', 'http://127.1/a']],
  ['tauri://localhost/:id', ['tauri://localhost/a/b', 'tauri://localhost/a', 'tauri://localhost/']],
  ['data\\:text/*', ['data:text/plain,hello', 'data:image/png,test']],
].map(([pattern, urls]) => ({pattern, urls})));
const dataBytes = readFileSync(upstreamData);
for (const row of JSON.parse(dataBytes)) {
  if (row.pattern?.length === 1 && typeof row.pattern[0] === 'string') {
    rows.push({pattern: row.pattern[0], urls: typeof row.inputs?.[0] === 'string' && row.inputs.length === 1 ? [row.inputs[0]] : urls});
  }
  else if (row.pattern?.length === 1 && row.pattern[0] && typeof row.pattern[0] === 'object'
    && Object.keys(row.pattern[0]).length === 1 && typeof row.pattern[0].pathname === 'string') {
    const inputs = typeof row.inputs?.[0] === 'string' ? [row.inputs[0]]
      : typeof row.inputs?.[0]?.pathname === 'string' ? ['https://example.com' + row.inputs[0].pathname] : urls;
    rows.push({pattern: 'https://example.com' + row.pattern[0].pathname, urls: inputs});
  }
}
const result = spawnSync(resolve(oracle), [], {input: rows.map(row => JSON.stringify(row)).join('\n') + '\n', encoding: 'utf8', maxBuffer: 16 * 1024 * 1024});
if (result.status !== 0) throw new Error(result.stderr);
const fixture = {
  source: 'urlpattern 0.3.0 with tauri-utils 2.9.3 RemoteUrlPattern overrides',
  upstreamDataSha256: createHash('sha256').update(dataBytes).digest('hex'),
  oracleLockSha256: createHash('sha256').update(readFileSync(new URL('Cargo.lock', import.meta.url))).digest('hex'),
  cases: result.stdout.trim().split('\n').map(row => JSON.parse(row))
};
writeFileSync('tests/NetWiv.Core.Tests/fixtures/remote-url-pattern-oracle.json', JSON.stringify(fixture, null, 2) + '\n');
console.log(`${fixture.cases.length} patterns, ${fixture.cases.reduce((n, row) => n + (row.matches?.length ?? 0), 0)} URL decisions`);
