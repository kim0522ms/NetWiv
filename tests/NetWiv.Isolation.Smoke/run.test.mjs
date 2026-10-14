import assert from 'node:assert/strict';
import test from 'node:test';
import { runHost } from './run.mjs';

const result = [{ name: 'functional assertion', scope: 'public', passed: true }];
const row = `console.log('ISOLATION_RESULT ' + ${JSON.stringify(JSON.stringify(result))});`;
const command = code => [process.execPath, '-e', code];

test('consumes a result even without a trailing newline', async () => {
  const observed = await runHost(command(`process.stdout.write('ISOLATION_RESULT ' + ${JSON.stringify(JSON.stringify(result))});`), 'fixture');
  assert.deepEqual(observed, { exitCode: 0, cases: result });
});

test('rejects malformed duplicate missing empty results and nonzero exits', async () => {
  for (const [code, pattern] of [
    ["console.log('ISOLATION_RESULT {');setInterval(()=>{},1000);", /JSON|Unexpected|Expected/],
    [row + row, /duplicate/],
    ['', /missing/],
    ["console.log('ISOLATION_RESULT []');", /missing/],
    [row + 'process.exitCode=7;', /exited with 7/]
  ]) await assert.rejects(runHost(command(code), 'fixture', 3000, 500), pattern);
  await assert.rejects(runHost(['netwiv-nonexistent-isolation-test'], 'fixture'), /ENOENT/);
});

async function descendant(mode) {
  let pid, cleanupNeeded = true;
  const original = console.log;
  console.log = value => {
    if (String(value).startsWith('isolation-child-pid:')) pid = Number(String(value).split(':')[1]);
    else original(value);
  };
  try {
    const code = `const {spawn}=require('node:child_process');
      const child=spawn(process.execPath,['-e','process.send("ready");setInterval(()=>{},1000)'],
        {detached:process.platform==='win32'&&${mode === 'exit'},stdio:['ignore',process.stdout,'ignore','ipc']});
      child.once('message',()=>{
        console.log('isolation-child-pid:'+child.pid);
        ${row}
        ${mode === 'exit' ? 'process.exit(0);' : 'setInterval(()=>{},1000);'}
      });`;
    const started = Date.now();
    const timeout = process.platform === 'win32' ? 5000 : 700;
    if (mode === 'exit' && process.platform === 'win32') {
      // The job closes as the host exits, so inherited stdout closes normally.
      assert.deepEqual(await runHost(command(code), 'fixture', timeout, 300), { exitCode: 0, cases: result });
    } else await assert.rejects(runHost(command(code), 'fixture', timeout, 300),
      mode === 'exit' ? /stdout remained open/ : /timed out/);
    assert.ok(Date.now() - started < (process.platform === 'win32' ? 7000 : 3000), 'Runner exceeded bounded cleanup time');
    assert.ok(Number.isInteger(pid) && pid > 0, 'Descendant did not start');
    {
      const deadline = Date.now() + 1000;
      while (Date.now() < deadline) {
        try { process.kill(pid, 0); }
        catch (error) { if (error.code === 'ESRCH') { cleanupNeeded = false; return; } throw error; }
        await new Promise(resolve => setTimeout(resolve, 20));
      }
      assert.fail('Owned descendant survived process tree termination');
    }
  } finally {
    console.log = original;
    if (pid && cleanupNeeded) {
      try { process.kill(pid, 'SIGKILL'); }
      catch (error) { if (error.code !== 'ESRCH') throw error; }
    }
  }
}

test('timeout terminates live-parent descendants and closes inherited stdout', () => descendant('timeout'));
test('exited parent closes owned descendants within a bounded grace period', () => descendant('exit'));
