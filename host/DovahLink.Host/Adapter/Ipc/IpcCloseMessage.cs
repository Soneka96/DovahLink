namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by either side to announce a deterministic close of the private channel. Decoding and
/// encoding this message is side-effect-free; a receiver may observe it more than once for the same
/// logical close without that being an error.
/// </summary>
/// <param name="CorrelationId">Always zero; a close is unsolicited and expects no reply.</param>
/// <param name="Reason">Why the sender is closing the channel.</param>
public sealed record IpcCloseMessage(ulong CorrelationId, IpcCloseReason Reason) : IpcMessage(CorrelationId);
