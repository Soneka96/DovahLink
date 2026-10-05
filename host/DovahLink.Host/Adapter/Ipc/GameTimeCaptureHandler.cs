using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Validates raw calendar globals and normalizes them into one authoritative Snapshot value.</summary>
public sealed class GameTimeCaptureHandler : ILiveCaptureHandler
{
    /// <summary>Decodes month names without replacement characters.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The one capture identity owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.GameTime),
        ]);

    /// <summary>Publishes a normalized calendar or its explicit unavailability.</summary>
    private readonly IStatePublisher<GameTime?> publisher;

    /// <summary>Applies values through Host authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Creates the Host mapping for Skyrim game time.</summary>
    /// <param name="publisher">The typed publisher for the independent calendar area.</param>
    /// <param name="liveStateApplication">The shared Host authority and publication service.</param>
    public GameTimeCaptureHandler(
        IStatePublisher<GameTime?> publisher,
        ILiveStateApplication liveStateApplication)
    {
        this.publisher = publisher;
        this.liveStateApplication = liveStateApplication;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures => supportedCaptures;

    /// <inheritdoc/>
    public void Handle(LiveCaptureContext context)
    {
        IpcCaptureResultMessage capture = context.CaptureResult;
        if (context.CaptureUnit.StateAreas.Count != 1)
        {
            return;
        }

        GameTime? value = null;
        if (capture.Availability == CaptureAvailability.Available)
        {
            if (!TryDecode(capture.Payload, out value))
            {
                return;
            }
        }
        else if (capture.Payload.Length != 0)
        {
            return;
        }

        liveStateApplication.Apply(
            publisher,
            UpdateMode.Snapshot,
            context.CaptureUnit.StateAreas[0],
            value,
            capture.CorrelationId == 0,
            context.Source,
            context.AdapterSnapshot,
            context.PlayContextId,
            context.PlayContextGeneration,
            context.OccurredAt);
    }

    /// <summary>Validates the four raw globals and maps them to the public Skyrim calendar fields.</summary>
    /// <param name="payload">The four little-endian raw floats followed by a bounded localized month name.</param>
    /// <param name="gameTime">The normalized calendar when every required value is valid.</param>
    /// <returns><see langword="true"/> only for a complete calendar observation.</returns>
    private static bool TryDecode(byte[] payload, out GameTime? gameTime)
    {
        gameTime = null;
        const int monthNameOffset = sizeof(float) * 4;
        const int payloadHeaderBytes = monthNameOffset + 1;
        if (payload.Length < payloadHeaderBytes)
        {
            return false;
        }

        float rawYear = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(0, sizeof(float)));
        float rawMonth = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(sizeof(float), sizeof(float)));
        float rawDay = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(sizeof(float) * 2, sizeof(float)));
        float rawHour = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(sizeof(float) * 3, sizeof(float)));
        int monthNameLength = payload[monthNameOffset];
        if (monthNameLength == 0
            || monthNameLength > Constants.MaxGameMonthNameBytes
            || payload.Length != payloadHeaderBytes + monthNameLength
            || !float.IsFinite(rawYear)
            || !float.IsFinite(rawMonth)
            || !float.IsFinite(rawDay)
            || !float.IsFinite(rawHour)
            || rawYear < 0
            || (double)rawYear > int.MaxValue
            || MathF.Truncate(rawYear) != rawYear
            || rawMonth < 0
            || rawMonth >= 12
            || MathF.Truncate(rawMonth) != rawMonth
            || rawDay < 1
            || rawDay > 31
            || MathF.Truncate(rawDay) != rawDay
            || rawHour < 0
            || rawHour >= 24)
        {
            return false;
        }

        string monthName;
        try
        {
            monthName = StrictUtf8.GetString(payload.AsSpan(payloadHeaderBytes, monthNameLength));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        if (monthName.Contains('\0'))
        {
            return false;
        }

        int hour = (int)MathF.Floor(rawHour);
        int minute = (int)Math.Floor(((double)rawHour - hour) * 60.0);
        if (minute is < 0 or > 59)
        {
            return false;
        }

        gameTime = new GameTime((int)rawYear, (int)rawMonth + 1, monthName, (int)rawDay, hour, minute);
        return true;
    }
}
