namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Asks the adapter to perform one host-owned opaque sample read token.</summary>
/// <param name="CorrelationId">The nonzero request identity used for cancellation and diagnostics.</param>
/// <param name="SampleToken">The nonzero token the adapter maps at the final Skyrim boundary.</param>
public sealed record IpcReadSampleMessage(ulong CorrelationId, uint SampleToken) : IpcMessage(CorrelationId);
