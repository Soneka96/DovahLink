using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests complete Host assembly and publication of bounded tracked-quest pages.</summary>
public class TrackedQuestCaptureCoordinatorTests
{
    /// <summary>Verifies no complete capture starts without an active play context.</summary>
    [Fact]
    public async Task RunAsync_NoActivePlayContextDoesNotCollect()
    {
        var fixture = new CoordinatorCadenceFixture(hasActivePlayContext: false);
        using var shutdown = new CancellationTokenSource();
        Task runTask = fixture.Coordinator.RunAsync(shutdown.Token);

        Assert.Equal(0, fixture.Collector.Calls);
        Assert.Empty(fixture.Application.Calls);
        Assert.False(runTask.IsCompleted);
        shutdown.Cancel();
        await runTask;
    }

    /// <summary>Verifies capture cycles are serialized and separated by a full Slow interval.</summary>
    [Fact]
    public async Task RunAsync_SerializesCapturesAndWaitsSlowIntervalAfterEachAttempt()
    {
        var fixture = new CoordinatorCadenceFixture(hasActivePlayContext: true);
        using var shutdown = new CancellationTokenSource();
        Task runTask = fixture.Coordinator.RunAsync(shutdown.Token);
        await fixture.Collector.SecondCallStarted.Task.WaitAsync(
            Constants.LiveStateSlowSampleInterval + TimeSpan.FromSeconds(3));

        IReadOnlyList<long> callTimes = fixture.Collector.CallTimes;
        Assert.Equal(2, callTimes.Count);
        Assert.True(
            Stopwatch.GetElapsedTime(callTimes[0], callTimes[1])
                >= Constants.LiveStateSlowSampleInterval - TimeSpan.FromMilliseconds(50));
        Assert.Equal(1, fixture.Collector.MaximumConcurrentCalls);

        shutdown.Cancel();
        await runTask;
    }

    /// <summary>Verifies a capture longer than the cadence is followed by a full Slow interval.</summary>
    [Fact]
    public async Task RunAsync_LongCaptureDoesNotStartACatchUpCycle()
    {
        TimeSpan captureDuration = Constants.LiveStateSlowSampleInterval + TimeSpan.FromMilliseconds(200);
        var fixture = new CoordinatorCadenceFixture(hasActivePlayContext: true);
        fixture.Collector.FirstCaptureDuration = captureDuration;
        using var shutdown = new CancellationTokenSource();
        Task runTask = fixture.Coordinator.RunAsync(shutdown.Token);
        await fixture.Collector.SecondCallStarted.Task.WaitAsync(
            captureDuration + Constants.LiveStateSlowSampleInterval + TimeSpan.FromSeconds(3));

        IReadOnlyList<long> callTimes = fixture.Collector.CallTimes;
        Assert.True(
            Stopwatch.GetElapsedTime(callTimes[0], callTimes[1])
                >= captureDuration + Constants.LiveStateSlowSampleInterval - TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, fixture.Collector.MaximumConcurrentCalls);

        shutdown.Cancel();
        await runTask;
    }

    /// <summary>Verifies sorting, current-instance filtering, raw type, all requested states, and localization.</summary>
    [Fact]
    public async Task RunAsync_AssemblesAllTrackedQuestsAndFiltersCurrentInstances()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([20, 10]);
        fixture.AddMetadata(10, "Localized ten", 0xFE, currentInstanceId: 5);
        fixture.AddObjectivePage(10, 0, false,
        [
            (30, 4u, (byte)0, (string?)null),
            (20, 5u, (byte)3, "Quest objective — displayed"),
            (10, 5u, (byte)5, (string?)null),
        ]);
        fixture.AddMetadata(10, "Localized ten", 0xFE, currentInstanceId: 5);
        fixture.AddMetadata(20, "Localized twenty", 8, currentInstanceId: 2);
        fixture.AddObjectivePage(20, 0, false, [(1, 2u, (byte)4, "Localized failure")]);
        fixture.AddMetadata(20, "Localized twenty", 8, currentInstanceId: 2);
        fixture.AddIdsPage([20, 10]);

