using System.Buffers.Binary;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

// TODO(stage4-file-extraction): Move ILiveCaptureHandler to its own
// ILiveCaptureHandler.cs in the post-Stage-4 structural cleanup PR.
// Temporarily colocated with its sole implementer to hold this PR's
// changed-file count down; extraction only, no behavior change.
/// <summary>Handles captures for the explicitly declared source and key identities it owns.</summary>
public interface ILiveCaptureHandler
{
    /// <summary>The source and key identities this handler accepts.</summary>
    IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures { get; }

    /// <summary>Decodes and applies one capture after generic provenance and context validation.</summary>
    /// <param name="context">The exact capture, catalog unit, and authority snapshots validated by the sink.</param>
    void Handle(LiveCaptureContext context);
}

/// <summary>Decodes Character captures and applies them through shared Host authority rules.</summary>
public sealed class CharacterCaptureHandler : ILiveCaptureHandler
{
    /// <summary>The Character capture identities owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline),
            (CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged),
        ]);

    /// <summary>Backs float-valued Character areas.</summary>
    private readonly IStatePublisher<float?> floatPublisher;

    /// <summary>Backs the Character level area.</summary>
    private readonly IStatePublisher<ushort?> levelPublisher;

    /// <summary>Applies decoded values through shared authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Creates the handler for the current Character capture set.</summary>
    /// <param name="floatPublisher">The typed publisher for health, magicka, stamina, and experience.</param>
    /// <param name="levelPublisher">The typed publisher for level.</param>
    /// <param name="liveStateApplication">The shared Host authority and publication service.</param>
    public CharacterCaptureHandler(
        IStatePublisher<float?> floatPublisher,
        IStatePublisher<ushort?> levelPublisher,
        ILiveStateApplication liveStateApplication)
    {
        this.floatPublisher = floatPublisher;
        this.levelPublisher = levelPublisher;
        this.liveStateApplication = liveStateApplication;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures => supportedCaptures;

    /// <inheritdoc/>
    public void Handle(LiveCaptureContext context)
    {
        CaptureUnitDefinition unit = context.CaptureUnit;
        IpcCaptureResultMessage captureResult = context.CaptureResult;

        if (unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals)
        {
            ApplyVitals(captureResult, context);
        }
        else if (unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp)
        {
            ApplyScalarFloat(captureResult, context);
        }
        else if ((unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterLevelBaseline)
            || (unit.Source == CaptureSourceKind.Event && unit.CaptureKey == (uint)CharacterEventKey.CharacterLevelChanged))
        {
            ApplyLevel(captureResult, context);
        }
    }

    /// <summary>Decodes and applies a coherent health, magicka, and stamina sample.</summary>
    /// <param name="captureResult">The Vitals capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyVitals(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 3)
        {
            return;
        }

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

        bool isResynchronizationBaseline = captureResult.CorrelationId == 0;
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], health, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[1], magicka, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[2], stamina, isResynchronizationBaseline, context);
    }

    /// <summary>Decodes and applies one experience value.</summary>
    /// <param name="captureResult">The XP capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyScalarFloat(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

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

        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], value, captureResult.CorrelationId == 0, context);
    }

    /// <summary>Decodes and applies the level baseline Sample or level-changed Event.</summary>
    /// <param name="captureResult">The Level capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyLevel(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

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

        UpdateMode mode = context.CaptureUnit.Source == CaptureSourceKind.Sample ? UpdateMode.Snapshot : UpdateMode.Event;
        bool isResynchronizationBaseline = mode == UpdateMode.Snapshot && captureResult.CorrelationId == 0;
        Apply(levelPublisher, mode, context.CaptureUnit.StateAreas[0], value, isResynchronizationBaseline, context);
    }

    /// <summary>Passes a decoded area value and its validated context to shared Host authority.</summary>
    /// <typeparam name="TState">The publisher's captured value type.</typeparam>
    /// <param name="publisher">The typed publisher for the destination area.</param>
    /// <param name="mode">The area's canonical Snapshot or Event mode.</param>
    /// <param name="areaId">The destination state area.</param>
    /// <param name="value">The decoded value, or <see langword="null"/> when unavailable.</param>
    /// <param name="isResynchronizationBaseline">Whether the capture is an identified baseline sample.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void Apply<TState>(
        IStatePublisher<TState> publisher,
        UpdateMode mode,
        StateAreaId areaId,
        TState value,
        bool isResynchronizationBaseline,
        LiveCaptureContext context) =>
        liveStateApplication.Apply(
            publisher,
            mode,
            areaId,
            value,
            isResynchronizationBaseline,
            context.Source,
            context.AdapterSnapshot,
            context.PlayContextId,
            context.PlayContextGeneration,
            context.OccurredAt);

    /// <summary>Decodes a little-endian float and rejects NaN and Infinity.</summary>
    /// <param name="bytes">The four-byte encoded float.</param>
    /// <param name="value">The decoded float.</param>
    /// <returns><see langword="true"/> when the decoded value is finite.</returns>
    private static bool TryDecodeFiniteFloat(ReadOnlySpan<byte> bytes, out float value)
    {
        value = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return float.IsFinite(value);
    }
}
