import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { selectCases } from './select-cases.mjs';
import { compareResults, validateResults } from './compare-results.mjs';
import { startOriginServers } from './origin-server.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const prefix = 'NETWIV_CONFORMANCE ';

// Node cannot own a Windows process tree after its intermediate parents exit.
// The launcher assigns the suspended host to a kill-on-close job before any
// guest code runs. The job handle is never inherited by the host.
const windowsLauncher = String.raw`
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class NetWivOwnedHost {
  [StructLayout(LayoutKind.Sequential)] struct Limits {
    public long ProcessTime, JobTime;
    public uint Flags;
    public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
    public uint ActiveProcesses;
    public UIntPtr Affinity;
    public uint Priority, Scheduling;
  }
  [StructLayout(LayoutKind.Sequential)] struct JobLimits {
    public Limits Basic;
    public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Startup {
    public uint Size;
    public string Reserved, Desktop, Title;
    public uint X, Y, Width, Height, XCount, YCount, Fill, Flags;
    public ushort Show, ReservedSize;
    public IntPtr ReservedBytes, Input, Output, Error;
  }
  [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {
    public IntPtr Process, Thread;
    public uint ProcessId, ThreadId;
  }
  [DllImport("kernel32", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
  [DllImport("kernel32", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int info, ref JobLimits limits, uint length);
  [DllImport("kernel32", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
  [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo process);
  [DllImport("kernel32", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
  [DllImport("kernel32")] static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
  [DllImport("kernel32")] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
  [DllImport("kernel32")] static extern bool TerminateProcess(IntPtr process, uint code);
  [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32")] static extern IntPtr GetStdHandle(int handle);
  [DllImport("kernel32", SetLastError = true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
  static string Quote(string value) {
    var output = new StringBuilder("\"");
    int slashes = 0;
    foreach (char ch in value) {
      if (ch == '\\') { slashes++; continue; }
      output.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
      output.Append(ch); slashes = 0;
    }
    return output.Append('\\', slashes * 2).Append('"').ToString();
  }
  public static int Run(string[] arguments) {
    IntPtr job = CreateJobObject(IntPtr.Zero, null);
    var process = new ProcessInfo();
    if (job == IntPtr.Zero) throw new Win32Exception();
    try {
      var limits = new JobLimits(); limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
      if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobLimits)))) throw new Win32Exception();
      var startup = new Startup(); startup.Size = (uint)Marshal.SizeOf(typeof(Startup)); startup.Flags = 0x100;
      startup.Input = GetStdHandle(-10); startup.Output = GetStdHandle(-11); startup.Error = GetStdHandle(-12);
      foreach (IntPtr handle in new[] { startup.Input, startup.Output, startup.Error })
        if (!SetHandleInformation(handle, 1, 1)) throw new Win32Exception();
      var command = new StringBuilder();
      foreach (string argument in arguments) { if (command.Length != 0) command.Append(' '); command.Append(Quote(argument)); }
      if (!CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000004, IntPtr.Zero, null, ref startup, out process)) {
        int error = Marshal.GetLastWin32Error();
        Console.WriteLine("NETWIV_HOST_SPAWN_ERROR " + (error == 2 || error == 3 ? "ENOENT" : "Windows error " + error));
        return 127;
      }
      if (!AssignProcessToJobObject(job, process.Process)) { TerminateProcess(process.Process, 127); throw new Win32Exception(); }
      if (ResumeThread(process.Thread) == uint.MaxValue) throw new Win32Exception();
      if (WaitForSingleObject(process.Process, uint.MaxValue) != 0) throw new Win32Exception();
      uint code;
      if (!GetExitCodeProcess(process.Process, out code)) throw new Win32Exception();
      return unchecked((int)code);
    } finally {
      // Closing the job also terminates descendants whose original parents exited.
      CloseHandle(job);
      if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
      if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
    }
  }
}
'@
$arguments = [string[]](ConvertFrom-Json $env:NETWIV_HOST_COMMAND)
exit [NetWivOwnedHost]::Run($arguments)
`;

export function spawnOwnedHost(command, options) {
  if (process.platform === 'linux') return spawn('python3', [
    fileURLToPath(new URL('./linux-owned-host.py', import.meta.url)), ...command
  ], options);
  if (process.platform !== 'win32') return spawn(command[0], command.slice(1), options);
  return spawn('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand',
    Buffer.from(windowsLauncher, 'utf16le').toString('base64')], {
    ...options, windowsHide: true,
    env: { ...(options.env ?? process.env), NETWIV_HOST_COMMAND: JSON.stringify(command) }
  });
}

