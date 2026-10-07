using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Decodes Character captures and applies them through shared Host authority rules.</summary>
public sealed class CharacterCaptureHandler : ILiveCaptureHandler
{
    /// <summary>The strict decoder matching the Adapter's UTF-8 identity payload policy.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The Character capture identities owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterIdentity),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterSupernaturalTraits),
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline),
            (CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged),
        ]);

    /// <summary>Applies decoded values through shared authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Creates the handler for the current Character capture set.</summary>
    /// <param name="liveStateApplication">The shared Host authority and publication service.</param>
    public CharacterCaptureHandler(
        ILiveStateApplication liveStateApplication)
    {
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
        else if (unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterIdentity)
        {
            ApplyIdentity(captureResult, context);
        }
        else if (unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterSupernaturalTraits)
        {
            ApplySupernaturalTraits(captureResult, context);
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
        Apply(UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], vitals, isResynchronizationBaseline, context);
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

        Apply(UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], value, captureResult.CorrelationId == 0, context);
    }

    /// <summary>Decodes and applies one complete Identity observation or its unavailable value.</summary>
    /// <param name="captureResult">The Identity capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplyIdentity(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

        CharacterIdentity? identity = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (!TryDecodeIdentity(captureResult.Payload, out identity))
            {
                return;
            }
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        Apply(UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], identity, captureResult.CorrelationId == 0, context);
    }

    /// <summary>Decodes and applies all three independent supernatural-traits predicates.</summary>
    /// <param name="captureResult">The supernatural-traits capture result.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void ApplySupernaturalTraits(IpcCaptureResultMessage captureResult, LiveCaptureContext context)
    {
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

        CharacterSupernaturalTraits? traits = null;
        if (captureResult.Availability == CaptureAvailability.Available)
        {
            if (captureResult.Payload.Length != Constants.CharacterSupernaturalTraitsPayloadLength
                || !TryDecodeBoolean(captureResult.Payload[0], out bool isVampire)
                || !TryDecodeBoolean(captureResult.Payload[1], out bool hasVampireLordForm)
                || !TryDecodeBoolean(captureResult.Payload[2], out bool hasWerewolfForm))
            {
                return;
            }

            traits = new CharacterSupernaturalTraits(isVampire, hasVampireLordForm, hasWerewolfForm);
        }
        else if (captureResult.Payload.Length != 0)
        {
            return;
        }

        Apply(UpdateMode.Snapshot, context.CaptureUnit.StateAreas[0], traits, captureResult.CorrelationId == 0, context);
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
        Apply(mode, context.CaptureUnit.StateAreas[0], value, isResynchronizationBaseline, context);
    }

    /// <summary>Passes a decoded area value and its validated context to shared Host authority.</summary>
    /// <typeparam name="TState">The decoded value type.</typeparam>
    /// <param name="mode">The area's canonical Snapshot or Event mode.</param>
    /// <param name="areaId">The destination state area.</param>
    /// <param name="value">The decoded value, or <see langword="null"/> when unavailable.</param>
    /// <param name="isResynchronizationBaseline">Whether the capture is an identified baseline sample.</param>
    /// <param name="context">The validated provenance and play-context metadata.</param>
    private void Apply<TState>(
        UpdateMode mode,
        StateAreaId areaId,
        TState value,
        bool isResynchronizationBaseline,
        LiveCaptureContext context) =>
        liveStateApplication.Apply(
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

    /// <summary>Decodes the bounded length-prefixed UTF-8 Identity payload without partial values.</summary>
    /// <param name="payload">The private payload bytes.</param>
    /// <param name="identity">The complete decoded identity when successful.</param>
    /// <returns><see langword="true"/> only for two non-empty, bounded, valid UTF-8 strings with no trailing bytes.</returns>
    private static bool TryDecodeIdentity(byte[] payload, out CharacterIdentity? identity)
    {
        identity = null;
        if (payload.Length < 4)
        {
            return false;
        }

        int nameLength = payload[0];
        if (nameLength == 0 || nameLength > Constants.MaxCharacterIdentityStringBytes)
        {
            return false;
        }

        int raceLengthOffset = 1 + nameLength;
        if (raceLengthOffset >= payload.Length)
        {
            return false;
        }

        int raceLength = payload[raceLengthOffset];
        if (raceLength == 0
            || raceLength > Constants.MaxCharacterIdentityStringBytes
            || payload.Length != raceLengthOffset + 1 + raceLength)
        {
            return false;
        }

        try
        {
            string name = StrictUtf8.GetString(payload.AsSpan(1, nameLength));
            string race = StrictUtf8.GetString(payload.AsSpan(raceLengthOffset + 1, raceLength));
            if (name.Contains('\0') || race.Contains('\0'))
            {
                return false;
            }

            identity = new CharacterIdentity(name, race);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Decodes one private payload boolean and rejects byte values other than zero or one.</summary>
    /// <param name="encoded">The encoded boolean byte.</param>
    /// <param name="value">The decoded value when the byte is valid.</param>
    /// <returns><see langword="true"/> when <paramref name="encoded"/> is zero or one.</returns>
    private static bool TryDecodeBoolean(byte encoded, out bool value)
    {
        value = encoded == 1;
        return encoded <= 1;
    }
}
