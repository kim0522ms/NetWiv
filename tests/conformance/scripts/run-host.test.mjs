import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { runHost } from './run-host.mjs';

const row = value => `console.log('NETWIV_CONFORMANCE ' + ${JSON.stringify(JSON.stringify(value))});`;
const pass = name => row({type:'case', name, status:'pass', value:42}) + row({type:'complete', count:1});
const command = code => [process.execPath, '-e', code];

test('consumes complete functional records from subprocess stdout without files', async () => {
  const result = await runHost(command(pass('a')), ['a']);
  assert.equal(result.length, 2);
  assert.equal(result[0].value, 42);
});

test('preserves quoted executable arguments through the owned-host launcher', async () => {
  const value = 'space "quote" \\ trailing\\ 한글';
  const code = "console.log('NETWIV_CONFORMANCE '+JSON.stringify({type:'case',name:'argv',status:'pass',value:process.argv[1]}));" + row({type:'complete',count:1});
  const observed = await runHost([...command(code), value], ['argv']);
  assert.equal(observed[0].value, value);
});

test('rejects malformed and duplicate subprocess records', async () => {
  await assert.rejects(runHost(command("console.log('NETWIV_CONFORMANCE {');setInterval(()=>{},1000);"), ['a']), /Malformed/);
  await assert.rejects(runHost(command(row({type:'case',name:'a',status:'pass'}) + pass('a')), ['a']), /Duplicate/);
});

test('rejects zero cases, missing cases, nonzero exits and spawn errors', async () => {
  await assert.rejects(runHost(command(''), ['a']), /zero cases/);
  await assert.rejects(runHost(command(pass('a')), ['a','b']), /differs from source selection/);
  await assert.rejects(runHost(command(pass('a') + 'process.exitCode=7;'), ['a']), /exited with 7/);
  await assert.rejects(runHost(['netwiv-nonexistent-test-executable'], ['a']), /ENOENT/);
});

test('timeout terminates the subprocess and settles only after it closes', async () => {
  const started = Date.now();
  await assert.rejects(runHost(command("process.on('SIGTERM',()=>{});setInterval(()=>{},1000);"), ['a'], 100), /timed out/);
  assert.ok(Date.now() - started < 3000);
});

for (const exitParent of [true, false]) {
  test(`cleans owned stdout-inheriting descendants when parent ${exitParent ? 'exits' : 'stalls'}`, async () => {
    let ownedPid, cleanupNeeded = true;
    const write = process.stdout.write;
    process.stdout.write = function(chunk, ...args) {
      const match = /NETWIV_TEST_CHILD (\d+)/.exec(String(chunk));
      if (match) { ownedPid = Number(match[1]); return true; }
      return write.call(this, chunk, ...args);
    };
    const code = `const {spawn}=require('node:child_process');
      const child=spawn(process.execPath,['-e',"process.send('ready');setInterval(()=>{},1000);"],
        {detached:${process.platform === 'win32' && exitParent},stdio:['ignore',process.stdout,'ignore','ipc']});
      child.once('message',()=>{console.log('NETWIV_TEST_CHILD '+child.pid);${exitParent ? 'process.exit(0)' : 'setInterval(()=>{},1000)'}});`;
    try {
      const timeout = exitParent ? 5000 : process.platform === 'win32' ? 5000 : 250;
      await assert.rejects(runHost(command(code), ['a'], timeout), /zero cases|inherited stdout|timed out/);
      assert.ok(ownedPid);
      assert.throws(() => process.kill(ownedPid, 0), error => {
        if (error.code === 'ESRCH') cleanupNeeded = false;
        return error.code === 'ESRCH';
      });
    } finally {
      process.stdout.write = write;
      if (ownedPid && cleanupNeeded) {
        try { process.kill(ownedPid, 'SIGKILL'); } catch (error) { if (error.code !== 'ESRCH') throw error; }
      }
    }
  });
}

test('CLI runs the selected reference and candidate sequentially and compares in memory', () => {
  const name = 'app.website-data-store-sdk-lifecycle';
  const executable = fileURLToPath(new URL('./run-host.mjs', import.meta.url));
  const code = pass(name);
  const output = execFileSync(process.execPath, [executable, '--cases', name,
    '--reference', ...command(code), '--candidate', ...command(code)], {encoding:'utf8'});
  assert.match(output, /Matched 1 passing cases; 0 availability skips/);
  assert.match(output, /full gate not run/);
  assert.doesNotMatch(output, /NETWIV_CONFORMANCE|platform|architecture|Sha256/);
});