export function stopOwnedTree(child) {
  if (!child.pid) return;
  if (process.platform === 'linux') {
    // The subreaper must survive until all owned descendants have been waited.
    child.kill('SIGTERM');
  } else if (process.platform === 'win32') {
    // Kill the owned launcher handle; closing its job terminates the whole tree.
    child.kill('SIGKILL');
  } else {
    try { process.kill(-child.pid, 'SIGKILL'); } catch (error) { if (error.code !== 'ESRCH') child.kill('SIGKILL'); }
  }
}

export function runHost(command, expectedCases, timeoutMs = 65000) {
  return new Promise((resolve, reject) => {
    const values = [];
    let pending = '', failure, settlement, settled = false;
    const child = spawnOwnedHost(command, { detached: process.platform !== 'win32',
      windowsHide: true, stdio: ['ignore', 'pipe', 'inherit'], env: {
      ...process.env, NETWIV_CONFORMANCE_CASES: JSON.stringify(expectedCases),
      NETWIV_CONFORMANCE_ROOT: path.join(root, 'tests/conformance')
    } });
    const finish = (code, signal) => {
      if (settled) return;
      if (pending) { const value = pending; pending = ''; line(value); }
      settled = true;
      clearTimeout(timer); clearTimeout(settlement);
      if (failure) { reject(failure); return; }
      if (code !== 0) { reject(Error(`Conformance host exited with ${code ?? signal}`)); return; }
      try { validateResults(values, expectedCases); resolve(values); }
      catch (error) { reject(error); }
    };
    const stop = error => {
      failure ??= error;
      stopOwnedTree(child);
      settlement ??= setTimeout(() => { child.stdout.destroy(); finish(null, 'terminated'); }, 1500);
    };
    const timer = setTimeout(() => stop(Error('Conformance host timed out')), timeoutMs);
    const line = value => {
      if (value.startsWith('NETWIV_HOST_SPAWN_ERROR ')) { stop(Error(value)); return; }
      if (!value.startsWith(prefix)) { if (value) process.stdout.write(value + '\n'); return; }
      try { values.push(JSON.parse(value.slice(prefix.length))); }
      catch { stop(Error('Malformed conformance result')); }
    };
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', chunk => {
      pending += chunk;
      const lines = pending.split(/\r?\n/);
      pending = lines.pop();
      for (const value of lines) line(value);
    });
    child.once('error', error => { failure = error; finish(null, 'spawn-error'); });
    child.once('exit', (code, signal) => {
      stopOwnedTree(child);
      settlement ??= setTimeout(() => {
        failure ??= Error('Conformance host left inherited stdout open');
        child.stdout.destroy(); finish(code, signal);
      }, 1500);
    });
    child.once('close', finish);
  });
}

async function main(args) {
  const referenceIndex = args.indexOf('--reference'), candidateIndex = args.indexOf('--candidate');
  if (referenceIndex < 0 || candidateIndex <= referenceIndex + 1 || candidateIndex === args.length - 1)
    throw Error('usage: run-host.mjs [--cases name[,name...]] --reference <executable> [args...] --candidate <executable> [args...]');
  const source = await readFile(path.join(root, 'tests/conformance/frontend/suite.js'), 'utf8');
  const suiteCases = [...source.matchAll(/^await test\(['"]([^'"]+)['"]/gm)].map(match => match[1]);
  if (!suiteCases.length || new Set(suiteCases).size !== suiteCases.length ||
      [...source.matchAll(/^await test\(/gm)].length !== suiteCases.length)
    throw Error('Conformance suite must declare unique literal fixture names');
  const expectedCases = selectCases(suiteCases, args.slice(0, referenceIndex));
  const origins = expectedCases.includes('security.frame-origins')
    ? await startOriginServers(path.join(root, 'tests/conformance/dist')) : null;
  try {
    const reference = await runHost(args.slice(referenceIndex + 1, candidateIndex), expectedCases);
    const candidate = await runHost(args.slice(candidateIndex + 1), expectedCases);
    const result = compareResults(reference, candidate, expectedCases);
    console.log(`Matched ${result.passed} passing cases; ${result.skipped} availability skips` +
      (expectedCases.length < suiteCases.length ? ` (focused selection of ${suiteCases.length}; full gate not run)` : ''));
  } finally { await origins?.close(); }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  await main(process.argv.slice(2));
