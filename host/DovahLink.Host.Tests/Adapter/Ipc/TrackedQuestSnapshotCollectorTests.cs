using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests complete tracked-quest collection across bounded private pages.</summary>
public class TrackedQuestSnapshotCollectorTests
{
    /// <summary>Verifies a 65-quest collection reads all pages and the final consistency set.</summary>
    [Fact]
    public async Task CollectAsync_UsesEveryIdPageForInitialAndFinalSets()
    {
        uint[] engineOrderIds = Enumerable.Range(1, 65).Select(id => (uint)id).Reverse().ToArray();
        var fixture = new CollectorFixture(engineOrderIds);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(result);
        Assert.Equal(Enumerable.Range(1, 65).Select(id => (uint)id), snapshot.Quests.Select(quest => quest.QuestId));
        Assert.Equal(65, snapshot.Quests.Select(quest => quest.QuestId).Distinct().Count());
        Assert.Equal(
            [0, 32, 64, 0, 32, 64],
            fixture.PageReader.ReadPageCalls
                .Where(call => call.Kind == TrackedQuestPageKind.TrackedQuestIds)
                .Select(call => (int)call.Cursor));
    }

    /// <summary>Verifies the Host accepts the exact complete-quest collection limit.</summary>
    [Fact]
    public async Task CollectAsync_AcceptsMaximumTrackedQuestCount()
    {
        uint[] questIds = Enumerable.Range(1, Constants.MaxTrackedQuests).Select(id => (uint)id).ToArray();
        var fixture = new CollectorFixture(questIds);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(result);
        Assert.Equal(Constants.MaxTrackedQuests, snapshot.Quests.Count);
    }

    /// <summary>Verifies the Host rejects a complete tracked-ID sequence above its collection limit.</summary>
    [Fact]
    public async Task CollectAsync_RejectsMoreThanMaximumTrackedQuests()
    {
        uint[] engineOrderIds = Enumerable.Range(1, Constants.MaxTrackedQuests + 1)
            .Select(id => (uint)id)
            .ToArray();
        var fixture = new CollectorFixture(engineOrderIds);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(
            [0, 32, 64, 96],
            fixture.PageReader.ReadPageCalls
                .Where(call => call.Kind == TrackedQuestPageKind.TrackedQuestIds)
                .Select(call => (int)call.Cursor));
        Assert.DoesNotContain(fixture.PageReader.ReadPageCalls, call => call.Kind == TrackedQuestPageKind.QuestMetadata);
    }

    /// <summary>Verifies identical duplicate engine IDs follow the existing set-based deduplication behavior.</summary>
    [Fact]
    public async Task CollectAsync_DeduplicatesIdenticalQuestIds()
    {
        var fixture = new CollectorFixture([10, 10]);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(result);
        Assert.Equal([10u], snapshot.Quests.Select(quest => quest.QuestId));
        Assert.Equal(2, fixture.MetadataReadCounts[10]);
    }

    /// <summary>Verifies old-instance rows count toward raw page cursors and are filtered after paging.</summary>
    [Fact]
    public async Task CollectAsync_ObjectivePagesAdvanceByRawRecordsBeforeInstanceFiltering()
    {
        var fixture = new CollectorFixture([10]);
        fixture.MetadataForRead = (_, _) => ("Localized quest", 8, 2);
        fixture.SetObjectiveFacts(10, Enumerable.Range(0, 31)
            .Select(index => ((ushort)index, index % 2 == 0 ? 1u : 2u, (byte)1, (string?)null))
            .ToArray());

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(result);
        Assert.Equal([0, 30], fixture.PageReader.ReadPageCalls
            .Where(call => call.Kind == TrackedQuestPageKind.Objectives)
            .Select(call => (int)call.Cursor));
        Assert.Equal(Enumerable.Range(1, 15).Select(index => (ushort)(index * 2 - 1)),
            snapshot.Quests[0].Objectives.Select(objective => objective.Index));
        Assert.All(snapshot.Quests[0].Objectives, objective => Assert.Equal(2u, objective.InstanceId));
    }

    /// <summary>Verifies the Host accepts exactly the complete raw-objective limit.</summary>
    [Fact]
    public async Task CollectAsync_AcceptsMaximumRawObjectiveCount()
    {
        var fixture = new CollectorFixture([10]);
        fixture.SetObjectiveFacts(10, Enumerable.Range(0, Constants.MaxTrackedQuestObjectives)
            .Select(index => ((ushort)index, 1u, (byte)0, (string?)null))
            .ToArray());

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(result);
        Assert.Equal(Constants.MaxTrackedQuestObjectives, snapshot.Quests[0].Objectives.Count);
    }

