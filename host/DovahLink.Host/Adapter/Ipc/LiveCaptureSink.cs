using System.Buffers.Binary;
using System.Text.Json;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The domain-facing entry point for adapter-reported capture results. Keeps
/// <see cref="AdapterIpcSession"/> itself free of state-area decoding and application policy: the
/// session only routes a decoded <see cref="IpcCaptureResultMessage"/> here, and this sink owns
/// everything about turning it into authoritative state.
/// </summary>
public interface ILiveCaptureSink
{
    /// <summary>
    /// Raised at the start of every <see cref="ApplyCaptureResult"/> call, before any of its own
    /// recognition, provenance, or decoding checks -- carrying the raw result and the adapter
    /// connection generation it arrived under, per <see cref="IAdapterAvailabilityTracker"/>'s own
    /// canonical numbering. Lets an interested collaborator (for example <c>LiveStateScheduler</c>,
    /// releasing its own per-sample outstanding-request tracking) observe that a reply arrived at
    /// all, independently of whether this sink goes on to actually apply it. This is the sink's own
    /// event specifically so a listener like the scheduler never needs a direct dependency on
    /// <see cref="AdapterIpcSession"/> or the adapter-IPC listener it would otherwise have to reach
    /// through -- avoiding a composition-root dependency cycle back through the connection factory
    /// that builds every session.
    /// </summary>
    event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <summary>Applies one adapter-reported capture result.</summary>
    /// <param name="captureResult">The decoded capture result to apply.</param>
    void ApplyCaptureResult(IpcCaptureResultMessage captureResult);
}

/// <inheritdoc cref="ILiveCaptureSink"/>
/// <remarks>
/// Decodes each capture per <see cref="LiveStateCatalog"/>, applies it through the matching typed
/// <see cref="IStatePublisher{TState}"/>, and publishes through <see cref="IStatePublicationSink"/>
/// only when the apply actually changed the area's value. Unknown capture keys, malformed payloads,
/// non-finite floats, and captures whose stamped play context no longer matches the host's current
/// one are all silently dropped -- fail closed, never a plausible fabricated value.
/// </remarks>
public sealed class LiveCaptureSink : ILiveCaptureSink
{
    /// <summary>The catalog this sink decodes and applies every capture against.</summary>
    private readonly LiveStateCatalog catalog;

    /// <summary>Backs every float-valued area: health, magicka, stamina, and experience.</summary>
    private readonly IStatePublisher<float?> floatPublisher;

    /// <summary>Backs the level area.</summary>
    private readonly IStatePublisher<ushort?> levelPublisher;

    /// <summary>Where an accepted, changed value becomes a publication.</summary>
    private readonly IStatePublicationSink publicationSink;

    /// <summary>Consulted for source identity and resynchronization gating.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Consulted to reject a capture whose stamped play context has already gone stale.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Claims each baseline area's resynchronization token and records its own transaction's progress.</summary>
    private readonly IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator;

    /// <summary>Stamps every publication's <c>occurredAt</c> display timestamp.</summary>
    private readonly IClock clock;

