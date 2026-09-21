using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IStatePublicationFeed"/> with settable snapshot values and a way to raise events.</summary>
public sealed class FakeStatePublicationFeed : IStatePublicationFeed
{
    /// <summary>The value each area's <see cref="TryGetSnapshot"/> call returns.</summary>
    private readonly Dictionary<StateAreaId, StateSnapshotPublication> snapshotsByArea = [];

    /// <inheritdoc/>
    public event Action<StateEventPublication>? EventOccurred;

    /// <inheritdoc/>
    public event Action<StateSnapshotPublication>? SnapshotChanged;

    /// <inheritdoc/>
    public event Action? SnapshotAvailabilityChanged;

    /// <summary>Whether any caller currently holds a live registration on <see cref="EventOccurred"/>.</summary>
    public bool HasSubscribers => EventOccurred is not null;

    /// <summary>Sets the value <see cref="TryGetSnapshot"/> returns for <paramref name="areaId"/>.</summary>
    /// <param name="areaId">The state area to set a value for.</param>
    /// <param name="snapshot">The value <see cref="TryGetSnapshot"/> should return.</param>
    public void SetSnapshot(StateAreaId areaId, StateSnapshotPublication snapshot) => snapshotsByArea[areaId] = snapshot;

    /// <summary>
    /// Invoked synchronously at the start of every <see cref="TryGetSnapshot"/> call, before it looks
    /// up or returns anything -- lets a test inject work (for example raising an event) exactly while
    /// a caller's baseline fetch is in flight, to exercise a race the caller's own locking is meant to
    /// close.
    /// </summary>
    public Action? OnTryGetSnapshot { get; set; }

    /// <inheritdoc/>
    public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot)
    {
        OnTryGetSnapshot?.Invoke();
        return snapshotsByArea.TryGetValue(areaId, out snapshot);
    }

    /// <summary>Raises <see cref="EventOccurred"/>, as a real feed would when a registered area's value changes.</summary>
    /// <param name="eventPublication">The event to raise.</param>
    public void RaiseEvent(StateEventPublication eventPublication) => EventOccurred?.Invoke(eventPublication);

    /// <summary>Raises <see cref="SnapshotChanged"/>, as a real feed would when a registered Snapshot-mode area's value changes.</summary>
    /// <param name="snapshotPublication">The snapshot value to raise.</param>
    public void RaiseSnapshotChanged(StateSnapshotPublication snapshotPublication) => SnapshotChanged?.Invoke(snapshotPublication);

    /// <summary>Raises <see cref="SnapshotAvailabilityChanged"/> without claiming that a snapshot is readable.</summary>
    public void RaiseSnapshotAvailabilityChanged() => SnapshotAvailabilityChanged?.Invoke();
}

// TODO(stage4-file-extraction): Move FakeLiveCaptureSink to its own
// FakeLiveCaptureSink.cs in the post-Stage-4 structural cleanup PR.
// Temporarily colocated here to hold this PR's changed-file count down;
// extraction only, no behavior change.
/// <summary>A controllable stand-in for <see cref="ILiveCaptureSink"/> that records every applied capture result.</summary>
public sealed class FakeLiveCaptureSink : ILiveCaptureSink
{
    /// <summary>Every capture result passed to <see cref="ApplyCaptureResult"/>, in call order.</summary>
    public List<IpcCaptureResultMessage> Applied { get; } = [];

    /// <summary>Every source passed to <see cref="ApplyCaptureResult"/>, in call order, parallel to <see cref="Applied"/>.</summary>
    public List<AdapterCaptureSource> Sources { get; } = [];

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source)
    {
        CaptureResultApplied?.Invoke(captureResult, source.ConnectionGeneration);
        Applied.Add(captureResult);
        Sources.Add(source);
    }
}

// TODO(stage4-file-extraction): Move FakeResynchronizationTransactionCoordinator
// and FakeAdapterContinuityRecovery to their own files in the post-Stage-4
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

/// <summary>A controllable stand-in for <see cref="IAdapterContinuityRecovery"/>.</summary>
public sealed class FakeAdapterContinuityRecovery : IAdapterContinuityRecovery
{
    /// <summary>Every connection passed to <see cref="SetCurrentConnection"/>, in call order.</summary>
    public List<IAdapterIpcConnection?> CurrentConnectionCalls { get; } = [];

    /// <summary>Every generation passed to <see cref="RequestRecovery"/>, in call order.</summary>
    public List<long> RecoveryRequests { get; } = [];

    /// <inheritdoc/>
    public void SetCurrentConnection(IAdapterIpcConnection? connection) => CurrentConnectionCalls.Add(connection);

    /// <inheritdoc/>
    public void RequestRecovery(long connectionGeneration) => RecoveryRequests.Add(connectionGeneration);
}
