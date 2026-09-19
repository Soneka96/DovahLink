using System.Buffers.Binary;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Time;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The domain-facing entry point for adapter-reported capture results. It validates capture
/// provenance and context, decodes recognized values, and delegates authority and publication to
/// <see cref="ILiveStateApplication"/>.
/// </summary>
public interface ILiveCaptureSink
{
    /// <summary>
    /// Raised at the start of every <see cref="ApplyCaptureResult"/> call, before any of its own
    /// recognition, provenance, or decoding checks -- carrying the raw result and the connection
    /// generation of <paramref name="source"/> as passed to that call, not rediscovered from mutable
    /// global availability state. Lets an interested collaborator (for example
    /// <c>LiveStateScheduler</c>, releasing its own per-sample outstanding-request tracking) observe
    /// that a reply arrived at all, independently of whether this sink goes on to actually apply it.
    /// This is the sink's own event specifically so a listener like the scheduler never needs a
    /// direct dependency on <see cref="AdapterIpcSession"/> or the adapter-IPC listener it would
    /// otherwise have to reach through -- avoiding a composition-root dependency cycle back through
    /// the connection factory that builds every session.
    /// </summary>
    event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <summary>Applies one adapter-reported capture result.</summary>
    /// <param name="captureResult">The decoded capture result to apply.</param>
    /// <param name="source">The exact adapter connection that received <paramref name="captureResult"/>.</param>
    void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source);
}

/// <inheritdoc cref="ILiveCaptureSink"/>
/// <remarks>
/// Validates captures against <see cref="LiveStateCatalog"/>, decodes their values, and delegates
/// application to <see cref="ILiveStateApplication"/>. Unknown capture keys, malformed payloads,
/// non-finite floats, and captures whose stamped play context no longer matches the host's current
/// one are silently dropped.
/// </remarks>
public sealed class LiveCaptureSink : ILiveCaptureSink
{
    /// <summary>The catalog this sink decodes and applies every capture against.</summary>
    private readonly LiveStateCatalog catalog;

    /// <summary>Backs every float-valued area: health, magicka, stamina, and experience.</summary>
    private readonly IStatePublisher<float?> floatPublisher;

    /// <summary>Backs the level area.</summary>
    private readonly IStatePublisher<ushort?> levelPublisher;

    /// <summary>Applies decoded values through shared Host authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Consulted for source validation and resynchronization gating.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>Consulted to reject a capture whose stamped play context has already gone stale.</summary>
    private readonly IPlayContextTracker playContextTracker;

    /// <summary>Stamps every publication's <c>occurredAt</c> display timestamp.</summary>
    private readonly IClock clock;

