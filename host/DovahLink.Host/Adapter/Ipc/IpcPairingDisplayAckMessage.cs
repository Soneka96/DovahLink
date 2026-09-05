namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter in response to <see cref="IpcPairingDisplayMessage"/>, reporting only whether
/// its Skyrim-facing display seam accepted the request. Initial display and manual redisplay gate a
/// public outcome on this acknowledgement; wrong-code automatic redisplay is best effort and its
/// caller may discard this result.
/// </summary>
/// <param name="CorrelationId">Matches the <see cref="IpcPairingDisplayMessage"/> this responds to.</param>
/// <param name="Accepted">Whether the adapter's display seam accepted and presented the code.</param>
public sealed record IpcPairingDisplayAckMessage(ulong CorrelationId, bool Accepted) : IpcMessage(CorrelationId);
