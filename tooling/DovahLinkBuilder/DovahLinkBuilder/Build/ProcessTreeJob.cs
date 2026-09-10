using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>
/// Tracks every process a single root process spawns, directly or transitively, through a Windows
/// Job Object. Membership is automatic: Windows assigns a newly created process to its parent's
/// job at creation time, and without an explicit breakaway limit -- which this wrapper never sets
/// -- a child cannot opt out. This reliably covers a detached descendant (for example one launched
/// with <c>start /b</c>) that <see cref="Process.Kill(bool)"/>'s own PID-snapshot tree walk can
/// otherwise miss or race, and needs no parent-PID discovery of its own: the OS already knows the
/// tree.
/// </summary>
public interface IProcessTreeJob : IDisposable
{
    /// <summary>Assigns <paramref name="process"/>, and every process it later spawns, to this job.</summary>
    /// <param name="process">The already-started root process to track.</param>
    /// <exception cref="Win32Exception">The process could not be assigned to this job.</exception>
    void Assign(Process process);

    /// <summary>Gets whether any process is currently active in this job.</summary>
    /// <exception cref="Win32Exception">This job's state could not be queried.</exception>
    bool HasActiveProcesses();

    /// <summary>Forcibly terminates every process currently active in this job.</summary>
    /// <exception cref="Win32Exception">This job's processes could not be terminated.</exception>
    void Terminate();
}

/// <inheritdoc cref="IProcessTreeJob"/>
public sealed class ProcessTreeJob : IProcessTreeJob
{
    /// <summary>Selects the extended-limit-information class for <see cref="SetInformationJobObject"/>.</summary>
    private const int JobObjectExtendedLimitInformation = 9;

    /// <summary>Selects the basic-accounting-information class for <see cref="QueryInformationJobObject"/>.</summary>
    private const int JobObjectBasicAccountingInformation = 1;

    /// <summary>Terminates every process in the job as soon as its last open handle closes.</summary>
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>The native job handle this wrapper owns.</summary>
    private readonly SafeJobObjectHandle handle;

    /// <summary>Creates a new, empty Job Object with kill-on-close armed.</summary>
    /// <exception cref="Win32Exception">The job could not be created or configured.</exception>
    public ProcessTreeJob()
    {
        handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new ExtendedLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose };
        int length = Marshal.SizeOf<ExtendedLimitInformation>();
        IntPtr limitsPointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(limits, limitsPointer, fDeleteOld: false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, limitsPointer, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(limitsPointer);
        }
    }

    /// <inheritdoc/>
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <inheritdoc/>
    public bool HasActiveProcesses()
    {
        int length = Marshal.SizeOf<BasicAccountingInformation>();
        IntPtr infoPointer = Marshal.AllocHGlobal(length);
        try
        {
            if (!QueryInformationJobObject(handle, JobObjectBasicAccountingInformation, infoPointer, (uint)length, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var info = Marshal.PtrToStructure<BasicAccountingInformation>(infoPointer);
            return info.ActiveProcesses > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPointer);
        }
    }

    /// <inheritdoc/>
    public void Terminate()
    {
        if (!TerminateJobObject(handle, exitCode: 1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();

    /// <summary>
    /// The subset of <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>'s fields this wrapper ever sets
    /// (only <see cref="LimitFlags"/>) still must be declared in full, matching layout: Windows
    /// validates <see cref="SetInformationJobObject"/>'s buffer against this info class's exact
    /// native size regardless of which fields are actually populated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long PerProcessUserTimeLimit;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long PerJobUserTimeLimit;

        /// <summary>The only field this wrapper actually sets: which limits below are active. Only <see cref="JobObjectLimitKillOnJobClose"/> is ever set.</summary>
        public uint LimitFlags;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr MinimumWorkingSetSize;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr MaximumWorkingSetSize;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint ActiveProcessLimit;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr Affinity;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint PriorityClass;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint SchedulingClass;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long ReadOperationCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long WriteOperationCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long OtherOperationCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long ReadTransferCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long WriteTransferCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long OtherTransferCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr ProcessMemoryLimit;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr JobMemoryLimit;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr PeakProcessMemoryUsed;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public UIntPtr PeakJobMemoryUsed;
    }

    /// <summary>The fields of <c>JOBOBJECT_BASIC_ACCOUNTING_INFORMATION</c> this wrapper reads via <see cref="QueryInformationJobObject"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccountingInformation
    {
        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long TotalUserTime;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long TotalKernelTime;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long ThisPeriodTotalUserTime;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public long ThisPeriodTotalKernelTime;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint TotalPageFaultCount;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint TotalProcesses;

        /// <summary>The only field this wrapper actually reads: the number of processes currently active in the job.</summary>
        public uint ActiveProcesses;

        /// <summary>Unused by this wrapper; present only to match the native struct's layout and size.</summary>
        public uint TotalTerminatedProcesses;
    }

    /// <summary>Creates a new, unnamed Job Object.</summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobObjectHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    /// <summary>Assigns a process to a Job Object.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobObjectHandle job, SafeProcessHandle process);

    /// <summary>Sets limit information on a Job Object.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobObjectHandle job, int informationClass, IntPtr information, uint informationLength);

    /// <summary>Queries information about a Job Object.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        SafeJobObjectHandle job, int informationClass, IntPtr information, uint informationLength, out uint returnLength);

    /// <summary>Terminates every process currently assigned to a Job Object.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeJobObjectHandle job, uint exitCode);
}