    /// <summary>Creates a live capture sink.</summary>
    /// <param name="catalog">The catalog this sink decodes and applies every capture against.</param>
    /// <param name="floatPublisher">Backs every float-valued area.</param>
    /// <param name="levelPublisher">Backs the level area.</param>
    /// <param name="liveStateApplication">Applies decoded values through shared Host authority and publication rules.</param>
    /// <param name="adapterAvailabilityTracker">Consulted for source validation and resynchronization gating.</param>
    /// <param name="playContextTracker">Consulted to reject a capture whose stamped play context has already gone stale.</param>
    /// <param name="clock">Stamps every publication's display timestamp.</param>
    public LiveCaptureSink(
        LiveStateCatalog catalog,
        IStatePublisher<float?> floatPublisher,
        IStatePublisher<ushort?> levelPublisher,
        ILiveStateApplication liveStateApplication,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IPlayContextTracker playContextTracker,
        IClock clock)
    {
        this.catalog = catalog;
        this.floatPublisher = floatPublisher;
        this.levelPublisher = levelPublisher;
        this.liveStateApplication = liveStateApplication;
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.playContextTracker = playContextTracker;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public event Action<IpcCaptureResultMessage, long>? CaptureResultApplied;

    /// <inheritdoc/>
    public void ApplyCaptureResult(IpcCaptureResultMessage captureResult, AdapterCaptureSource source)
    {
        CaptureResultApplied?.Invoke(captureResult, source.ConnectionGeneration);

        AdapterAvailabilitySnapshot adapterSnapshot = adapterAvailabilityTracker.GetSnapshot();
        if (adapterSnapshot.Current != AdapterAvailability.Available
            || adapterSnapshot.CurrentInstanceId != source.InstanceId
            || adapterSnapshot.ConnectionGeneration != source.ConnectionGeneration)
        {
            return;
        }

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

        DateTimeOffset occurredAt = clock.UtcNow;

        if (captureResult.Source == CaptureSourceKind.Sample && captureResult.CaptureKey == (uint)CharacterSampleToken.CharacterVitals)
        {
            ApplyVitals(captureResult, unit, source, adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
        }
        else if (captureResult.Source == CaptureSourceKind.Sample && captureResult.CaptureKey == (uint)CharacterSampleToken.CharacterXp)
        {
            ApplyScalarFloat(captureResult, unit.StateAreas[0], source, adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
        }
        else
        {
            // The only remaining catalog entries are the level baseline sample and the
            // level-changed event, both sharing the same uint16 decode and the same area.
            ApplyLevel(captureResult, unit.StateAreas[0], source, adapterSnapshot, currentContext, contextSnapshot.TransitionGeneration, occurredAt);
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
        AdapterCaptureSource source,
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
        // ReadSample's own nonzero one -- see the shared application contract for why this, not
        // the adapter's global resynchronization flag, is what decides baseline-vs-live purpose here.
        bool isResynchronizationBaseline = captureResult.CorrelationId == 0;
        liveStateApplication.Apply(
            floatPublisher, UpdateMode.Snapshot, unit.StateAreas[0], health, isResynchronizationBaseline, source, adapterSnapshot,
            capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        liveStateApplication.Apply(
            floatPublisher, UpdateMode.Snapshot, unit.StateAreas[1], magicka, isResynchronizationBaseline, source, adapterSnapshot,
            capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
        liveStateApplication.Apply(
            floatPublisher, UpdateMode.Snapshot, unit.StateAreas[2], stamina, isResynchronizationBaseline, source, adapterSnapshot,
            capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
    }

    /// <summary>Decodes and applies a single-float capture (experience) to its one area.</summary>
    private void ApplyScalarFloat(
        IpcCaptureResultMessage captureResult,
        StateAreaId areaId,
        AdapterCaptureSource source,
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

        // A baseline sample's own correlation id, not the
        // adapter's global resynchronization flag, decides baseline-vs-live purpose here.
        liveStateApplication.Apply(
            floatPublisher, UpdateMode.Snapshot, areaId, value, captureResult.CorrelationId == 0, source, adapterSnapshot,
            capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
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
        AdapterCaptureSource source,
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
        // Only a baseline sample can satisfy resynchronization. A native level-changed Event remains
        // an Event even when resynchronization is outstanding; the shared application gives it a
        // token-authorized path that never records an accepted baseline area.
        bool isResynchronizationBaseline = captureResult.Source == CaptureSourceKind.Sample
            && captureResult.CorrelationId == 0;
        liveStateApplication.Apply(
            levelPublisher, mode, areaId, value, isResynchronizationBaseline, source, adapterSnapshot,
            capturedPlayContextId, capturedPlayContextGeneration, occurredAt);
    }

    /// <summary>Decodes a little-endian 32-bit float, rejecting NaN and Infinity: a capture failure must never become a plausible published value.</summary>
    private static bool TryDecodeFiniteFloat(ReadOnlySpan<byte> bytes, out float value)
    {
        value = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return float.IsFinite(value);
    }
}

// TODO(stage4-file-extraction): Move AdapterCaptureSource to its own
// AdapterCaptureSource.cs in the post-Stage-4 structural cleanup PR.
// Temporarily colocated here to hold this PR's changed-file count down;
// extraction only, no behavior change.
/// <summary>
/// Identifies the exact adapter connection that received one capture result. Passed in by the
/// receiving <see cref="AdapterIpcSession"/>, which already knows its own identity, rather than
/// rediscovered inside <see cref="LiveCaptureSink"/> from mutable global availability state that may
/// have since moved on to a newer connection.
/// </summary>
/// <param name="InstanceId">The adapter instance this capture result was received from.</param>
/// <param name="ConnectionGeneration">The connection generation the capture result was received on.</param>
public readonly record struct AdapterCaptureSource(AdapterInstanceId InstanceId, long ConnectionGeneration);