    /// <summary>Verifies raw objective records from multiple old quest instances share one Host limit.</summary>
    [Fact]
    public async Task CollectAsync_RejectsRawObjectiveLimitExceededAcrossQuests()
    {
        var fixture = new CollectorFixture([1, 2]);
        (ushort Index, uint InstanceId, byte State, string? Text)[] oldInstanceFacts =
            Enumerable.Range(0, 600)
                .Select(index => ((ushort)index, 2u, (byte)0, (string?)null))
                .ToArray();
        fixture.SetObjectiveFacts(1, oldInstanceFacts);
        fixture.SetObjectiveFacts(2, oldInstanceFacts);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        Assert.Null(result);
        Assert.True(fixture.PageReader.ReadPageCalls.Count(call => call.Kind == TrackedQuestPageKind.Objectives && call.QuestId == 1) > 0);
        Assert.True(fixture.PageReader.ReadPageCalls.Count(call => call.Kind == TrackedQuestPageKind.Objectives && call.QuestId == 2) > 0);
    }

    /// <summary>Verifies metadata changing across the objective read invalidates the complete collection.</summary>
    [Fact]
    public async Task CollectAsync_CurrentInstanceChangeDuringCaptureReturnsUnavailable()
    {
        var fixture = new CollectorFixture([10]);
        fixture.MetadataForRead = (_, readCount) =>
            ("Localized quest", (byte)8, readCount == 1 ? 1u : 2u);

        TrackedQuests? result = await fixture.Collector.CollectAsync(
            fixture.Source, fixture.PlayContext, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(2, fixture.MetadataReadCounts[10]);
    }

    /// <summary>Verifies title and raw type changes between metadata reads invalidate the collection.</summary>
    [Fact]
    public async Task CollectAsync_TitleOrTypeChangeDuringCaptureReturnsUnavailable()
    {
        var changedTitle = new CollectorFixture([10]);
        changedTitle.MetadataForRead = (_, readCount) =>
            (readCount == 1 ? "Localized quest" : "Changed title", 8, 1);
        var changedType = new CollectorFixture([10]);
        changedType.MetadataForRead = (_, readCount) =>
            ("Localized quest", readCount == 1 ? (byte)8 : (byte)9, 1);

        Assert.Null(await changedTitle.Collector.CollectAsync(
            changedTitle.Source, changedTitle.PlayContext, CancellationToken.None));
        Assert.Null(await changedType.Collector.CollectAsync(
            changedType.Source, changedType.PlayContext, CancellationToken.None));
    }

    /// <summary>Builds deterministic private pages for a complete collection scenario.</summary>
    private sealed class CollectorFixture
    {
        /// <summary>The engine-order tracked IDs returned by each ID scan.</summary>
        private readonly uint[] questIds;

        /// <summary>The number of metadata reads completed for each quest.</summary>
        private readonly Dictionary<uint, int> metadataReadCounts = [];

        /// <summary>The raw objective records returned for each quest.</summary>
        private readonly Dictionary<uint, (ushort Index, uint InstanceId, byte State, string? Text)[]> objectiveFactsByQuest = [];

        /// <summary>The Adapter identity and connection generation supplied to the collector.</summary>
        private readonly AdapterCaptureSource source = new(AdapterInstanceId.NewId(), 1);

        /// <summary>The play context and transition generation supplied to the collector.</summary>
        private readonly PlayContextSnapshot playContext = new(PlayContextId.NewId(), 3);

        /// <summary>The page capture catalog entry used in returned contexts.</summary>
        private readonly CaptureUnitDefinition captureUnit = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)TrackedQuestCaptureKey.Page);

        /// <summary>The next correlation assigned to a returned page.</summary>
        private ulong nextCorrelationId = 1;

        /// <summary>The complete-snapshot collector under test.</summary>
        public TrackedQuestSnapshotCollector Collector { get; }

        /// <summary>The page reader that records every requested operation.</summary>
        public FakeTrackedQuestPageReader PageReader { get; } = new();

        /// <summary>The captured Adapter source.</summary>
        public AdapterCaptureSource Source => source;

        /// <summary>The captured play context.</summary>
        public PlayContextSnapshot PlayContext => playContext;

        /// <summary>The number of metadata pages read for each quest.</summary>
        public IReadOnlyDictionary<uint, int> MetadataReadCounts => metadataReadCounts;

        /// <summary>Supplies each metadata read's title, type, and current instance identity.</summary>
        public Func<uint, int, (string Title, byte Type, uint CurrentInstanceId)> MetadataForRead { get; set; } =
            (_, _) => ("Localized quest", 8, 1);

        /// <summary>Sets the raw objective records returned for one quest.</summary>
        /// <param name="questId">The runtime FormID owning the records.</param>
        /// <param name="facts">The raw page sequence, including records from prior instances.</param>
        public void SetObjectiveFacts(
            uint questId,
            (ushort Index, uint InstanceId, byte State, string? Text)[] facts) =>
            objectiveFactsByQuest[questId] = facts;

        /// <summary>Creates the collector and binds the scripted page source.</summary>
        /// <param name="questIds">The tracked IDs in Adapter enumeration order.</param>
        public CollectorFixture(uint[] questIds)
        {
            this.questIds = questIds;
            PageReader.ReadPageOverride = ReadPageAsync;
            Collector = new TrackedQuestSnapshotCollector(PageReader);
        }