        await fixture.RunToNextSnapshotAsync();

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(Assert.Single(fixture.Application.Calls).Value);
        Assert.Equal([10u, 20u], snapshot.Quests.Select(quest => quest.QuestId));
        Assert.Equal((byte)0xFE, snapshot.Quests[0].Type);
        Assert.Equal([10, 20], snapshot.Quests[0].Objectives.Select(objective => objective.Index));
        Assert.Equal(TrackedQuestObjectiveState.CompletedAndDisplayed, snapshot.Quests[0].Objectives[1].State);
        Assert.Equal(TrackedQuestObjectiveState.FailedAndDisplayed, snapshot.Quests[0].Objectives[0].State);
        Assert.Null(snapshot.Quests[0].Objectives[0].Text);
        Assert.Equal("Quest objective — displayed", snapshot.Quests[0].Objectives[1].Text);
        Assert.Single(snapshot.Quests[1].Objectives);
        Assert.Equal(TrackedQuestObjectiveState.Failed, snapshot.Quests[1].Objectives[0].State);
        Assert.All(fixture.Application.Calls, call => Assert.Equal(UpdateMode.Snapshot, call.Mode));
        Assert.All(fixture.Application.Calls, call => Assert.True(call.IsResynchronizationBaseline));
        var applied = Assert.Single(fixture.Application.Calls);
        Assert.Equal(new StateAreaId(Constants.TrackedQuestsStateArea), applied.AreaId);
        Assert.Equal(fixture.Source, applied.Source);
        Assert.Equal(fixture.InitialPlayContextId, applied.PlayContextId);
        Assert.Equal(fixture.PlayContextGeneration, applied.PlayContextGeneration);
        Assert.Equal(fixture.UtcNow, applied.OccurredAt);
    }

    /// <summary>Verifies that zero tracked quests is a synchronized empty value, not unavailable.</summary>
    [Fact]
    public async Task RunAsync_ZeroTrackedQuestsPublishesAvailableEmptyCollection()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([]);
        fixture.AddIdsPage([]);

        await fixture.RunToNextSnapshotAsync();

        TrackedQuests snapshot = Assert.IsType<TrackedQuests>(Assert.Single(fixture.Application.Calls).Value);
        Assert.Empty(snapshot.Quests);
    }

    /// <summary>Verifies that one failed objective page produces unavailable state instead of a partial quest.</summary>
    [Fact]
    public async Task RunAsync_FailedRequiredPagePublishesUnavailableInsteadOfPartialData()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        fixture.AddResponse(TrackedQuestPageKind.Objectives, 10, 0, CaptureAvailability.Unavailable, []);

        await fixture.RunToNextSnapshotAsync();

        var unavailable = Assert.Single(fixture.Application.Calls);
        Assert.Null(unavailable.Value);
        Assert.Equal(new StateAreaId(Constants.TrackedQuestsStateArea), unavailable.AreaId);
        Assert.Equal(fixture.Source, unavailable.Source);
        Assert.Equal(fixture.InitialPlayContextId, unavailable.PlayContextId);
        Assert.Equal(fixture.PlayContextGeneration, unavailable.PlayContextGeneration);
        Assert.Equal(fixture.UtcNow, unavailable.OccurredAt);
        Assert.True(unavailable.IsResynchronizationBaseline);
    }

    /// <summary>Verifies that changing the play context during paging discards the old collection.</summary>
    [Fact]
    public async Task RunAsync_PlayContextChangeDiscardsOldCollection()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.ChangeContextAfterNextPage = true;

        await fixture.RunWithoutSnapshotAsync();

        Assert.Empty(fixture.Application.Calls);
    }

    /// <summary>Verifies a play-context end during collection discards the captured value.</summary>
    [Fact]
    public async Task RunAsync_PlayContextEndDuringCollectionDiscardsOldValue()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.EndContextAfterNextPage = true;

        await fixture.RunWithoutSnapshotAsync();

        Assert.Empty(fixture.Application.Calls);
    }

    /// <summary>Verifies that changing tracked IDs before the final recheck never publishes the earlier list.</summary>
    [Fact]
    public async Task RunAsync_ChangedTrackedIdSetPublishesUnavailable()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        fixture.AddObjectivePage(10, 0, false, []);
        fixture.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        fixture.AddIdsPage([11]);

        await fixture.RunToNextSnapshotAsync();

        Assert.Null(Assert.Single(fixture.Application.Calls).Value);
    }

    /// <summary>Verifies that identical raw duplicate objective records collapse while conflicting duplicates fail the whole collection.</summary>
    [Fact]
    public async Task RunAsync_DuplicateObjectiveRecordsAreDeduplicatedOrRejectedByValue()
    {
        var identical = new CaptureFixture();
        identical.AddIdsPage([10]);
        identical.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        identical.AddObjectivePage(10, 0, false,
        [
            (1, 1u, (byte)2, "same"),
            (1, 1u, (byte)2, "same"),
        ]);
        identical.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        identical.AddIdsPage([10]);

        await identical.RunToNextSnapshotAsync();

        TrackedQuests identicalSnapshot = Assert.IsType<TrackedQuests>(Assert.Single(identical.Application.Calls).Value);
        Assert.Single(identicalSnapshot.Quests[0].Objectives);

        var conflicting = new CaptureFixture();
        conflicting.AddIdsPage([10]);
        conflicting.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
        conflicting.AddObjectivePage(10, 0, false,
        [
            (1, 1u, (byte)2, "same"),
            (1, 1u, (byte)4, "same"),
        ]);

        await conflicting.RunToNextSnapshotAsync();

        Assert.Null(Assert.Single(conflicting.Application.Calls).Value);
    }

    /// <summary>Verifies the complete Snapshot objective limit fails closed across multiple quests.</summary>
    [Fact]
    public async Task RunAsync_ObjectiveAggregateAboveLimitPublishesUnavailable()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([1, 2]);
        AddQuestWithManyObjectives(fixture, 1, 600);
        AddQuestWithManyObjectives(fixture, 2, 600);
        fixture.AddIdsPage([1, 2]);

        await fixture.RunToNextSnapshotAsync();

        Assert.Null(Assert.Single(fixture.Application.Calls).Value);
    }

    /// <summary>Verifies old-instance objective rows still consume the complete collection's raw-record bound.</summary>
    [Fact]
    public async Task RunAsync_OldInstanceObjectiveAggregateAboveLimitPublishesUnavailable()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([1, 2]);
        AddQuestWithManyObjectives(fixture, 1, 600, objectiveInstanceId: 2);
        AddQuestWithManyObjectives(fixture, 2, 600, objectiveInstanceId: 2);

        await fixture.RunToNextSnapshotAsync();

        Assert.Null(Assert.Single(fixture.Application.Calls).Value);
    }

    /// <summary>Verifies one objective response cannot be mistaken for a different Adapter generation.</summary>
    [Fact]
    public async Task RunAsync_AdapterGenerationChangeDiscardsOldCollection()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.ChangeConnectionAfterNextPage = true;

        await fixture.RunWithoutSnapshotAsync();

        Assert.Empty(fixture.Application.Calls);
    }

    /// <summary>Verifies adapter unavailability with the same connection generation discards the capture.</summary>
    [Fact]
    public async Task RunAsync_AdapterUnavailableDuringCollectionDiscardsOldValue()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.AdapterUnavailableAfterNextPage = true;

        await fixture.RunWithoutSnapshotAsync();

        Assert.Empty(fixture.Application.Calls);
    }

    /// <summary>Verifies a changed Adapter instance is rejected even when its connection generation is reused.</summary>
    [Fact]
    public async Task RunAsync_AdapterInstanceChangeWithoutGenerationChangeDiscardsOldValue()
    {
        var fixture = new CaptureFixture();
        fixture.AddIdsPage([10]);
        fixture.ChangeInstanceAfterNextPage = true;

        await fixture.RunWithoutSnapshotAsync();

        Assert.Empty(fixture.Application.Calls);
    }

    /// <summary>Verifies the next ordinary Snapshot uses the non-resynchronization handoff after a baseline.</summary>
    [Fact]
    public async Task RunAsync_AppliesOrdinarySnapshotAfterResynchronizationCompletes()
    {
        var fixture = new CaptureFixture();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            fixture.AddIdsPage([10]);
            fixture.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
            fixture.AddObjectivePage(10, 0, false, []);
            fixture.AddMetadata(10, "Localized ten", 8, currentInstanceId: 1);
            fixture.AddIdsPage([10]);
        }

        using var shutdown = new CancellationTokenSource();
        Task runTask = fixture.Coordinator.RunAsync(shutdown.Token);
        await fixture.Application.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.MarkResynchronized();
        await fixture.Application.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shutdown.Cancel();
        await runTask;

        Assert.Equal(2, fixture.Application.Calls.Count);
        Assert.True(fixture.Application.Calls[0].IsResynchronizationBaseline);
        Assert.False(fixture.Application.Calls[1].IsResynchronizationBaseline);
        Assert.All(fixture.Application.Calls, call =>
        {
            Assert.Equal(new StateAreaId(Constants.TrackedQuestsStateArea), call.AreaId);
            Assert.Equal(fixture.Source, call.Source);
            Assert.Equal(fixture.InitialPlayContextId, call.PlayContextId);
            Assert.Equal(fixture.PlayContextGeneration, call.PlayContextGeneration);
            Assert.Equal(fixture.UtcNow, call.OccurredAt);
        });
    }

    /// <summary>Adds one quest's complete metadata, objective pages, and final metadata confirmation.</summary>
    /// <param name="fixture">The scripted page source.</param>
    /// <param name="questId">The quest runtime FormID.</param>
    /// <param name="objectiveCount">The number of raw current-instance objective facts.</param>
    /// <param name="objectiveInstanceId">The engine instance ID attached to each objective.</param>
    private static void AddQuestWithManyObjectives(
        CaptureFixture fixture,
        uint questId,
        int objectiveCount,
        uint objectiveInstanceId = 1)
    {
        const uint instanceId = 1;
        fixture.AddMetadata(questId, $"Quest {questId}", 8, instanceId);
        ushort cursor = 0;
        while (cursor < objectiveCount)
        {
            int count = Math.Min(30, objectiveCount - cursor);
            var facts = Enumerable.Range(cursor, count)
                .Select(index => ((ushort)index, objectiveInstanceId, (byte)0, (string?)null))
                .ToArray();
            ushort next = (ushort)(cursor + count);
            fixture.AddObjectivePage(questId, cursor, next < objectiveCount, facts);
            cursor = next;
        }

        fixture.AddMetadata(questId, $"Quest {questId}", 8, instanceId);
    }

    /// <summary>A response script with a matching fake Host/Adapter connection and publication sink.</summary>
    private sealed class CaptureFixture
    {
        /// <summary>The Adapter identity used by every response in this scenario.</summary>
        private readonly AdapterInstanceId instanceId = AdapterInstanceId.NewId();

        /// <summary>The currently active play-context identity.</summary>
        private readonly PlayContextId initialPlayContextId = PlayContextId.NewId();

        /// <summary>The play-context tracker whose active generation gates each complete capture.</summary>
        private readonly FakePlayContextTracker playContextTracker = new();

        /// <summary>The Adapter authority tracker for this test connection.</summary>
        private readonly AdapterAvailabilityTracker adapterTracker = new();

        /// <summary>The Host's view of its currently active fake connection.</summary>
        private readonly FakeAdapterIpcListener listener = new();

        /// <summary>The fixed time stamped on publications from this fixture.</summary>
        private readonly FakeClock clock = new()
        {
            UtcNow = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        };

        /// <summary>The page reader exercised by the capture coordinator.</summary>
        private readonly TrackedQuestPageReader pageReader;

        /// <summary>The current Adapter connection and its tracked-quest page response callback.</summary>
        private readonly FakeAdapterIpcConnection connection;

        /// <summary>The current page response program consumed as requests are sent.</summary>
        private readonly Queue<PageResponse> responses = new();

        /// <summary>The next unique correlation assigned by the fake connection.</summary>
        private ulong nextCorrelationId = 100;

        /// <summary>Creates the service graph and binds it to one live play context and Adapter generation.</summary>
        public CaptureFixture()
        {
            playContextTracker.NotifyTransition(initialPlayContextId);
            adapterTracker.CommitConnected(instanceId, 1);
            Application = new RecordingLiveStateApplication();
            connection = new FakeAdapterIpcConnection(new MemoryStream())
            {
                ConnectionGeneration = 1,
                TrySendReadSampleResult = true,
                PrepareTrackedQuestPageOverride = (kind, questId, cursor) =>
                    new IpcReadTrackedQuestPageMessage(nextCorrelationId++, kind, questId, cursor),
            };
            listener.CurrentConnection = connection;
            pageReader = new TrackedQuestPageReader(() => listener, adapterTracker, playContextTracker);
            connection.OnTrySendTrackedQuestPage = DeliverNextResponse;
            var snapshotCollector = new TrackedQuestSnapshotCollector(pageReader);
            Coordinator = new TrackedQuestCaptureCoordinator(
                () => listener,
                snapshotCollector,
                adapterTracker,
                playContextTracker,
                Application,
                clock);
        }

        /// <summary>The complete-quest coordinator exercised by this test.</summary>
        public ITrackedQuestCaptureCoordinator Coordinator { get; }

        /// <summary>The Adapter source stamped on each accepted response.</summary>
        public AdapterCaptureSource Source => new(instanceId, 1);

        /// <summary>The Adapter identity used for this fixture's live connection.</summary>
        public AdapterInstanceId InstanceId => instanceId;

        /// <summary>The initial play-context identity stamped on each response.</summary>
        public PlayContextId InitialPlayContextId => initialPlayContextId;

        /// <summary>The transition generation established for the initial play context.</summary>
        public long PlayContextGeneration => playContextTracker.GetSnapshot().TransitionGeneration;

        /// <summary>The timestamp supplied to publication.</summary>
        public DateTimeOffset UtcNow => clock.UtcNow;

        /// <summary>Every typed Host application call observed by the fake.</summary>
        public RecordingLiveStateApplication Application { get; }

        /// <summary>Whether the next accepted request changes the current play context after its old-context response is delivered.</summary>
        public bool ChangeContextAfterNextPage { get; set; }

        /// <summary>Whether the next accepted response ends the active play context.</summary>
        public bool EndContextAfterNextPage { get; set; }

        /// <summary>Whether the next accepted request changes the Adapter connection generation after delivery.</summary>
        public bool ChangeConnectionAfterNextPage { get; set; }

        /// <summary>Whether the next accepted response makes the current Adapter unavailable.</summary>
        public bool AdapterUnavailableAfterNextPage { get; set; }

        /// <summary>Whether the next accepted response binds a different Adapter instance at the same generation.</summary>
        public bool ChangeInstanceAfterNextPage { get; set; }

        /// <summary>Whether the response callback has changed the play context.</summary>
        public bool ContextChangedAfterPage { get; private set; }

        /// <summary>Whether the response callback ended the play context.</summary>
        public bool ContextEndedAfterPage { get; private set; }

        /// <summary>Whether the response callback has changed the Adapter generation.</summary>
        public bool ConnectionChangedAfterPage { get; private set; }

        /// <summary>Whether the response callback made the Adapter unavailable.</summary>
        public bool AdapterUnavailableAfterPage { get; private set; }

        /// <summary>Whether the response callback changed Adapter instance identity.</summary>
        public bool AdapterInstanceChangedAfterPage { get; private set; }

        /// <summary>Adds a tracked-ID page response.</summary>
        /// <param name="ids">The engine-order runtime IDs to return.</param>
        /// <param name="hasMore">Whether another page exists.</param>
        public void AddIdsPage(uint[] ids, bool hasMore = false) =>
            AddResponse(TrackedQuestPageKind.TrackedQuestIds, 0, 0, CaptureAvailability.Available, BuildIdsPage(ids, hasMore));

        /// <summary>Completes the fake adapter's current resynchronization transaction.</summary>
        public void MarkResynchronized()
        {
            IAdapterResynchronizationToken token = Assert.IsAssignableFrom<IAdapterResynchronizationToken>(
                adapterTracker.TryClaimResynchronizationToken());
            adapterTracker.NotifyResynchronized(instanceId, 1, token);
        }

        /// <summary>Adds one quest metadata response.</summary>
        /// <param name="questId">The requested runtime FormID.</param>
        /// <param name="title">The localized title.</param>
        /// <param name="type">The raw Skyrim type.</param>
        /// <param name="currentInstanceId">The current engine quest instance.</param>
        public void AddMetadata(uint questId, string title, byte type, uint currentInstanceId) =>
            AddResponse(TrackedQuestPageKind.QuestMetadata, questId, 0, CaptureAvailability.Available,
                BuildMetadataPage(questId, title, type, currentInstanceId));

        /// <summary>Adds one objective page response.</summary>
        /// <param name="questId">The requested runtime FormID.</param>
        /// <param name="cursor">The requested objective cursor.</param>
        /// <param name="hasMore">Whether another page exists.</param>
        /// <param name="facts">Index, instance, state, and nullable text facts.</param>
        public void AddObjectivePage(uint questId, ushort cursor, bool hasMore,
            (ushort Index, uint InstanceId, byte State, string? Text)[] facts)
        {
            ushort nextCursor = (ushort)(cursor + facts.Length);
            AddResponse(TrackedQuestPageKind.Objectives, questId, cursor, CaptureAvailability.Available,
                BuildObjectivePage(questId, nextCursor, hasMore, facts));
        }

        /// <summary>Adds one available or unavailable raw page response.</summary>
        /// <param name="kind">The expected page kind.</param>
        /// <param name="questId">The expected runtime FormID.</param>
        /// <param name="cursor">The expected page cursor.</param>
        /// <param name="availability">The Adapter capture availability.</param>
        /// <param name="payload">The private page payload.</param>
        public void AddResponse(TrackedQuestPageKind kind, uint questId, ushort cursor,
            CaptureAvailability availability, byte[] payload) =>
            responses.Enqueue(new PageResponse(kind, questId, cursor, availability, payload));

        /// <summary>Runs until the next complete value, including unavailable, is applied.</summary>
        public async Task RunToNextSnapshotAsync()
        {
            using var shutdown = new CancellationTokenSource();
            Task runTask = Coordinator.RunAsync(shutdown.Token);
            await Application.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
            shutdown.Cancel();
            await runTask;
        }

        /// <summary>Runs until a changed-authority capture is discarded and confirms no Snapshot is applied.</summary>
        public async Task RunWithoutSnapshotAsync()
        {
            using var shutdown = new CancellationTokenSource();
            Task runTask = Coordinator.RunAsync(shutdown.Token);
            await WaitUntilAsync(
                () => ContextChangedAfterPage || ContextEndedAfterPage || ConnectionChangedAfterPage
                    || AdapterUnavailableAfterPage || AdapterInstanceChangedAfterPage,
                runTask);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            shutdown.Cancel();
            await runTask;
        }

        /// <summary>Delivers one scripted response through the Coordinator's pre-registered correlation.</summary>
        /// <param name="request">The request accepted into the fake outbound queue.</param>
        private void DeliverNextResponse(IpcReadTrackedQuestPageMessage request)
        {
            Assert.NotEmpty(responses);
            PageResponse response = responses.Dequeue();
            Assert.Equal(response.Kind, request.PageKind);
            Assert.Equal(response.QuestId, request.QuestId);
            Assert.Equal(response.Cursor, request.Cursor);

            PlayContextSnapshot playContext = playContextTracker.GetSnapshot();
            AdapterAvailabilitySnapshot adapterSnapshot = new(
                AdapterAvailability.Available, instanceId, true, 1);
            CaptureUnitDefinition unit = LiveStateCatalog.Default.CaptureUnits.Single(
                candidate => candidate.Source == CaptureSourceKind.Sample
                    && candidate.CaptureKey == (uint)TrackedQuestCaptureKey.Page);
            var captureResult = new IpcCaptureResultMessage(
                request.CorrelationId, CaptureSourceKind.Sample, (uint)TrackedQuestCaptureKey.Page,
                response.Availability, playContext.Current!.Value, response.Payload);
            var context = new LiveCaptureContext(
                captureResult, new AdapterCaptureSource(instanceId, 1), unit, adapterSnapshot,
                playContext.Current.Value, playContext.TransitionGeneration, DateTimeOffset.UtcNow);
            pageReader.AcceptPageCapture(context);
            if (ChangeContextAfterNextPage)
            {
                ChangeContextAfterNextPage = false;
                ContextChangedAfterPage = true;
                playContextTracker.NotifyTransition(PlayContextId.NewId());
            }

            if (EndContextAfterNextPage)
            {
                EndContextAfterNextPage = false;
                ContextEndedAfterPage = true;
                playContextTracker.ClearCurrent();
            }

            if (ChangeConnectionAfterNextPage)
            {
                ChangeConnectionAfterNextPage = false;
                ConnectionChangedAfterPage = true;
                // A new generation also re-arms availability, matching the real lifecycle transition.
                adapterTracker.CommitConnected(AdapterInstanceId.NewId(), 2);
                listener.CurrentConnection = new FakeAdapterIpcConnection(new MemoryStream())
                {
                    ConnectionGeneration = 2,
                    TrySendReadSampleResult = true,
                };
            }

            if (AdapterUnavailableAfterNextPage)
            {
                AdapterUnavailableAfterNextPage = false;
                AdapterUnavailableAfterPage = true;
                adapterTracker.CommitDisconnected(instanceId, 1);
            }

            if (ChangeInstanceAfterNextPage)
            {
                ChangeInstanceAfterNextPage = false;
                AdapterInstanceChangedAfterPage = true;
                adapterTracker.CommitConnected(AdapterInstanceId.NewId(), 1);
            }
        }

        /// <summary>Waits for a bounded condition or an unexpected service exit.</summary>
        /// <param name="condition">The condition that ends the wait.</param>
        /// <param name="runTask">The collector task that must remain alive until cancellation.</param>
        private static async Task WaitUntilAsync(Func<bool> condition, Task runTask)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition())
            {
                if (runTask.IsCompleted)
                {
                    await runTask;
                    Assert.Fail("The collector stopped before the expected page response was delivered.");
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("The page response was not delivered within the test bound.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5));
            }
        }

        /// <summary>Builds the tracked-ID private page layout used by the Adapter contract.</summary>
        /// <param name="ids">The raw runtime FormIDs.</param>
        /// <param name="hasMore">Whether the next 32-ID page exists.</param>
        /// <returns>The count, continuation flag, and little-endian FormIDs.</returns>
        private static byte[] BuildIdsPage(uint[] ids, bool hasMore)
        {
            byte[] payload = new byte[2 + (ids.Length * sizeof(uint))];
            payload[0] = checked((byte)ids.Length);
            payload[1] = hasMore ? (byte)1 : (byte)0;
            for (int index = 0; index < ids.Length; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2 + (index * sizeof(uint))), ids[index]);
            }

            return payload;
        }

        /// <summary>Builds the metadata page layout including the Host-side current-instance filter fact.</summary>
        /// <param name="questId">The runtime quest FormID.</param>
        /// <param name="title">The localized title.</param>
        /// <param name="type">The raw quest-type byte.</param>
        /// <param name="currentInstanceId">The current runtime instance ID.</param>
        /// <returns>The exact private metadata page bytes.</returns>
        private static byte[] BuildMetadataPage(uint questId, string title, byte type, uint currentInstanceId)
        {
            byte[] titleBytes = Encoding.UTF8.GetBytes(title);
            byte[] payload = new byte[10 + titleBytes.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, questId);
            payload[4] = type;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), currentInstanceId);
            payload[9] = checked((byte)titleBytes.Length);
            titleBytes.CopyTo(payload, 10);
            return payload;
        }

        /// <summary>Builds the count-prefixed objective-instance page layout.</summary>
        /// <param name="questId">The runtime quest FormID.</param>
        /// <param name="nextCursor">The first raw objective offset after this page.</param>
        /// <param name="hasMore">Whether another page exists.</param>
        /// <param name="facts">The index, instance ID, raw state, and nullable text values.</param>
        /// <returns>The exact private objective page bytes.</returns>
        private static byte[] BuildObjectivePage(uint questId, ushort nextCursor, bool hasMore,
            (ushort Index, uint InstanceId, byte State, string? Text)[] facts)
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, questId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), nextCursor);
            payload[6] = hasMore ? (byte)1 : (byte)0;
            payload[7] = checked((byte)facts.Length);
            using var stream = new MemoryStream();
            stream.Write(payload);
            foreach ((ushort index, uint instanceId, byte state, string? text) in facts)
            {
                byte[] entry = new byte[8];
                BinaryPrimitives.WriteUInt16LittleEndian(entry, index);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(2), instanceId);
                entry[6] = state;
                byte[] textBytes = text is null ? [] : Encoding.UTF8.GetBytes(text);
                entry[7] = text is null ? byte.MaxValue : checked((byte)textBytes.Length);
                stream.Write(entry);
                stream.Write(textBytes);
            }

            return stream.ToArray();
        }
    }

    /// <summary>One scripted Adapter page reply consumed by the fake connection.</summary>
    /// <param name="Kind">The requested page kind.</param>
    /// <param name="QuestId">The requested runtime FormID.</param>
    /// <param name="Cursor">The requested offset.</param>
    /// <param name="Availability">Whether the Adapter could capture the page.</param>
    /// <param name="Payload">The encoded private page data.</param>
    private sealed record PageResponse(
        TrackedQuestPageKind Kind,
        uint QuestId,
        ushort Cursor,
        CaptureAvailability Availability,
        byte[] Payload);

    /// <summary>Records the typed value and authority mode applied by the collector.</summary>
    private sealed class RecordingLiveStateApplication : ILiveStateApplication
    {
        /// <summary>Completes when the first value is applied.</summary>
        public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when a second value is applied.</summary>
        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Every application call, in call order.</summary>
        public List<AppliedValue> Calls { get; } = [];

        /// <inheritdoc/>
        public void Apply<TState>(UpdateMode mode, StateAreaId areaId,
            TState value, bool isResynchronizationBaseline, AdapterCaptureSource source,
            AdapterAvailabilitySnapshot adapterSnapshot, PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration, DateTimeOffset occurredAt)
        {
            Calls.Add(new AppliedValue(value, mode, areaId, isResynchronizationBaseline, source,
                capturedPlayContextId, capturedPlayContextGeneration, occurredAt));
            FirstCall.TrySetResult();
            if (Calls.Count == 2)
            {
                SecondCallStarted.TrySetResult();
            }
        }
    }

    /// <summary>Builds an active or inactive coordinator authority for cadence tests.</summary>
    private sealed class CoordinatorCadenceFixture
    {
        /// <summary>The Host's adapter authority tracker.</summary>
        private readonly AdapterAvailabilityTracker adapterTracker = new();

        /// <summary>The Host's play-context authority tracker.</summary>
        private readonly FakePlayContextTracker playContextTracker = new();

        /// <summary>The Host's current Adapter connection.</summary>
        private readonly FakeAdapterIpcListener listener = new();

        /// <summary>The collector that records capture call timing and overlap.</summary>
        public ControlledSnapshotCollector Collector { get; } = new();

        /// <summary>The application recorder used by the coordinator.</summary>
        public RecordingLiveStateApplication Application { get; } = new();

        /// <summary>The capture coordinator under test.</summary>
        public TrackedQuestCaptureCoordinator Coordinator { get; }

        /// <summary>Creates one active Adapter connection and optional active play context.</summary>
        /// <param name="hasActivePlayContext">Whether the coordinator can start game-state capture.</param>
        public CoordinatorCadenceFixture(bool hasActivePlayContext)
        {
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            adapterTracker.CommitConnected(instanceId, 1);
            if (hasActivePlayContext)
            {
                playContextTracker.NotifyTransition(PlayContextId.NewId());
            }

            listener.CurrentConnection = new FakeAdapterIpcConnection(new MemoryStream())
            {
                ConnectionGeneration = 1,
                TrySendReadSampleResult = true,
            };
            Coordinator = new TrackedQuestCaptureCoordinator(
                () => listener,
                Collector,
                adapterTracker,
                playContextTracker,
                Application,
                new SystemClock());
        }
    }

    /// <summary>Records serialized collection starts for cadence assertions.</summary>
    private sealed class ControlledSnapshotCollector : ITrackedQuestSnapshotCollector
    {
        /// <summary>Protects the observed collection start timestamps.</summary>
        private readonly object gate = new();

        /// <summary>Monotonic timestamps for each complete collection attempt.</summary>
        private readonly List<long> callTimes = [];

        /// <summary>The number of collection calls started.</summary>
        private int calls;

        /// <summary>The number of active collection calls.</summary>
        private int activeCalls;

        /// <summary>The largest number of overlapping collection calls observed.</summary>
        private int maximumConcurrentCalls;

        /// <summary>The controlled duration of the first complete capture.</summary>
        public TimeSpan FirstCaptureDuration { get; set; }

        /// <summary>Completes when the second collection cycle starts.</summary>
        public TaskCompletionSource SecondCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The total number of collection attempts started.</summary>
        public int Calls => Volatile.Read(ref calls);

        /// <summary>The largest number of overlapping collection calls observed.</summary>
        public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);

        /// <summary>Returns a snapshot of collection start timestamps.</summary>
        public IReadOnlyList<long> CallTimes
        {
            get
            {
                lock (gate)
                {
                    return callTimes.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public async Task<TrackedQuests?> CollectAsync(
            AdapterCaptureSource source,
            PlayContextSnapshot playContext,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref activeCalls);
            UpdateMaximumConcurrentCalls(active);
            int call = Interlocked.Increment(ref calls);
            lock (gate)
            {
                callTimes.Add(Stopwatch.GetTimestamp());
            }

            if (call == 2)
            {
                SecondCallStarted.TrySetResult();
            }

            try
            {
                if (call == 1 && FirstCaptureDuration > TimeSpan.Zero)
                {
                    await Task.Delay(FirstCaptureDuration, cancellationToken);
                }

                await Task.Yield();
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }

        /// <summary>Records a newly observed maximum collection concurrency.</summary>
        /// <param name="activeCalls">The number of calls currently active.</param>
        private void UpdateMaximumConcurrentCalls(int activeCalls)
        {
            int observed;
            while (activeCalls > (observed = Volatile.Read(ref maximumConcurrentCalls)))
            {
                if (Interlocked.CompareExchange(ref maximumConcurrentCalls, activeCalls, observed) == observed)
                {
                    break;
                }
            }
        }
    }

    /// <summary>One typed state-application call observed by the fake application.</summary>
    /// <param name="Value">The complete collection or unavailable value.</param>
    /// <param name="Mode">The canonical update mode.</param>
    /// <param name="AreaId">The registered state area.</param>
    /// <param name="IsResynchronizationBaseline">Whether Host authority used the baseline path.</param>
    /// <param name="Source">The exact Adapter generation.</param>
    /// <param name="PlayContextId">The collection's play context.</param>
    /// <param name="PlayContextGeneration">The collection's play-context generation.</param>
    /// <param name="OccurredAt">The timestamp supplied for publication.</param>
    private sealed record AppliedValue(
        object? Value,
        UpdateMode Mode,
        StateAreaId AreaId,
        bool IsResynchronizationBaseline,
        AdapterCaptureSource Source,
        PlayContextId PlayContextId,
        long PlayContextGeneration,
        DateTimeOffset OccurredAt);
}
