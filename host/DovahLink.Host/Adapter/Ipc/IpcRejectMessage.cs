namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by either side to report that a frame it could still safely decode carried an invalid
/// version, kind, identity, or payload. A frame that cannot be safely decoded at all is never
/// answered this way; the receiver closes immediately instead, per
/// <c>ai/context/protocol/security.md</c>'s "Failure behavior".
/// </summary>
/// <param name="CorrelationId">Matches the rejected message's correlation id, or zero if it had none.</param>
/// <param name="Reason">Why the message was rejected.</param>
public sealed record IpcRejectMessage(ulong CorrelationId, IpcRejectReason Reason) : IpcMessage(CorrelationId);
