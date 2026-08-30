namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the adapter in response to <see cref="IpcResynchronizeRequestMessage"/>. Reports only
/// whether the adapter could service the request; the fresh baseline data itself is a later
/// concept's contract.
/// </summary>
/// <param name="CorrelationId">Matches the <see cref="IpcResynchronizeRequestMessage"/> this responds to.</param>
/// <param name="Accepted">Whether the adapter could capture and will deliver a fresh baseline.</param>
public sealed record IpcResynchronizeResultMessage(ulong CorrelationId, bool Accepted) : IpcMessage(CorrelationId);