        /// <summary>Builds one matching capture response for a requested page.</summary>
        /// <param name="requestSource">The source expected by the collector.</param>
        /// <param name="requestPlayContext">The play context expected by the collector.</param>
        /// <param name="kind">The requested page kind.</param>
        /// <param name="questId">The requested quest ID, or zero for ID pages.</param>
        /// <param name="cursor">The requested page cursor.</param>
        /// <param name="cancellationToken">The collection lifetime token.</param>
        /// <returns>The bounded response context.</returns>
        private Task<LiveCaptureContext?> ReadPageAsync(
            AdapterCaptureSource requestSource,
            PlayContextSnapshot requestPlayContext,
            TrackedQuestPageKind kind,
            uint questId,
            ushort cursor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] payload = kind switch
            {
                TrackedQuestPageKind.TrackedQuestIds => BuildIdsPage(cursor),
                TrackedQuestPageKind.QuestMetadata => BuildMetadataPage(questId),
                TrackedQuestPageKind.Objectives => BuildObjectivePage(questId, cursor),
                _ => throw new InvalidOperationException("The collector requested an unknown page kind."),
            };
            PlayContextId responsePlayContext = requestPlayContext.Current
                ?? throw new InvalidOperationException("The collector requested a page without an active play context.");
            var result = new IpcCaptureResultMessage(
                nextCorrelationId++, CaptureSourceKind.Sample, (uint)TrackedQuestCaptureKey.Page,
                CaptureAvailability.Available, responsePlayContext, payload);
            var adapterSnapshot = new AdapterAvailabilitySnapshot(
                AdapterAvailability.Available, requestSource.InstanceId, true, requestSource.ConnectionGeneration);
            return Task.FromResult<LiveCaptureContext?>(new LiveCaptureContext(
                result, requestSource, captureUnit, adapterSnapshot, responsePlayContext,
                requestPlayContext.TransitionGeneration, DateTimeOffset.UtcNow));
        }

        /// <summary>Encodes one ID page in engine enumeration order.</summary>
        /// <param name="cursor">The first tracked-ID offset requested.</param>
        /// <returns>The count, continuation flag, and little-endian FormIDs.</returns>
        private byte[] BuildIdsPage(ushort cursor)
        {
            int start = cursor;
            int count = Math.Min(Constants.TrackedQuestIdsPerPage, questIds.Length - start);
            bool hasMore = start + count < questIds.Length;
            byte[] payload = new byte[2 + (count * sizeof(uint))];
            payload[0] = checked((byte)count);
            payload[1] = hasMore ? (byte)1 : (byte)0;
            for (int index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2 + (index * sizeof(uint))), questIds[start + index]);
            }

            return payload;
        }

        /// <summary>Encodes one metadata response and advances that quest's metadata read count.</summary>
        /// <param name="questId">The requested runtime FormID.</param>
        /// <returns>The bounded metadata page bytes.</returns>
        private byte[] BuildMetadataPage(uint questId)
        {
            metadataReadCounts.TryGetValue(questId, out int readCount);
            metadataReadCounts[questId] = ++readCount;
            (string title, byte type, uint currentInstanceId) = MetadataForRead(questId, readCount);
            byte[] text = Encoding.UTF8.GetBytes(title);
            byte[] payload = new byte[10 + text.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, questId);
            payload[4] = type;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), currentInstanceId);
            payload[9] = checked((byte)text.Length);
            text.CopyTo(payload, 10);
            return payload;
        }

        /// <summary>Encodes one byte-bounded objective page at the requested raw-record cursor.</summary>
        /// <param name="questId">The requested runtime FormID.</param>
        /// <param name="cursor">The first raw objective offset requested.</param>
        /// <returns>The exact page bytes and raw continuation metadata.</returns>
        private byte[] BuildObjectivePage(uint questId, ushort cursor)
        {
            if (!objectiveFactsByQuest.TryGetValue(questId, out var objectiveFacts))
            {
                objectiveFacts = [];
            }

            List<(ushort Index, uint InstanceId, byte State, string? Text, byte[] TextBytes)> page = [];
            int position = 8;
            int offset = cursor;
            while (offset < objectiveFacts.Length)
            {
                (ushort index, uint instanceId, byte state, string? text) = objectiveFacts[offset];
                byte[] textBytes = text is null ? [] : Encoding.UTF8.GetBytes(text);
                int entryLength = 8 + textBytes.Length;
                if (position + entryLength > Constants.MaxTrackedQuestCapturePageBytes)
                {
                    break;
                }

                page.Add((index, instanceId, state, text, textBytes));
                position += entryLength;
                offset++;
            }

            ushort nextCursor = checked((ushort)(cursor + page.Count));
            bool hasMore = nextCursor < objectiveFacts.Length;
            byte[] payload = new byte[position];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, questId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), nextCursor);
            payload[6] = hasMore ? (byte)1 : (byte)0;
            payload[7] = checked((byte)page.Count);
            position = 8;
            foreach ((ushort index, uint instanceId, byte state, string? text, byte[] textBytes) in page)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(position), index);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(position + 2), instanceId);
                payload[position + 6] = state;
                payload[position + 7] = text is null ? byte.MaxValue : checked((byte)textBytes.Length);
                position += 8;
                textBytes.CopyTo(payload, position);
                position += textBytes.Length;
            }

            return payload;
        }
    }
}
