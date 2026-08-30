namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host to request that the adapter answer with a fresh current-state baseline. Carries
/// no payload of its own; the actual captured baseline is a later concept's contract.
/// </summary>
/// <param name="CorrelationId">Pairs this request with its <see cref="IpcResynchronizeResultMessage"/> response.</param>
public sealed record IpcResynchronizeRequestMessage(ulong CorrelationId) : IpcMessage(CorrelationId);
