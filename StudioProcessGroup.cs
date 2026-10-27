using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Max;

// Windows closes the job handle even if Studio is forcefully terminated. Every
// assigned runner then stops, including runners whose own timeout cannot fire.
internal sealed class StudioProcessGroup : IDisposable
{
    private const uint KillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformation = 9;
    private readonly SafeJobHandle? job;

    internal StudioProcessGroup()
    {
        if (!OperatingSystem.IsWindows()) return;
        IntPtr handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Max Studio could not create its process group.");
        var created = new SafeJobHandle(handle);
        try
        {
            var limits = new JobExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
            if (!SetInformationJobObject(created, ExtendedLimitInformation, ref limits,
                (uint)Marshal.SizeOf<JobExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Max Studio could not configure its process group.");
            job = created;
        }
        catch { created.Dispose(); throw; }
    }

    internal void AssignProcess(Process process)
    {
        if (job == null) return;
        if (job.IsClosed) throw new ObjectDisposedException(nameof(StudioProcessGroup));
        if (!AssignProcessToJobObject(job, process.SafeHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Max Studio could not protect its runner process. The program was stopped.");
    }

    public void Dispose() => job?.Dispose();

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeJobHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        internal JobBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass,
        ref JobExtendedLimitInformation information, uint informationLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
