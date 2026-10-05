using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Decodes raw player-location facts and maps them into one authoritative Snapshot value.</summary>
public sealed class PlayerLocationCaptureHandler : ILiveCaptureHandler
{
    /// <summary>Decodes bounded UTF-8 names without replacement characters.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The one capture identity owned by this handler.</summary>
    private static readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures =
        Array.AsReadOnly<(CaptureSourceKind Source, uint CaptureKey)>(
        [
            (CaptureSourceKind.Sample, (uint)CharacterSampleToken.PlayerLocation),
        ]);

    /// <summary>Publishes the typed player-location value or its explicit unavailability.</summary>
    private readonly IStatePublisher<PlayerLocation?> publisher;

    /// <summary>Applies values through Host authority and publication rules.</summary>
    private readonly ILiveStateApplication liveStateApplication;

    /// <summary>Creates the Host mapping for player-location captures.</summary>
    /// <param name="publisher">The typed publisher for the independent location area.</param>
    /// <param name="liveStateApplication">The shared Host authority and publication service.</param>
    public PlayerLocationCaptureHandler(
        IStatePublisher<PlayerLocation?> publisher,
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

        PlayerLocation? value = null;
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

    /// <summary>Decodes the bounded private payload and selects the semantic location source.</summary>
    /// <param name="payload">The Adapter's little-endian engine-fact payload.</param>
    /// <param name="location">The complete typed public value when decoding succeeds.</param>
    /// <returns><see langword="true"/> only for a complete, well-formed capture.</returns>
    private static bool TryDecode(byte[] payload, out PlayerLocation? location)
    {
        location = null;
        int offset = 0;
        if (!TryReadFormId(payload, ref offset, out uint cellId)
            || cellId == 0
            || offset >= payload.Length
            || payload[offset] > 1)
        {
            return false;
        }

        PlayerLocationCellKind cellKind = payload[offset++] == 1
            ? PlayerLocationCellKind.Interior
            : PlayerLocationCellKind.Exterior;
        if (!TryReadName(payload, ref offset, out string? cellName)
            || !TryReadFormId(payload, ref offset, out uint playerLocationId)
            || !TryReadName(payload, ref offset, out string? playerLocationName)
            || !TryReadFormId(payload, ref offset, out uint cellLocationId)
            || !TryReadName(payload, ref offset, out string? cellLocationName)
            || !TryReadFormId(payload, ref offset, out uint worldspaceId)
            || !TryReadName(payload, ref offset, out string? worldspaceName)
            || offset != payload.Length
            || (playerLocationId == 0 && playerLocationName is not null)
            || (cellLocationId == 0 && cellLocationName is not null)
            || (worldspaceId == 0 && worldspaceName is not null))
        {
            return false;
        }

        uint? selectedLocationId = playerLocationId != 0
            ? playerLocationId
            : cellLocationId != 0 ? cellLocationId : null;
        string? selectedLocationName = playerLocationId != 0
            ? playerLocationName
            : cellLocationId != 0 ? cellLocationName : null;
        location = new PlayerLocation(
            cellId,
            cellKind,
            cellName,
            selectedLocationId,
            selectedLocationName,
            worldspaceId == 0 ? null : worldspaceId,
            worldspaceName);
        return true;
    }

    /// <summary>Reads one little-endian runtime FormID.</summary>
    /// <param name="payload">The complete capture bytes.</param>
    /// <param name="offset">The next unread byte, advanced on success.</param>
    /// <param name="formId">The decoded FormID when four bytes are available.</param>
    /// <returns><see langword="true"/> when one FormID is available.</returns>
    private static bool TryReadFormId(byte[] payload, ref int offset, out uint formId)
    {
        formId = 0;
        if (payload.Length - offset < sizeof(uint))
        {
            return false;
        }
        formId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, sizeof(uint)));
        offset += sizeof(uint);
        return true;
    }

    /// <summary>Reads one bounded UTF-8 name, where a zero length represents absence.</summary>
    /// <param name="payload">The complete capture bytes.</param>
    /// <param name="offset">The next unread byte, advanced on success.</param>
    /// <param name="name">The decoded name or <see langword="null"/> when absent.</param>
    /// <returns><see langword="true"/> when the length, bytes, and UTF-8 are valid.</returns>
    private static bool TryReadName(byte[] payload, ref int offset, out string? name)
    {
        name = null;
        if (offset >= payload.Length)
        {
            return false;
        }
        int length = payload[offset++];
        if (length == 0)
        {
            return true;
        }
        if (length > Constants.MaxPlayerLocationNameBytes || payload.Length - offset < length)
        {
            return false;
        }
        try
        {
            string decoded = StrictUtf8.GetString(payload.AsSpan(offset, length));
            if (decoded.Contains('\0'))
            {
                return false;
            }
            name = decoded;
            offset += length;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