    /// <summary>Creates a live capture sink.</summary>
    /// <param name="catalog">The catalog this sink decodes and applies every capture against.</param>
    /// <param name="floatPublisher">Backs every float-valued area.</param>
    /// <param name="levelPublisher">Backs the level area.</param>
    /// <param name="publicationSink">Where an accepted, changed value becomes a publication.</param>
    /// <param name="adapterAvailabilityTracker">Consulted for source identity and resynchronization gating.</param>
    /// <param name="playContextTracker">Consulted to reject a capture whose stamped play context has already gone stale.</param>
    /// <param name="resynchronizationTransactionCoordinator">Claims each baseline area's resynchronization token and records its own transaction's progress.</param>
    /// <param name="clock">Stamps every publication's display timestamp.</param>
    public LiveCaptureSink(
        LiveStateCatalog catalog,
        IStatePublisher<float?> floatPublisher,
        IStatePublisher<ushort?> levelPublisher,
        IStatePublicationSink publicationSink,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IResynchronizationTransactionCoordinator resynchronizationTransactionCoordinator,
        IClock clock)
    {
        this.catalog = catalog;
        this.floatPublisher = floatPublisher;
        this.levelPublisher = levelPublisher;
        this.publicationSink = publicationSink;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.resynchronizationTransactionCoordinator = resynchronizationTransactionCoordinator;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult)
    {
        CaptureResultApplied?.Invoke(captureResult, adapterAvailabilityTracker.GetSnapshot().ConnectionGeneration);

        CaptureUnitDefinition? unit = catalog.CaptureUnits.FirstOrDefault(
            candidate => candidate.Source == captureResult.Source && candidate.CaptureKey == captureResult.CaptureKey);
        if (unit is null)
        {
            return;
        }

        PlayContextSnapshot contextSnapshot = playContextTracker.GetSnapshot();
        if (contextSnapshot.Current is not PlayContextId currentContext || currentContext != captureResult.PlayContextId)
        {
            // Queued or delayed across a transition; applying it here would misattribute an old
            // context's value to the new one. See ai/context/host/architecture.md's provenance rule.
            return;
        }

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        DateTimeOffset occurredAt = clock.UtcNow;

        if (captureResult.Source == CaptureSourceKind.Sample && captureResult.CaptureKey == (uint)CharacterSampleToken.CharacterVitals)
        {
            ApplyVitals(captureResult, unit, adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
        }
        else if (captureResult.Source == CaptureSourceKind.Sample && captureResult.CaptureKey == (uint)CharacterSampleToken.CharacterXp)
        {
            ApplyScalarFloat(captureResult, unit.StateAreas[0], adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
        }
        else
        {
            // The only remaining catalog entries are the level baseline sample and the
            // level-changed event, both sharing the same uint16 decode and the same area.
            ApplyLevel(captureResult, unit.StateAreas[0], adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
        }
    }

    /// <summary>
    /// Decodes and applies the one coherent vitals sample -- health, magicka, and stamina, in that
    /// fixed wire order, matching <see cref="LiveStateCatalog.Default"/>'s own declared area order
    /// for this capture unit -- to their three independent areas. A malformed payload (wrong length,
    /// or any of the three not a finite value) applies nothing at all, rather than a partial or
    /// fabricated reading.
    /// </summary>
    private void ApplyVitals(
        IpcCaptureResultMessage captureResult,
        CaptureUnitDefinition unit,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        float? health = null;
        float? magicka = null;
        float? stamina = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (captureResult.Payload.Length != 12
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(0, 4), out float decodedHealth)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(4, 4), out float decodedMagicka)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(8, 4), out float decodedStamina))
            {
                return;
            }

            (health, magicka, stamina) = (decodedHealth, decodedMagicka, decodedStamina);
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        // A baseline sample always carries correlation id zero, unlike a scheduler-issued ordinary
        // ReadSample's own nonzero one -- see ApplyAndPublish's own documentation for why this, not
        // the adapter's global resynchronization flag, is what decides baseline-vs-live purpose here.
        bool isResynchronizationBaseline = captureResult.CorrelationId == 0;
        ApplyAndPublish(floatPublisher, UpdateMode.Snapshot, unit.StateAreas[0], health, isResynchronizationBaseline, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        ApplyAndPublish(floatPublisher, UpdateMode.Snapshot, unit.StateAreas[1], magicka, isResynchronizationBaseline, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        ApplyAndPublish(floatPublisher, UpdateMode.Snapshot, unit.StateAreas[2], stamina, isResynchronizationBaseline, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
    }

    /// <summary>Decodes and applies a single-float capture (experience) to its one area.</summary>
    private void ApplyScalarFloat(
        IpcCaptureResultMessage captureResult,
        StateAreaId areaId,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        float? value = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (captureResult.Payload.Length != 4 || !TryDecodeFiniteFloat(captureResult.Payload, out float decoded))
            {
                return;
            }

            value = decoded;
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        // See ApplyVitals's identical comment: a baseline sample's own correlation id, not the
        // adapter's global resynchronization flag, decides baseline-vs-live purpose here.
        ApplyAndPublish(floatPublisher, UpdateMode.Snapshot, areaId, value, captureResult.CorrelationId == 0, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
    }

    /// <summary>
    /// Decodes and applies a single-uint16 capture (level, from either the baseline sample or the
    /// level-changed event) to its one area. The two sources carry different delivery semantics even
    /// though they share the same decode and the same area: the baseline sample establishes the
    /// current authoritative level as a replaceable <see cref="UpdateMode.Snapshot"/>, while the
    /// level-changed event is an ordered, reliable <see cref="UpdateMode.Event"/> -- routing both
    /// through Event would misrepresent a resynchronization baseline as an ordered level change.
    /// </summary>
    private void ApplyLevel(
        IpcCaptureResultMessage captureResult,
        StateAreaId areaId,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        ushort? value = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (captureResult.Payload.Length != 2)
            {
                return;
            }

            value = BinaryPrimitives.ReadUInt16LittleEndian(captureResult.Payload);
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        UpdateMode mode = captureResult.Source == CaptureSourceKind.Sample ? UpdateMode.Snapshot : UpdateMode.Event;
        // The baseline sample's own correlation id (zero, like every other baseline sample) decides
        // baseline-vs-live purpose, matching ApplyVitals/ApplyScalarFloat -- see ApplyAndPublish's own
        // documentation. The level-changed event branch deliberately still reads the adapter's global
        // resynchronization flag, unchanged from before: a native event genuinely satisfying (or
        // wrongly appearing to satisfy) a resynchronization while one is outstanding for some other
        // area is a real, separate question this fix does not address, since a live level-up firing
        // mid-resync must remain reliable, not simply be classified correctly.
        bool isResynchronizationBaseline = captureResult.Source == CaptureSourceKind.Sample
            ? captureResult.CorrelationId == 0
            : adapterSnapshot.NeedsResynchronization;
        ApplyAndPublish(levelPublisher, mode, areaId, value, isResynchronizationBaseline, adapterSnapshot, capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
    }

    /// <summary>
    /// Applies one area's value through <paramref name="publisher"/> -- routing through
    /// <see cref="IStatePublisher{TState}.ApplyResynchronizationBaseline"/> when
    /// <paramref name="isResynchronizationBaseline"/> is <see langword="true"/>, or
    /// <see cref="IStatePublisher{TState}.Apply"/> otherwise. The caller decides
    /// <paramref name="isResynchronizationBaseline"/> from the capture's own identity (its
    /// correlation id and source), never from the adapter's global
    /// <see cref="AdapterAvailabilitySnapshot.NeedsResynchronization"/> flag alone: an ordinary,
    /// scheduler-issued sample always carries a nonzero correlation id and must never be read as
    /// satisfying a resynchronization merely because one happens to be outstanding when its reply
    /// arrives. An accepted, actually-changed result publishes through
    /// <see cref="IStatePublicationSink.PublishSnapshot"/> or <see cref="IStatePublicationSink.PublishEvent"/>;
    /// an accepted-but-unchanged Snapshot-mode resynchronization baseline still calls
    /// <see cref="IStatePublicationSink.EstablishBaseline"/> so the publication feed's pull-read
    /// cache -- unconditionally cleared by the continuity loss this resynchronization is recovering
    /// from -- is restored even when nothing about the value actually differs from before. An
    /// accepted resynchronization baseline is also reported to
    /// <see cref="resynchronizationTransactionCoordinator"/> regardless of whether it changed
    /// anything, since "this area's baseline landed" is what the transaction tracks, not "this area's
    /// value differs from before."
    /// </summary>
    /// <param name="publisher">The typed publisher this area's value is applied through.</param>
    /// <param name="mode">Whether this area publishes as a replaceable Snapshot or an ordered Event.</param>
    /// <param name="areaId">The state area this value belongs to.</param>
    /// <param name="value">The decoded captured value.</param>
    /// <param name="isResynchronizationBaseline">Whether this specific capture is a resynchronization baseline, decided by the caller from the capture's own identity.</param>
    /// <param name="adapterSnapshot">The adapter availability snapshot read once for this whole capture result.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this value was captured.</param>
    /// <param name="occurredAt">When this value was captured, for display and diagnostics only.</param>
    private void ApplyAndPublish<TState>(
        IStatePublisher<TState> publisher,
        UpdateMode mode,
        StateAreaId areaId,
        TState value,
        bool isResynchronizationBaseline,
        AdapterAvailabilitySnapshot adapterSnapshot,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt)
    {
        StateApplyResult result;
        if (isResynchronizationBaseline)
        {
            if (adapterSnapshot.CurrentInstanceId is not AdapterInstanceId resyncInstanceId)
            {
                return;
            }

            IAdapterResynchronizationToken? token = resynchronizationTransactionCoordinator.AcquireToken(
                resyncInstanceId, adapterSnapshot.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            if (token is null)
            {
                return;
            }

            result = publisher.ApplyResynchronizationBaseline(token, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);
            if (result.Accepted)
            {
                resynchronizationTransactionCoordinator.RecordAreaAccepted(
                    areaId, resyncInstanceId, adapterSnapshot.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration);
            }
        }
        else
        {
            if (adapterSnapshot.CurrentInstanceId is not AdapterInstanceId instanceId)
            {
                return;
            }

            result = publisher.Apply(instanceId, adapterSnapshot.ConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);
        }

        if (!result.Accepted)
        {
            return;
        }

        JsonElement data = JsonSerializer.SerializeToElement(new { value });

        if (!result.Changed)
        {
            // An accepted resynchronization baseline still repopulates the feed's pull-read cache even
            // when nothing actually differs from before: that cache was unconditionally cleared by the
            // continuity loss this resynchronization is recovering from, so leaving it untouched here
            // would strand it empty until some later, unrelated value change happened to refill it. An
            // ordinary (non-baseline) unchanged apply has no such cache to restore -- it was never
            // cleared -- so it stays a pure no-op, matching the architecture's "unchanged sampled values
            // stop before they create... publication[or] queue entry" rule.
            if (isResynchronizationBaseline && mode == UpdateMode.Snapshot)
            {
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

    /// <summary>Decodes a little-endian 32-bit float, rejecting NaN and Infinity: a capture failure must never become a plausible published value.</summary>
    private static bool TryDecodeFiniteFloat(ReadOnlySpan<byte> bytes, out float value)
    {
        value = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return float.IsFinite(value);
    }
}
