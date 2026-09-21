using System.Text.Json;
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
    /// <typeparam name="TState">The value type owned by <paramref name="publisher"/>.</typeparam>
    /// <param name="publisher">The typed publisher responsible for the area's authoritative state.</param>
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
    /// Ordinary samples use ordinary publisher authority. Only a baseline sample can record an
    /// accepted resynchronization area. Events remain reliable during resynchronization and request
    /// controlled recovery if current authority cannot safely apply them.
    /// </remarks>
    void Apply<TState>(
        IStatePublisher<TState> publisher,
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

    /// <summary>Receives accepted changes and restored baseline snapshots.</summary>
    private readonly IStatePublicationSink publicationSink;

    /// <summary>Creates the shared authority and publication application service.</summary>
    /// <param name="resynchronizationTransactionCoordinator">Coordinates accepted baseline areas.</param>
    /// <param name="continuityRecovery">Requests generation-checked recovery for rejected Events.</param>
    /// <param name="publicationSink">Stores and publishes accepted state changes.</param>
    public LiveStateApplication(
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator,
        IAdapterContinuityRecovery continuityRecovery,
        IStatePublicationSink publicationSink)
    {
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;
        this.continuityRecovery = continuityRecovery;
        this.publicationSink = publicationSink;
    }

    /// <inheritdoc/>
    public void Apply<TState>(
        IStatePublisher<TState> publisher,
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
        StateApplyResult result;
        if (isResynchronizationBaseline)
        {
            IAdapterResynchronizationToken? token = resynchronizationTransactionCoordinator.AcquireToken(
                source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            if (token is null)
            {
                return;
            }

            result = publisher.ApplyResynchronizationBaseline(token, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);
            if (result.Accepted)
            {
                resynchronizationTransactionCoordinator.RecordAreaAccepted(
                    areaId, source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            }
        }
        else if (mode == UpdateMode.Event)
        {
            IAdapterResynchronizationToken? token = adapterSnapshot.NeedsResynchronization
                ? resynchronizationTransactionCoordinator.AcquireToken(
                    source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration)
                : null;
            result = publisher.ApplyEvent(
                source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
                token, areaId, value);

            if (!result.Accepted)
            {
                token = resynchronizationTransactionCoordinator.AcquireToken(
                    source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
                result = token is null
                    ? StateApplyResult.Rejected
                    : publisher.ApplyEvent(
                        source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration,
                        token, areaId, value);
            }

            if (!result.Accepted)
            {
                continuityRecovery.RequestRecovery(source.ConnectionGeneration);
                return;
            }
        }
        else
        {
            result = publisher.Apply(source.InstanceId, source.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);
        }

        if (!result.Accepted)
        {
            return;
        }

        JsonElement data = JsonSerializer.SerializeToElement(new { value });

        if (!result.Changed)
        {
            if (isResynchronizationBaseline && mode == UpdateMode.Snapshot)
            {
                // Continuity loss clears the feed cache, so an unchanged baseline must restore it without publishing a change.
                publicationSink.EstablishBaseline(areaId, result.Revision, data, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
            }

            return;
        }

        if (mode == UpdateMode.Snapshot)
        {
            publicationSink.PublishSnapshot(areaId, result.Revision, data, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        }
        else
        {
            publicationSink.PublishEvent(areaId, result.BaseRevision, result.Revision, data, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        }
    }
}
