using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IResynchronizationTransactionCoordinator"/> that records every call.</summary>
public sealed class FakeResynchronizationTransactionCoordinator : IResynchronizationTransactionCoordinator
{
    /// <summary>The token <see cref="AcquireToken"/> returns. <see langword="null"/> by default, matching a stale or unclaimable tuple.</summary>
    public IAdapterResynchronizationToken? AcquireTokenResult { get; set; }

    /// <summary>Every call to <see cref="AcquireToken"/>, in call order.</summary>
    public List<(AdapterInstanceId InstanceId, long ConnectionGeneration, PlayContextId PlayContextId, long PlayContextGeneration)> AcquireTokenCalls { get; } = [];

    /// <summary>Every call to <see cref="RecordAreaAccepted"/>, in call order.</summary>
    public List<(StateAreaId AreaId, AdapterInstanceId InstanceId, long ConnectionGeneration, PlayContextId PlayContextId, long PlayContextGeneration)> RecordAreaAcceptedCalls { get; } = [];

    /// <summary>Every call to <see cref="RecordAdapterPlanAccepted"/>, in call order.</summary>
    public List<(bool Accepted, AdapterInstanceId InstanceId, long ConnectionGeneration, PlayContextId PlayContextId, long PlayContextGeneration)> RecordAdapterPlanAcceptedCalls { get; } = [];

    /// <inheritdoc/>
    public IAdapterResynchronizationToken? AcquireToken(AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration)
    {
        AcquireTokenCalls.Add((instanceId, connectionGeneration, playContextId, playContextGeneration));
        return AcquireTokenResult;
    }

    /// <inheritdoc/>
    public void RecordAreaAccepted(StateAreaId areaId, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration) =>
        RecordAreaAcceptedCalls.Add((areaId, instanceId, connectionGeneration, playContextId, playContextGeneration));

    /// <inheritdoc/>
    public void RecordAdapterPlanAccepted(bool accepted, AdapterInstanceId instanceId, long connectionGeneration, PlayContextId playContextId, long playContextGeneration) =>
        RecordAdapterPlanAcceptedCalls.Add((accepted, instanceId, connectionGeneration, playContextId, playContextGeneration));
}
