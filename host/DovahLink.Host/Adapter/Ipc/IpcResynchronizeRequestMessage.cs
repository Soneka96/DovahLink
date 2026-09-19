namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// Sent by the host with the bounded event registrations and baseline samples the adapter must
/// execute to establish a fresh current-state baseline.
/// </summary>
/// <param name="CorrelationId">Pairs this request with its <see cref="IpcResynchronizeResultMessage"/> response.</param>
/// <param name="PersistentEventKeys">The ordered event keys to register before sampling.</param>
/// <param name="BaselineSampleTokens">The ordered baseline sample tokens to capture after event registration.</param>
/// <remarks>
/// The Host de-duplicates each list in first-seen order; the codec preserves the order it receives,
/// including duplicates.
/// </remarks>
public sealed record IpcResynchronizeRequestMessage(
    ulong CorrelationId,
    IReadOnlyList<uint> PersistentEventKeys,
    IReadOnlyList<uint> BaselineSampleTokens) : IpcMessage(CorrelationId)
{
    /// <summary>Creates a valid no-op resynchronization request.</summary>
    /// <param name="correlationId">Pairs this request with its result response.</param>
    public IpcResynchronizeRequestMessage(ulong correlationId) : this(correlationId, [], [])
    {
    }
}
