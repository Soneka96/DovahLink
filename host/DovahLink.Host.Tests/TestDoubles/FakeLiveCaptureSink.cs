using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="ILiveCaptureSink"/> that records every applied capture result.</summary>
public sealed class FakeLiveCaptureSink : ILiveCaptureSink
{
    /// <summary>Every capture result passed to <see cref="ApplyCaptureResult"/>, in call order.</summary>
    public List<IpcCaptureResultMessage> Applied { get; } = [];

    /// <summary>The connection generation <see cref="ApplyCaptureResult"/> reports through <see cref="CaptureResultApplied"/>.</summary>
    public long ConnectionGeneration { get; set; }

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult)
    {
        CaptureResultApplied?.Invoke(captureResult, ConnectionGeneration);
        Applied.Add(captureResult);
    }
}

// TODO(stage4-file-extraction): Move FakeResynchronizationTransactionCoordinator
// to its own FakeResynchronizationTransactionCoordinator.cs in the post-Stage-4
// structural cleanup PR. Temporarily colocated here to hold this PR's
// changed-file count down; extraction only, no behavior change.
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
