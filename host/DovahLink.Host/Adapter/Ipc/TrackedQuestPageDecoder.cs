using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.State;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>Validates and decodes the Adapter's fixed-capacity tracked-quest page payloads.</summary>
internal static class TrackedQuestPageDecoder
{
    /// <summary>Decodes strict UTF-8 without replacing malformed bytes.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Decodes one page of runtime tracked quest FormIDs.</summary>
    /// <param name="payload">The bounded Adapter page bytes.</param>
    /// <param name="questIds">The decoded nonzero IDs when the page is valid.</param>
    /// <param name="hasMore">Whether another ID page is required.</param>
    /// <returns>Whether the page is structurally complete and within its bounds.</returns>
    public static bool TryDecodeQuestIds(byte[] payload, out uint[] questIds, out bool hasMore)
    {
        questIds = [];
        hasMore = false;
        if (payload.Length is < 2 or > Constants.MaxTrackedQuestCapturePageBytes)
        {
            return false;
        }

        int count = payload[0];
        if (count > Constants.TrackedQuestIdsPerPage
            || payload[1] > 1
            || (payload[1] == 1 && count != Constants.TrackedQuestIdsPerPage)
            || (payload[1] == 1 && count == 0)
            || payload.Length != 2 + (count * sizeof(uint)))
        {
            return false;
        }

        uint[] decoded = new uint[count];
        for (int index = 0; index < count; index++)
        {
            uint questId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2 + index * sizeof(uint), sizeof(uint)));
            if (questId == 0)
            {
                return false;
            }

            decoded[index] = questId;
        }

        questIds = decoded;
        hasMore = payload[1] == 1;
        return true;
    }

    /// <summary>Decodes metadata for exactly the requested quest.</summary>
    /// <param name="payload">The bounded metadata page bytes.</param>
    /// <param name="expectedQuestId">The runtime quest ID named by the request.</param>
    /// <param name="title">The strict localized title when valid.</param>
    /// <param name="type">The raw Skyrim quest-type byte.</param>
    /// <param name="currentInstanceId">The raw current engine quest-instance ID.</param>
    /// <returns>Whether the complete metadata item has valid identity, title, and bounds.</returns>
    public static bool TryDecodeMetadata(
        byte[] payload,
        uint expectedQuestId,
        out string? title,
        out byte type,
        out uint currentInstanceId)
    {
        title = null;
        type = 0;
        currentInstanceId = 0;
        if (payload.Length < 10 || payload.Length > Constants.MaxTrackedQuestCapturePageBytes)
        {
            return false;
        }

        uint questId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, sizeof(uint)));
        uint decodedCurrentInstanceId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(5, sizeof(uint)));
        int titleLength = payload[9];
        if (questId == 0
            || questId != expectedQuestId
            || titleLength is < 1 or > Constants.MaxTrackedQuestTextBytes
            || payload.Length != 10 + titleLength)
        {
            return false;
        }

        if (!TryDecodeText(payload.AsSpan(10, titleLength), out string? decodedTitle) || string.IsNullOrEmpty(decodedTitle))
        {
            return false;
        }

        title = decodedTitle;
        type = payload[4];
        currentInstanceId = decodedCurrentInstanceId;
        return true;
    }

    /// <summary>Decodes one objective page, including every nullable authored text and raw state.</summary>
    /// <param name="payload">The bounded objective page bytes.</param>
    /// <param name="expectedQuestId">The runtime quest ID named by the request.</param>
    /// <param name="requestedCursor">The objective offset named by the request.</param>
    /// <param name="nextCursor">The first offset after the returned raw instance records.</param>
    /// <param name="hasMore">Whether another objective page is required.</param>
    /// <param name="objectives">The complete decoded page.</param>
    /// <returns>Whether the page contains only fully encoded objective records.</returns>
    public static bool TryDecodeObjectives(
        byte[] payload,
        uint expectedQuestId,
        ushort requestedCursor,
        out ushort nextCursor,
        out bool hasMore,
        out QuestObjective[] objectives)
    {
        nextCursor = 0;
        hasMore = false;
        objectives = [];
        if (payload.Length is < 8 or > Constants.MaxTrackedQuestCapturePageBytes)
        {
            return false;
        }

        uint questId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, sizeof(uint)));
        ushort pageCursor = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4, sizeof(ushort)));
        byte hasMoreByte = payload[6];
        int count = payload[7];
        if (questId == 0
            || questId != expectedQuestId
            || hasMoreByte > 1
            || count > (Constants.MaxTrackedQuestCapturePageBytes - 8) / 8
            || pageCursor != requestedCursor + count
            || pageCursor > Constants.MaxTrackedQuestObjectives
            || (hasMoreByte == 1 && (count == 0 || pageCursor >= Constants.MaxTrackedQuestObjectives)))
        {
            return false;
        }

        QuestObjective[] decoded = new QuestObjective[count];
        int offset = 8;
        for (int index = 0; index < count; index++)
        {
            if (payload.Length - offset < 8)
            {
                return false;
            }

            ushort objectiveIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset, sizeof(ushort)));
            uint instanceId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 2, sizeof(uint)));
            byte rawState = payload[offset + 6];
            byte textLength = payload[offset + 7];
            offset += 8;
            if (!TryDecodeState(rawState, out TrackedQuestObjectiveState state))
            {
                return false;
            }

            string? text = null;
            if (textLength != byte.MaxValue)
            {
                if (textLength > Constants.MaxTrackedQuestTextBytes || payload.Length - offset < textLength)
                {
                    return false;
                }

                if (!TryDecodeText(payload.AsSpan(offset, textLength), out text))
                {
                    return false;
                }

                offset += textLength;
            }

            decoded[index] = new QuestObjective(objectiveIndex, instanceId, text, state);
        }

        if (offset != payload.Length)
        {
            return false;
        }

        nextCursor = pageCursor;
        hasMore = hasMoreByte == 1;
        objectives = decoded;
        return true;
    }

    /// <summary>Decodes one length-bounded localized UTF-8 string and rejects embedded NUL.</summary>
    /// <param name="bytes">The exact UTF-8 byte slice.</param>
    /// <param name="text">The decoded string when valid.</param>
    /// <returns>Whether the byte slice decodes strictly and contains no NUL.</returns>
    private static bool TryDecodeText(ReadOnlySpan<byte> bytes, out string? text)
    {
        text = null;
        try
        {
            string decoded = StrictUtf8.GetString(bytes);
            if (decoded.Contains('\0'))
            {
                return false;
            }

            text = decoded;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Maps exactly Skyrim's six raw objective state values.</summary>
    /// <param name="rawState">The state byte copied by the Adapter.</param>
    /// <param name="state">The corresponding typed state when recognized.</param>
    /// <returns>Whether <paramref name="rawState"/> is one of the pinned engine states.</returns>
    private static bool TryDecodeState(byte rawState, out TrackedQuestObjectiveState state)
    {
        switch (rawState)
        {
            case 0: state = TrackedQuestObjectiveState.Dormant; return true;
            case 1: state = TrackedQuestObjectiveState.Displayed; return true;
            case 2: state = TrackedQuestObjectiveState.Completed; return true;
            case 3: state = TrackedQuestObjectiveState.CompletedAndDisplayed; return true;
            case 4: state = TrackedQuestObjectiveState.Failed; return true;
            case 5: state = TrackedQuestObjectiveState.FailedAndDisplayed; return true;
            default: state = default; return false;
        }
    }
}
