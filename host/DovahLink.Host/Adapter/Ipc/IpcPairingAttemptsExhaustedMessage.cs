namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host to present a no-code terminal notification once the wrong-attempt hard limit has
/// cancelled the active pairing challenge. Best effort and unsolicited: the adapter sends no reply,
/// and a missing or unavailable adapter never blocks the client response that already reported the
/// outcome.
/// </summary>
/// <param name="CorrelationId">Always zero; this notification is unsolicited and expects no reply.</param>
public sealed record IpcPairingAttemptsExhaustedMessage(ulong CorrelationId) : IpcMessage(CorrelationId);
