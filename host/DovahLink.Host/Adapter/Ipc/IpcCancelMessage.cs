namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by either side to cancel a previously sent request identified by <see cref="IpcMessage.CorrelationId"/>.
/// Cancelling a request that already completed or was already cancelled is a harmless no-op, not an
/// error, at this wire-contract layer.
/// </summary>
/// <param name="CorrelationId">The correlation id of the request being cancelled.</param>
public sealed record IpcCancelMessage(ulong CorrelationId) : IpcMessage(CorrelationId);
