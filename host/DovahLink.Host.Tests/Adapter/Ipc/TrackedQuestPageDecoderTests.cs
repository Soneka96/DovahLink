using System.Buffers.Binary;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests strict decoding of the Adapter's bounded tracked-quest page payloads.</summary>
public class TrackedQuestPageDecoderTests
{
    /// <summary>Decodes little-endian FormIDs and the page continuation flag.</summary>
    [Fact]
    public void TryDecodeQuestIds_AvailableEmptyAndMultiplePages()
    {
        Assert.True(TrackedQuestPageDecoder.TryDecodeQuestIds([0, 0], out uint[] empty, out bool emptyHasMore));
        Assert.Empty(empty);
        Assert.False(emptyHasMore);

        byte[] payload = new byte[2 + (Constants.TrackedQuestIdsPerPage * sizeof(uint))];
        payload[0] = Constants.TrackedQuestIdsPerPage;
        payload[1] = 1;
        for (int index = 0; index < Constants.TrackedQuestIdsPerPage; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2 + (index * sizeof(uint))), (uint)index + 1);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2 + ((Constants.TrackedQuestIdsPerPage - 1) * sizeof(uint))), 0x90ABCDEF);
        Assert.True(TrackedQuestPageDecoder.TryDecodeQuestIds(payload, out uint[] ids, out bool hasMore));
        Assert.Equal(Constants.TrackedQuestIdsPerPage, ids.Length);
        Assert.Equal(0x12345678u, ids[0]);
        Assert.Equal(0x90ABCDEFu, ids[^1]);
        Assert.True(hasMore);
    }

    /// <summary>Rejects malformed page counts, IDs, continuation flags, and payload sizes.</summary>
    [Fact]
    public void TryDecodeQuestIds_MalformedPagesFailClosed()
    {
        Assert.False(TrackedQuestPageDecoder.TryDecodeQuestIds([0, 1], out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeQuestIds([1, 1, 1, 0, 0, 0], out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeQuestIds([1, 0, 0, 0, 0, 0], out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeQuestIds([0, 2], out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeQuestIds(new byte[256], out _, out _));
    }

    /// <summary>Preserves raw quest type, current instance ID, and strict localized title.</summary>
    [Fact]
    public void TryDecodeMetadata_PreservesEngineFactsAndUtf8Title()
    {
        const string title = "Draugr — Jarl's Favor";
        byte[] titleBytes = System.Text.Encoding.UTF8.GetBytes(title);
        byte[] payload = new byte[10 + titleBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x12345678);
        payload[4] = 0xFE;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), 0x90ABCDEF);
        payload[9] = (byte)titleBytes.Length;
        titleBytes.CopyTo(payload, 10);

        bool decoded = TrackedQuestPageDecoder.TryDecodeMetadata(
            payload, 0x12345678, out string? actualTitle, out byte type, out uint currentInstanceId);

        Assert.True(decoded);
        Assert.Equal(title, actualTitle);
        Assert.Equal((byte)0xFE, type);
        Assert.Equal(0x90ABCDEFu, currentInstanceId);
    }

    /// <summary>Rejects metadata with a mismatched ID, absent title, malformed UTF-8, or an extra tail.</summary>
    [Fact]
    public void TryDecodeMetadata_InvalidFactsFailClosed()
    {
        byte[] payload = [1, 0, 0, 0, 8, 0, 0, 0, 0, 1, (byte)'Q'];
        Assert.False(TrackedQuestPageDecoder.TryDecodeMetadata(payload, 2, out _, out _, out _));

        payload[9] = 0;
        Assert.False(TrackedQuestPageDecoder.TryDecodeMetadata(payload, 1, out _, out _, out _));

        payload[9] = 2;
        payload = [1, 0, 0, 0, 8, 0, 0, 0, 0, 2, 0xC0, 0xAF];
        Assert.False(TrackedQuestPageDecoder.TryDecodeMetadata(payload, 1, out _, out _, out _));

        payload = [1, 0, 0, 0, 8, 0, 0, 0, 0, 1, (byte)'Q', 0];
        Assert.False(TrackedQuestPageDecoder.TryDecodeMetadata(payload, 1, out _, out _, out _));
    }

    /// <summary>Decodes all six engine objective states and nullable/localized objective text.</summary>
    [Fact]
    public void TryDecodeObjectives_PreservesAllEngineStatesAndText()
    {
        List<byte> payload = BuildObjectivePage(0x12345678, 6, false,
        [
            (1, 5u, (byte)0, (string?)null),
            (2, 5u, (byte)1, "Dragonborn — meet Delphine"),
            (3, 5u, (byte)2, (string?)null),
            (4, 5u, (byte)3, (string?)null),
            (5, 5u, (byte)4, (string?)null),
            (6, 5u, (byte)5, (string?)null),
        ]);

        Assert.Equal((byte)6, payload[7]);
        Assert.Equal((ushort)6, BinaryPrimitives.ReadUInt16LittleEndian(payload.Skip(4).Take(2).ToArray()));

        bool decoded = TrackedQuestPageDecoder.TryDecodeObjectives(
            payload.ToArray(), 0x12345678, 0, out ushort cursor, out bool hasMore, out QuestObjective[] objectives);

        Assert.True(decoded);
        Assert.Equal((ushort)6, cursor);
        Assert.False(hasMore);
        Assert.Equal(
            [
                TrackedQuestObjectiveState.Dormant,
                TrackedQuestObjectiveState.Displayed,
                TrackedQuestObjectiveState.Completed,
                TrackedQuestObjectiveState.CompletedAndDisplayed,
                TrackedQuestObjectiveState.Failed,
                TrackedQuestObjectiveState.FailedAndDisplayed,
            ],
            objectives.Select(objective => objective.State));
        Assert.Null(objectives[0].Text);
        Assert.Equal("Dragonborn — meet Delphine", objectives[1].Text);
    }

    /// <summary>Rejects objective-page identity, cursor, state, text, and exact-length violations.</summary>
    [Fact]
    public void TryDecodeObjectives_MalformedPagesFailClosed()
    {
        byte[] valid = BuildObjectivePage(1, 1, true, [(1, 1u, (byte)0, (string?)null)]).ToArray();
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives(valid, 2, 1, out _, out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives(valid, 1, 1, out _, out _, out _));

        byte[] unknownState = BuildObjectivePage(1, 1, false, [(1, 1u, (byte)6, (string?)null)]).ToArray();
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives(unknownState, 1, 0, out _, out _, out _));

        byte[] invalidUtf8 = BuildObjectivePage(1, 1, false, [(1, 1u, (byte)0, "Q")]).ToArray();
        invalidUtf8[^1] = 0xC0;
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives(invalidUtf8, 1, 0, out _, out _, out _));

        byte[] trailing = BuildObjectivePage(1, 1, false, [(1, 1u, (byte)0, (string?)null)]).ToArray();
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives([.. trailing, 0], 1, 0, out _, out _, out _));
        Assert.False(TrackedQuestPageDecoder.TryDecodeObjectives(new byte[256], 1, 0, out _, out _, out _));
    }

    /// <summary>Builds one private objective page using the checked binary layout.</summary>
    /// <param name="questId">The runtime quest FormID.</param>
    /// <param name="nextCursor">The first raw instance offset after the returned records.</param>
    /// <param name="hasMore">Whether another page follows.</param>
    /// <param name="objectives">Index, instance ID, raw state, and nullable text values.</param>
    /// <returns>The exact little-endian page bytes.</returns>
    private static List<byte> BuildObjectivePage(uint questId, ushort nextCursor, bool hasMore,
        IEnumerable<(ushort Index, uint InstanceId, byte State, string? Text)> objectives)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, questId);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), nextCursor);
        header[6] = hasMore ? (byte)1 : (byte)0;
        var payload = new List<byte>(header);
        foreach ((ushort index, uint instanceId, byte state, string? text) in objectives)
        {
            byte[] objectiveHeader = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(objectiveHeader, index);
            BinaryPrimitives.WriteUInt32LittleEndian(objectiveHeader.AsSpan(2), instanceId);
            objectiveHeader[6] = state;
            byte[] textBytes = text is null ? [] : System.Text.Encoding.UTF8.GetBytes(text);
            objectiveHeader[7] = text is null ? byte.MaxValue : (byte)textBytes.Length;
            payload.AddRange(objectiveHeader);
            payload.AddRange(textBytes);
            payload[7]++;
        }

        return payload;
    }
}
