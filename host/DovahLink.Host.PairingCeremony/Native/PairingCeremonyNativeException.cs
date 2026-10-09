namespace DovahLink.Host.PairingCeremony.Native;

/// <summary>
/// A native operation failed, classified for the fail-closed owner. It carries only an operation
/// name and a status name: never a handle, pointer, path, key, or other secret value.
/// </summary>
internal sealed class PairingCeremonyNativeException : Exception
{
    /// <summary>Creates a classified native failure.</summary>
    /// <param name="kind">The fail-closed classification.</param>
    /// <param name="operation">The native operation that failed.</param>
    /// <param name="statusName">The native status name, or a short description.</param>
    /// <param name="innerException">The original package exception, if any.</param>
    public PairingCeremonyNativeException(NativeFailureKind kind, string operation, string statusName, Exception? innerException = null)
        : base($"{operation} failed: {statusName}.", innerException)
    {
        Kind = kind;
        Operation = operation;
        StatusName = statusName;
    }

    /// <summary>The fail-closed classification.</summary>
    public NativeFailureKind Kind { get; }

    /// <summary>The native operation that failed.</summary>
    public string Operation { get; }

    /// <summary>The native status name, or a short description.</summary>
    public string StatusName { get; }
}
