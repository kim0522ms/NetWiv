import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { compare } from './compare.mjs';
import { spawnOwnedHost, stopOwnedTree } from '../conformance/scripts/run-host.mjs';

export function runHost(command, host, timeoutMs = 70000, closeTimeoutMs = 2000) {
  return new Promise((resolve, reject) => {
    const child = spawnOwnedHost(command, {
      stdio: ['ignore', 'pipe', 'inherit'], detached: process.platform !== 'win32'
    });
    let pending = '', cases, failure, cleanupTimer, settled = false;
    const finish = (error, exitCode) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer); clearTimeout(cleanupTimer);
      if (error) reject(error);
      else if (!Array.isArray(cases) || !cases.length) reject(Error(`${host}: missing functional assertion result`));
      else resolve({ exitCode, cases });
    };
    const stop = error => {
      if (failure || settled) return;
      failure = error;
      stopOwnedTree(child);
      clearTimeout(cleanupTimer);
      cleanupTimer = setTimeout(() => {
        child.stdout.destroy();
        stopOwnedTree(child);
        finish(failure);
      }, closeTimeoutMs);
    };
    const timer = setTimeout(() => stop(Error(`${host}: isolation host timed out`)), timeoutMs);
    const line = value => {
      if (value.startsWith('NETWIV_HOST_SPAWN_ERROR ')) { stop(Error(value)); return; }
      if (!value.startsWith('ISOLATION_RESULT ')) { if (value) console.log(value); return; }
      try {
        if (cases !== undefined) throw Error(`${host}: duplicate result`);
        cases = JSON.parse(value.slice('ISOLATION_RESULT '.length));
      } catch (error) { stop(error); }
    };
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', text => {
      pending += text;
      let newline;
      while ((newline = pending.indexOf('\n')) !== -1) {
        line(pending.slice(0, newline).trimEnd());
        pending = pending.slice(newline + 1);
      }
    });
    child.once('error', error => finish(error));
    child.once('exit', () => {
      if (!failure && !settled) cleanupTimer = setTimeout(() =>
        stop(Error(`${host}: stdout remained open after host exit`)), closeTimeoutMs);
    });
    child.once('close', exitCode => {
      if (pending) line(pending.trimEnd());
      finish(failure ?? (exitCode !== 0 ? Error(`${host}: isolation host exited with ${exitCode}`) : null), exitCode);
    });
  });
}

async function main() {
  const [referenceExe, candidateExe, ...extra] = process.argv.slice(2);
  if (!referenceExe || !candidateExe || extra.length) throw Error('usage: run.mjs <reference executable> <candidate executable>');
  const reference = await runHost([path.resolve(referenceExe)], 'reference');
  const candidate = await runHost([path.resolve(candidateExe)], 'netwiv');
  const comparison = compare(reference.cases, candidate.cases);
  console.log(JSON.stringify(comparison, null, 2));
  process.exitCode = comparison.passed ? 0 : 1;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
