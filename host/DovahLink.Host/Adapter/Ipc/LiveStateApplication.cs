using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Applies validated capture values through shared Host authority and publication rules.</summary>
public interface ILiveStateApplication
{
    /// <summary>
    /// Applies one area's value as an ordinary update, resynchronization baseline, or reliable
    /// Event according to the supplied capture identity and state-area mode.
    /// </summary>
    /// <typeparam name="TState">The decoded value type; an area's type is fixed by its first write.</typeparam>
    /// <param name="mode">Whether the area is replaceable Snapshot state or an ordered Event.</param>
    /// <param name="areaId">The state area receiving the value.</param>
    /// <param name="value">The decoded captured value.</param>
    /// <param name="isResynchronizationBaseline">Whether the caller identified this capture as a current baseline from its own source and correlation identity.</param>
    /// <param name="source">The exact adapter connection that delivered the capture.</param>
    /// <param name="adapterSnapshot">The availability snapshot read for this capture.</param>
    /// <param name="capturedPlayContextId">The play context stamped on the capture.</param>
    /// <param name="capturedPlayContextGeneration">The play-context generation read for this capture.</param>
    /// <param name="occurredAt">The capture timestamp used by any resulting publication.</param>
    /// <remarks>
    /// Ordinary samples use ordinary source authority. Only a baseline sample can record an accepted
    /// resynchronization area, and the authoritative store records it only after committing that baseline
    /// as replayable current state. Events remain reliable during resynchronization
    /// and request controlled recovery if current authority cannot safely apply them.
    /// </remarks>
    void Apply<TState>(
        UpdateMode mode,
        StateAreaId areaId,
        TState value,
        bool isResynchronizationBaseline,
        AdapterCaptureSource source,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt);
}

/// <inheritdoc cref="ILiveStateApplication"/>
public sealed class LiveStateApplication : ILiveStateApplication
{
    /// <summary>Claims baseline tokens and records accepted areas for each transaction.</summary>
    private readonly IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator;

    /// <summary>Requests controlled recovery when a reliable Event cannot be applied safely.</summary>
    private readonly IAdapterContinuityRecovery continuityRecovery;

    /// <summary>The single owner of authoritative current state, revisions, and replay Snapshots.</summary>
    private readonly IAuthoritativeStateStore stateStore;

    /// <summary>Creates the shared authority and application service.</summary>
    /// <param name="resynchronizationTransactionCoordinator">Coordinates accepted baseline areas.</param>
    /// <param name="continuityRecovery">Requests generation-checked recovery for rejected Events.</param>
    /// <param name="stateStore">Commits accepted state and publishes its changes.</param>
    public LiveStateApplication(
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator,
        IAdapterContinuityRecovery continuityRecovery,
        IAuthoritativeStateStore stateStore)
    {
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;
        this.continuityRecovery = continuityRecovery;
        this.stateStore = stateStore;
    }

    /// <inheritdoc/>
    public void Apply<TState>(
        UpdateMode mode,
        StateAreaId areaId,
        TState value,
        bool isResynchronizationBaseline,
        AdapterCaptureSource source,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        if (isResynchronizationBaseline)
        {
            IAdapterResynchronizationToken? token = resynchronizationTransactionCoordinator.AcquireToken(
                source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            if (token is null)
            {
                return;
            }

            // The store runs this under its ordering lock after committing the baseline as replayable
            // current state and before any change notification, and never for a rejected baseline, so a
            // baseline counts toward resynchronization only once its area can be replayed.
            stateStore.ApplyResynchronizationBaseline(
                mode, token, capturedPlayContextId, capturedPlayContextGeneration, occurredAt, areaId, value,
                onCommitted: () => resynchronizationTransactionCoordinator.RecordAreaAccepted(
                    areaId, source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration));
            return;
        }

        if (mode == UpdateMode.Event)
        {
            ApplyEvent(areaId, value, source, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
            return;
        }

        stateStore.Apply(
            source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration, occurredAt, areaId, value);
    }

    /// <summary>
    /// Applies a reliable Event, acquiring the resynchronization token when the adapter is gated and
    /// requesting controlled recovery if current authority cannot apply it.
    /// </summary>
    /// <typeparam name="TState">The decoded value type.</typeparam>
    /// <param name="areaId">The state area receiving the Event.</param>
    /// <param name="value">The Event's resulting value.</param>
    /// <param name="source">The exact adapter connection that delivered the Event.</param>
    /// <param name="adapterSnapshot">The availability snapshot read for this capture.</param>
    /// <param name="capturedPlayContextId">The play context stamped on the capture.</param>
    /// <param name="capturedPlayContextGeneration">The play-context generation read for this capture.</param>
    /// <param name="occurredAt">The capture timestamp used by any resulting publication.</param>
    private void ApplyEvent<TState>(
        StateAreaId areaId,
        TState value,
        AdapterCaptureSource source,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        IAdapterResynchronizationToken? token = adapterSnapshot.NeedsResynchronization
            ? resynchronizationTransactionCoordinator.AcquireToken(
                source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration)
            : null;
        StateApplyResult result = stateStore.ApplyEvent(
            source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
            token, occurredAt, areaId, value);

        if (!result.Accepted)
        {
            token = resynchronizationTransactionCoordinator.AcquireToken(
                source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            result = token is null
                ? StateApplyResult.Rejected
                : stateStore.ApplyEvent(
                    source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
                    token, occurredAt, areaId, value);
        }

        if (!result.Accepted)
        {
            continuityRecovery.RequestRecovery(source.ConnectionGeneration);
        }
    }
}
