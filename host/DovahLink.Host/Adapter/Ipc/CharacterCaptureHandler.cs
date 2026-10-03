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

    /// <summary>Backs the coherent Vitals area.</summary>
    private readonly IStatePublisher<CharacterVitals?> vitalsPublisher;

    /// <summary>Backs the Character XP area.</summary>
    private readonly IStatePublisher<float?> xpPublisher;

    /// <summary>Backs the Character level area.</summary>
    private readonly IStatePublisher<ushort?> levelPublisher;

    /// <summary>Applies decoded values through shared authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Creates the handler for the current Character capture set.</summary>
    /// <param name="vitalsPublisher">The typed publisher for coherent Vitals.</param>
    /// <param name="xpPublisher">The typed publisher for XP.</param>
    /// <param name="levelPublisher">The typed publisher for level.</param>
    /// <param name="liveStateApplication">The shared Host authority and publication service.</param>
    public CharacterCaptureHandler(
        IStatePublisher<CharacterVitals?> vitalsPublisher,
        IStatePublisher<float?> xpPublisher,
        IStatePublisher<ushort?> levelPublisher,
        ILiveStateApplication liveStateApplication)
    {
        this.vitalsPublisher = vitalsPublisher;
        this.xpPublisher = xpPublisher;
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
            ApplyXp(captureResult, context);
        }
        else if ((unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterLevelBaseline)
            || (unit.Source == CaptureSourceKind.Event && unit.CaptureKey == (uint)CharacterEventKey.CharacterLevelChanged))
        {
            ApplyLevel(captureResult, context);
        }
    }

    /// <summary>Decodes and applies one coherent current-and-maximum Vitals value.</summary>
    /// <param name="captureResult">The Vitals capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyVitals(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

        CharacterVitals? vitals = null;
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

            vitals = new CharacterVitals(
                new CharacterVital(decodedHealth, decodedHealthMax),
                new CharacterVital(decodedMagicka, decodedMagickaMax),
                new CharacterVital(decodedStamina, decodedStaminaMax));
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        bool isResynchronizationBaseline = captureResult.CorrelationId == 0;
        Apply(vitalsPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], vitals, isResynchronizationBaseline, context);
    }

    /// <summary>Decodes and applies one XP value.</summary>
    /// <param name="captureResult">The XP capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyXp(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
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

        Apply(xpPublisher, UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], value, captureResult.CorrelationId == 0, context);
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
