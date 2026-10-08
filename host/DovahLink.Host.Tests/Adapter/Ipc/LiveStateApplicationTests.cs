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

        /// <summary>The real store and feed view composed around the application under test.</summary>
        /// <param name="Application">The application under test.</param>
        /// <param name="Feed">The publication feed that exposes emitted state.</param>
        /// <param name="Store">The authoritative store the application writes through.</param>
        /// <param name="AdapterTracker">The controllable adapter authority source.</param>
        /// <param name="PlayContextTracker">The active play-context source.</param>
        /// <param name="Context">The play context stamped on test captures.</param>
        /// <param name="Coordinator">The controllable resynchronization coordinator.</param>
        /// <param name="Recovery">The recorder for controlled-recovery requests.</param>
        /// <param name="Source">The exact adapter connection stamped on test captures.</param>
        /// <param name="Clock">The clock used for publication timestamps.</param>
        private sealed record Fixture(
            LiveStateApplication Application,
            StatePublicationFeed Feed,
            IAuthoritativeStateStore Store,
            FakeAdapterAvailabilityTracker AdapterTracker,
            FakePlayContextTracker PlayContextTracker,
            PlayContextId Context,
            FakeResynchronizationTransactionCoordinator Coordinator,
            FakeAdapterContinuityRecovery Recovery,
            AdapterCaptureSource Source,
            FakeClock Clock);

        /// <summary>Builds a live application with a real store and a controllable resynchronization coordinator.</summary>
        private static Fixture CreateReady()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var registeredAreas = new RegisteredStateAreaPolicy();
            registeredAreas.TryRegister(XpArea);
            registeredAreas.TryRegister(LevelArea);
            var store = new AuthoritativeStateStore(adapterTracker, playContextTracker, registeredAreas, Fixtures.BuildStateAuthorityLifecycle());
            var feed = new StatePublicationFeed(store);
            var coordinator = new FakeResynchronizationTransactionCoordinator();
            var continuityRecovery = new FakeAdapterContinuityRecovery();
            var application = new LiveStateApplication(coordinator, continuityRecovery, store);
            var source = new AdapterCaptureSource(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration);
            return new Fixture(application, feed, store, adapterTracker, playContextTracker, context, coordinator, continuityRecovery, source, new FakeClock());
        }

        /// <summary>Verifies that an ordinary Snapshot uses ordinary source authority and publishes its changed value.</summary>
        [Fact]
        public void Apply_OrdinarySnapshot_PublishesWithoutClaimingBaseline()
        {
            Fixture fixture = CreateReady();

            fixture.Application.Apply(
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
            Assert.Equal(RevisionNumber.Initial.Next(), fixture.Store.CurrentRevision(XpArea));
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
                UpdateMode.Snapshot,
                XpArea,
                value: (float?)null,
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

        /// <summary>Verifies that a baseline for which the coordinator issues no token is dropped without writing state or recording an area.</summary>
        [Fact]
        public void Apply_BaselineWithoutToken_WritesNothingAndRecordsNothing()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = null;

            fixture.Application.Apply(
                UpdateMode.Snapshot, XpArea, 42.5f, isResynchronizationBaseline: true, fixture.Source,
                fixture.AdapterTracker.GetSnapshot(), fixture.Context, fixture.PlayContextTracker.TransitionGeneration, fixture.Clock.UtcNow);

            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
            Assert.Equal(RevisionNumber.Initial, fixture.Store.CurrentRevision(XpArea));
        }

        /// <summary>Verifies that a recorded baseline carries the exact area and capture identity it was committed under.</summary>
        [Fact]
        public void Apply_CommittedBaseline_RecordsExactAreaAndIdentity()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

            fixture.Application.Apply(
                UpdateMode.Snapshot, XpArea, 42.5f, isResynchronizationBaseline: true, fixture.Source,
                fixture.AdapterTracker.GetSnapshot(), fixture.Context, fixture.PlayContextTracker.TransitionGeneration, fixture.Clock.UtcNow);

            Assert.Equal(
                (XpArea, fixture.Source.InstanceId, fixture.Source.ConnectionGeneration, fixture.Context, fixture.PlayContextTracker.TransitionGeneration),
                Assert.Single(fixture.Coordinator.RecordAreaAcceptedCalls));
            Assert.Equal(
                (fixture.Source.InstanceId, fixture.Source.ConnectionGeneration, fixture.Context, fixture.PlayContextTracker.TransitionGeneration),
                Assert.Single(fixture.Coordinator.AcquireTokenCalls));
        }

        /// <summary>Verifies that an Event outside resynchronization uses ordinary authority: no token is claimed and no recovery is requested.</summary>
        [Fact]
        public void Apply_EventOutsideResynchronization_PublishesWithoutClaimingTokenOrRecovery()
        {
            Fixture fixture = CreateReady();
            StateEventPublication? publishedEvent = null;
            fixture.Feed.EventOccurred += publication => publishedEvent = publication;

            fixture.Application.Apply(
                UpdateMode.Event, LevelArea, (ushort)12, isResynchronizationBaseline: false, fixture.Source,
                fixture.AdapterTracker.GetSnapshot(), fixture.Context, fixture.PlayContextTracker.TransitionGeneration, fixture.Clock.UtcNow);

            Assert.NotNull(publishedEvent);
            Assert.Empty(fixture.Coordinator.AcquireTokenCalls);
            Assert.Empty(fixture.Recovery.RecoveryRequests);
        }

        /// <summary>Verifies that an Event the store cannot apply, with no token available to retry under, requests controlled recovery for its connection.</summary>
        [Fact]
        public void Apply_EventRejectedWithNoTokenToRetry_RequestsRecoveryForItsConnection()
        {
            Fixture fixture = CreateReady();
            var staleSource = new AdapterCaptureSource(fixture.Source.InstanceId, fixture.Source.ConnectionGeneration + 1);
            fixture.Coordinator.AcquireTokenResult = null;

            fixture.Application.Apply(
                UpdateMode.Event, LevelArea, (ushort)12, isResynchronizationBaseline: false, staleSource,
                fixture.AdapterTracker.GetSnapshot(), fixture.Context, fixture.PlayContextTracker.TransitionGeneration, fixture.Clock.UtcNow);

            Assert.Equal([staleSource.ConnectionGeneration], fixture.Recovery.RecoveryRequests);
            Assert.Equal(RevisionNumber.Initial, fixture.Store.CurrentRevision(LevelArea));
        }

        /// <summary>Verifies that an Event first tried under ordinary authority just as resynchronization began is retried with the claimed token and then applies without recovery.</summary>
        [Fact]
        public void Apply_EventRejectedForStaleGateView_RetriesWithTokenAndApplies()
        {
            Fixture fixture = CreateReady();
            AdapterAvailabilitySnapshot ungatedView = fixture.AdapterTracker.GetSnapshot();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            StateEventPublication? publishedEvent = null;
            fixture.Feed.EventOccurred += publication => publishedEvent = publication;

            fixture.Application.Apply(
                UpdateMode.Event, LevelArea, (ushort)12, isResynchronizationBaseline: false, fixture.Source,
                ungatedView, fixture.Context, fixture.PlayContextTracker.TransitionGeneration, fixture.Clock.UtcNow);

            Assert.NotNull(publishedEvent);
            Assert.Single(fixture.Coordinator.AcquireTokenCalls);
            Assert.Empty(fixture.Recovery.RecoveryRequests);
        }

        /// <summary>Verifies that a changed baseline captured under a play context that has since moved on is neither replayable nor counted toward the transaction.</summary>
        [Fact]
        public void Apply_ChangedBaselineFromStalePlayContext_IsRejectedAndNotRecorded()
        {
            Fixture fixture = CreateReady();
            fixture.AdapterTracker.NeedsResynchronization = true;
            fixture.Coordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            long capturedGeneration = fixture.PlayContextTracker.TransitionGeneration;
            fixture.PlayContextTracker.NotifyTransition(PlayContextId.NewId());
            bool snapshotChanged = false;
            fixture.Feed.SnapshotChanged += _ => snapshotChanged = true;

            fixture.Application.Apply(
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                fixture.AdapterTracker.GetSnapshot(),
                fixture.Context,
                capturedGeneration,
                fixture.Clock.UtcNow);

            Assert.Empty(fixture.Coordinator.RecordAreaAcceptedCalls);
            Assert.False(snapshotChanged);
            fixture.AdapterTracker.NeedsResynchronization = false;
            Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        }

        /// <summary>Verifies that an unchanged baseline arriving after the adapter dropped is rejected and not counted toward the transaction.</summary>
        [Fact]
        public void Apply_UnchangedBaselineAfterAdapterLoss_IsRejectedAndNotRecorded()
        {
            Fixture fixture = CreateReady();
            fixture.Application.Apply(
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
            AdapterAvailabilitySnapshot gatedSnapshot = fixture.AdapterTracker.GetSnapshot();
            fixture.AdapterTracker.Current = AdapterAvailability.Unavailable;

            fixture.Application.Apply(
                UpdateMode.Snapshot,
                XpArea,
                42.5f,
                isResynchronizationBaseline: true,
                fixture.Source,
                gatedSnapshot,
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
    }
}
