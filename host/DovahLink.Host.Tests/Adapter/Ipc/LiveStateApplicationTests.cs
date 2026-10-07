namespace DovahLink.Host.Tests.Adapter.Ipc
{
    using DovahLink.Host.Adapter;
    using DovahLink.Host.Adapter.Ipc;
    using DovahLink.Host.Identity;
    using DovahLink.Host.PlayContext;
    using DovahLink.Host.State;
    using DovahLink.Host.Tests.TestDoubles;

    /// <summary>Tests the shared authority and publication path for validated live capture values.</summary>
    public class LiveStateApplicationTests
    {
        private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
        private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);

        /// <summary>The real publishers and feed composed around the application under test.</summary>
        /// <param name="Application">The application under test.</param>
        /// <param name="Feed">The publication feed that exposes emitted state.</param>
        /// <param name="FloatPublisher">The publisher for float-valued areas.</param>
        /// <param name="LevelPublisher">The publisher for the level area.</param>
        /// <param name="AdapterTracker">The controllable adapter authority source.</param>
        /// <param name="PlayContextTracker">The active play-context source.</param>
        /// <param name="Context">The play context stamped on test captures.</param>
        /// <param name="Coordinator">The controllable resynchronization coordinator.</param>
        /// <param name="Source">The exact adapter connection stamped on test captures.</param>
        /// <param name="Clock">The clock used for publication timestamps.</param>
        private sealed record Fixture(
            LiveStateApplication Application,
            StatePublicationFeed Feed,
            IStatePublisher<float?> FloatPublisher,
            IStatePublisher<ushort?> LevelPublisher,
            FakeAdapterAvailabilityTracker AdapterTracker,
            FakePlayContextTracker PlayContextTracker,
            PlayContextId Context,
            FakeResynchronizationTransactionCoordinator Coordinator,
            AdapterCaptureSource Source,
            FakeClock Clock);

        /// <summary>Builds a live application with real publishers and a controllable resynchronization coordinator.</summary>
        private static Fixture CreateReady()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var registeredAreas = new RegisteredStateAreaPolicy();
            registeredAreas.TryRegister(XpArea);
            registeredAreas.TryRegister(LevelArea);
            var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas, Fixtures.BuildStateAuthorityLifecycle());
            var revisionTracker = new RevisionTracker();
            var floatPublisher = new StatePublisher<float?>(revisionTracker, playContextTracker, adapterTracker);
            var levelPublisher = new StatePublisher<ushort?>(revisionTracker, playContextTracker, adapterTracker);
            var coordinator = new FakeResynchronizationTransactionCoordinator();
            var continuityRecovery = new FakeAdapterContinuityRecovery();
            var application = new LiveStateApplication(coordinator, continuityRecovery, feed);
            var source = new AdapterCaptureSource(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration);
            return new Fixture(application, feed, floatPublisher, levelPublisher, adapterTracker, playContextTracker, context, coordinator, source, new FakeClock());
        }

        /// <summary>Verifies that an ordinary Snapshot uses ordinary publisher authority and publishes its changed value.</summary>
        [Fact]
        public void Apply_OrdinarySnapshot_PublishesWithoutClaimingBaseline()
        {
            Fixture fixture = CreateReady();

            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: false,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? snapshot));
            Assert.Equal(42.5f, snapshot!.Data.GetProperty("value").GetSingle());
            Assert.Empty(fixture.Coordinator.AcquireTokenCalls);
            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
        }

        /// <summary>Verifies that an unchanged ordinary Snapshot does not publish a duplicate.</summary>
        [Fact]
        public void Apply_UnchangedOrdinarySnapshot_DoesNotPublishAgain()
        {
            Fixture fixture = CreateReady();
            int snapshotCount = 0;
            fixture.Feed.SnapshotChanged += _ => snapshotCount++;

            for (int i = 0; i < 2; i++)
            {
                fixture.Application.Apply(
                    fixture.FloatPublisher,
                    UpdateMode.Snapshot,
                    XpArea,
                    42.5f,
                    isResynchronizationBaseline: false,
                    fixture.Source,
                    fixture.AdapterTracker.GetSnapshot(),
                    fixture.Context,
                    fixture.PlayContextTracker.TransitionGeneration,
                    fixture.Clock.UtcNow);
            }

            Assert.Equal(1, snapshotCount);
            Assert.Equal(RevisionNumber.Initial.Next(), fixture.FloatPublisher.CurrentRevision(XpArea));
        }

        /// <summary>Verifies that an accepted resynchronization baseline records its area and publishes its Snapshot value.</summary>
        [Fact]
        public void Apply_ResynchronizationBaseline_RecordsAreaAndPublishesSnapshot()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            bool eventRaised = false;
            fixture.Feed.EventOccurred += _ => eventRaised = true;

            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.Single(fixture.Coordinator.RecordAreaAcceptedCalls);
            Assert.Equal(XpArea, fixture.Coordinator.RecordAreaAcceptedCalls[0].AreaId);
            fixture.AdapterTracker.NeedsResynchronization = false;
            Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? snapshot));
            Assert.Equal(42.5f, snapshot!.Data.GetProperty("value").GetSingle());
            Assert.False(eventRaised);
        }

        /// <summary>Verifies an unavailable baseline still records its area so resynchronization can finish.</summary>
        [Fact]
        public void Apply_UnavailableResynchronizationBaseline_RecordsAreaAcceptance()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                value: null,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.Single(fixture.Coordinator.RecordAreaAcceptedCalls);
            Assert.Equal(XpArea, fixture.Coordinator.RecordAreaAcceptedCalls[0].AreaId);
        }

        /// <summary>Verifies that an unchanged baseline restores the feed cache without publishing a duplicate change.</summary>
        [Fact]
        public void Apply_UnchangedResynchronizationBaseline_RestoresFeedSnapshot()
        {
            Fixture fixture = CreateReady();
            int snapshotCount = 0;
            fixture.Feed.SnapshotChanged += _ => snapshotCount++;
            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: false,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);
            Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out _));
            fixture.AdapterTracker.PublishTransition(new AdapterAvailabilityTransition(
                AdapterAvailability.Available,
                AdapterAvailability.Unavailable,
                fixture.Source.InstanceId,
                fixture.Source.ConnectionGeneration));
            Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            fixture.AdapterTracker.NeedsResynchronization = false;
            Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? snapshot));
            Assert.Equal(42.5f, snapshot!.Data.GetProperty("value").GetSingle());
            Assert.Equal(1, snapshotCount);
            Assert.Single(fixture.Coordinator.RecordAreaAcceptedCalls);
        }

        /// <summary>Verifies that an Event remains reliable during resynchronization without satisfying a baseline area.</summary>
        [Fact]
        public void Apply_EventDuringResynchronization_PublishesEventWithoutRecordingBaseline()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            StateEventPublication? publishedEvent = null;
            fixture.Feed.EventOccurred += publication => publishedEvent = publication;

            fixture.Application.Apply(
                fixture.LevelPublisher,
                UpdateMode.Event,
                LevelArea,
                (ushort)12,
                isResynchronizationBaseline: false,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.NotNull(publishedEvent);
            Assert.Equal(LevelArea, publishedEvent!.StateArea);
            Assert.Equal(RevisionNumber.Initial, publishedEvent.BaseRevision);
            Assert.Equal(RevisionNumber.Initial.Next(), publishedEvent.Revision);
            Assert.Equal((ushort)12, publishedEvent.Data.GetProperty("value").GetUInt16());
            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
        }

        /// <summary>Verifies that a changed baseline whose play context moves on between publisher acceptance and the feed commit is neither replayable nor counted toward the transaction.</summary>
        [Fact]
        public void Apply_ChangedBaselineRejectedByFeedAfterPlayContextTransition_IsNotRecorded()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            var publisher = new InterleavingPublisher(fixture.FloatPublisher, () => fixture.PlayContextTracker.NotifyTransition(PlayContextId.NewId()));
            bool snapshotChanged = false;
            fixture.Feed.SnapshotChanged += _ => snapshotChanged = true;

            fixture.Application.Apply(
                publisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
            Assert.False(snapshotChanged);
            fixture.AdapterTracker.NeedsResynchronization = false;
            Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        }

        /// <summary>Verifies that an unchanged baseline whose adapter drops between publisher acceptance and the feed commit is not counted toward the transaction.</summary>
        [Fact]
        public void Apply_UnchangedBaselineRejectedByFeedAfterAdapterLoss_IsNotRecorded()
        {
            Fixture fixture = CreateReady();
            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: false,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            var publisher = new InterleavingPublisher(fixture.FloatPublisher, () => fixture.AdapterTracker.Current = AdapterAvailability.Unavailable);

            fixture.Application.Apply(
                publisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
        }

        /// <summary>Verifies that a committed baseline is counted exactly once, for both the changed and unchanged paths.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Apply_CommittedBaseline_IsRecordedExactlyOnce(bool unchanged)
        {
            Fixture fixture = CreateReady();
            if (unchanged)
            {
                fixture.Application.Apply(
                    fixture.FloatPublisher,
                    UpdateMode.Snapshot,
                    XpArea,
                    42.5f,
                    isResynchronizationBaseline: false,
                    fixture.Source,
                    fixture.AdapterTracker.GetSnapshot(),
                    fixture.Context,
                    fixture.PlayContextTracker.TransitionGeneration,
                    fixture.Clock.UtcNow);
            }

            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

            fixture.Application.Apply(
                fixture.FloatPublisher,
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                fixture.PlayContextTracker.TransitionGeneration,
                fixture.Clock.UtcNow);

            Assert.Single(fixture.Coordinator.RecordAreaAcceptedCalls);
        }

        /// <summary>Wraps a real publisher and runs a lifecycle change right after a resynchronization baseline is accepted, before the feed commit.</summary>
        private sealed class InterleavingPublisher(IStatePublisher<float?> inner, Action afterBaselineAccepted) : IStatePublisher<float?>
        {
            /// <inheritdoc/>
            public bool TryGetCurrentValue(StateAreaId areaId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out float? value) => inner.TryGetCurrentValue(areaId, out value);

            /// <inheritdoc/>
            public RevisionNumber CurrentRevision(StateAreaId areaId) => inner.CurrentRevision(areaId);

            /// <inheritdoc/>
            public StateApplyResult Apply(AdapterInstanceId sourceInstanceId, long sourceConnectionGeneration, PlayContextId capturedPlayContextId, long capturedPlayContextGeneration, StateAreaId areaId, float? value) =>
                inner.Apply(sourceInstanceId, sourceConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);

            /// <inheritdoc/>
            public StateApplyResult ApplyResynchronizationBaseline(IAdapterResynchronizationToken resynchronizationToken, PlayContextId capturedPlayContextId, long capturedPlayContextGeneration, StateAreaId areaId, float? value)
            {
                StateApplyResult result = inner.ApplyResynchronizationBaseline(resynchronizationToken, capturedPlayContextId, capturedPlayContextGeneration, areaId, value);
                afterBaselineAccepted();
                return result;
            }

            /// <inheritdoc/>
            public StateApplyResult ApplyEvent(AdapterInstanceId sourceInstanceId, long sourceConnectionGeneration, PlayContextId capturedPlayContextId, long capturedPlayContextGeneration, IAdapterResynchronizationToken? resynchronizationToken, StateAreaId areaId, float? value) =>
                inner.ApplyEvent(sourceInstanceId, sourceConnectionGeneration, capturedPlayContextId, capturedPlayContextGeneration, resynchronizationToken, areaId, value);
        }
    }
}
