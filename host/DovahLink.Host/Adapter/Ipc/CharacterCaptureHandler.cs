using System.Buffers.Binary;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

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
    /// <param name="floatPublisher">The typed publisher for Character float areas.</param>
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

    /// <summary>Decodes and applies a coherent current-and-maximum Vitals sample.</summary>
    /// <param name="captureResult">The Vitals capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyVitals(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 6)
        {
            return;
        }

        float? health = null;
        float? magicka = null;
        float? stamina = null;
        float? healthMax = null;
        float? magickaMax = null;
        float? staminaMax = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (captureResult.Payload.Length != 24
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(0, 4), out float decodedHealth)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(4, 4), out float decodedMagicka)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(8, 4), out float decodedStamina)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(12, 4), out float decodedHealthMax)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(16, 4), out float decodedMagickaMax)
                || !TryDecodeFiniteFloat(captureResult.Payload.AsSpan(20, 4), out float decodedStaminaMax))
            {
                return;
            }

            (health, magicka, stamina, healthMax, magickaMax, staminaMax) =
                (decodedHealth, decodedMagicka, decodedStamina, decodedHealthMax, decodedMagickaMax, decodedStaminaMax);
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        bool isResynchronizationBaseline = captureResult.CorrelationId == 0;
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], health, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[1], magicka, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[2], stamina, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[3], healthMax, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[4], magickaMax, isResynchronizationBaseline, context);
        Apply(floatPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[5], staminaMax, isResynchronizationBaseline, context);
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
