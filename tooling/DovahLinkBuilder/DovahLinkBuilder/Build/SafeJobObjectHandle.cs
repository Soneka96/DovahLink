using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DovahLink.DovahLinkBuilder.Build;

/// <summary>
/// Owns a native Windows Job Object handle, releasing it through <c>CloseHandle</c>. Used only by
/// <see cref="ProcessTreeJob"/>, which arms <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> on the job
/// this handle identifies -- so closing this handle, including from an unexpected exception
/// unwinding <see cref="ProcessTreeJob"/>'s own disposal, terminates every process still assigned
/// to it rather than merely stopping this wrapper from being able to see or touch it further.
/// </summary>
internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an invalid handle for a P/Invoke call to populate via <c>out</c>/return marshaling.</summary>
    public SafeJobObjectHandle()
        : base(ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle() => CloseHandle(handle);

    /// <summary>Closes a native Windows handle.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
