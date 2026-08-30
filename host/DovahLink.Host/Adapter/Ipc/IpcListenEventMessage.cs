namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Asks the adapter to listen for one host-owned opaque event key.</summary>
/// <param name="CorrelationId">The nonzero request identity used for cancellation and diagnostics.</param>
/// <param name="EventKey">The nonzero key the adapter maps at the final Skyrim boundary.</param>
public sealed record IpcListenEventMessage(ulong CorrelationId, uint EventKey) : IpcMessage(CorrelationId);
