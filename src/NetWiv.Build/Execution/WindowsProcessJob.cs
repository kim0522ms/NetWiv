using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetWiv.Build.Execution;

internal static partial class WindowsProcessJob
{
    internal static SafeFileHandle Attach(Process process)
    {
        var job = CreateJobObjectW(0, 0);
        try
        {
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (SetInformationJobObject(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()) == 0
                || AssignProcessToJobObject(job, process.SafeHandle) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot own the CLI child process tree.");
            return job;
        }
        catch { job.Dispose(); throw; }
    }

    internal static async Task StopAsync(SafeFileHandle job)
    {
        try
        {
            if (TerminateJobObject(job, 1) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                if (QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<BasicAccounting>(), out _) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                if (accounting.ActiveProcesses == 0) return;
                if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The CLI child process job did not finish terminating.");
                await Task.Delay(10);
            }
        }
        finally { job.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle CreateJobObjectW(nint attributes, nint name);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetInformationJobObject(SafeFileHandle job, int infoClass, in ExtendedLimits information, uint length);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int TerminateJobObject(SafeFileHandle job, uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int QueryInformationJobObject(SafeFileHandle job, int infoClass, out BasicAccounting information, uint length, out uint returnedLength);
}
