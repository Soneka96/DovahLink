using DovahLink.Host.Client.Transport;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPublicConnectionContext"/> that records every call.</summary>
public sealed class FakePublicConnectionContext : IPublicConnectionContext
{
    /// <summary>The value <see cref="TrySend"/> returns; defaults to <see langword="true"/>.</summary>
    public bool TrySendResult { get; set; } = true;

    /// <summary>The value <see cref="TrySendSnapshot"/> returns; defaults to <see langword="true"/>.</summary>
    public bool TrySendSnapshotResult { get; set; } = true;

    /// <summary>Every payload/lane pair passed to <see cref="TrySend"/> so far, in call order.</summary>
    public List<(byte[] Payload, PublicOutboundLane Lane)> SentPayloads { get; } = [];

    /// <summary>Every area/payload pair passed to <see cref="TrySendSnapshot"/> so far, in call order.</summary>
    public List<(StateAreaId AreaId, byte[] Payload)> SentSnapshots { get; } = [];

    /// <summary>The number of times <see cref="RequestClose"/> has been called.</summary>
    public int RequestCloseCalls { get; private set; }

    /// <summary>
    /// Invoked synchronously by every <see cref="TrySend"/> call, once this call's own payload/lane is
    /// already recorded in <see cref="SentPayloads"/> but before it resolves -- lets a test inject work
    /// (for example raising a feed event, which may itself reentrantly call <see cref="TrySend"/>)
    /// exactly while a caller's send is admitting a message, to exercise a race the caller's own
    /// locking is meant to close, while keeping <see cref="SentPayloads"/> in the order each call
    /// actually arrived rather than the order its own hook-triggered reentrancy happened to resolve.
    /// </summary>
    public Action? OnTrySend { get; set; }

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload, PublicOutboundLane lane)
    {
        SentPayloads.Add((payload.ToArray(), lane));
        OnTrySend?.Invoke();
        return TrySendResult;
    }

    /// <inheritdoc/>
    public bool TrySendSnapshot(StateAreaId areaId, ReadOnlyMemory<byte> payload)
    {
        SentSnapshots.Add((areaId, payload.ToArray()));
        return TrySendSnapshotResult;
    }

    /// <inheritdoc/>
    public void RequestClose() => RequestCloseCalls++;

    /// <summary>The value <see cref="RemainingOutboundCapacity"/> returns for every lane; defaults to <see cref="int.MaxValue"/>.</summary>
    public int RemainingOutboundCapacityResult { get; set; } = int.MaxValue;

    /// <inheritdoc/>
    public int RemainingOutboundCapacity(PublicOutboundLane lane) => RemainingOutboundCapacityResult;
}
