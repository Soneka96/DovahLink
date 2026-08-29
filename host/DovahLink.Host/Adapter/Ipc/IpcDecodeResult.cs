namespace DovahLink.Host.Adapter.Ipc;

/// <summary>The result of decoding one private IPC frame's header and payload bytes.</summary>
/// <param name="Message">The decoded message when decoding succeeded; otherwise <see langword="null"/>.</param>
/// <param name="FailureReason">Why decoding failed when it did not succeed; otherwise <see langword="null"/>.</param>
public sealed record IpcDecodeResult(IpcMessage? Message, IpcRejectReason? FailureReason)
{
    /// <summary>Creates a successful result carrying the decoded message.</summary>
    /// <param name="message">The decoded message.</param>
    public static IpcDecodeResult Success(IpcMessage message) => new(message, FailureReason: null);

    /// <summary>Creates a failed result carrying the fail-closed reason.</summary>
    /// <param name="reason">Why decoding failed.</param>
    public static IpcDecodeResult Failure(IpcRejectReason reason) => new(Message: null, reason);
}
