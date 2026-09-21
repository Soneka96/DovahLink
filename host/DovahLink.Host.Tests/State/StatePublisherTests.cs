namespace DovahLink.Host.Tests.State
{
    using System.Text.Json;
    using DovahLink.Host.Adapter;
    using DovahLink.Host.Identity;
    using DovahLink.Host.PlayContext;
    using DovahLink.Host.State;
    using DovahLink.Host.Tests.TestDoubles;

    /// <summary>Tests for <see cref="StatePublisher{TState}"/>, using <see langword="int"/> as a stand-in captured value type.</summary>
    public class StatePublisherTests
    {
        private static readonly StateAreaId AreaId = new("Character");

        /// <summary>Verifies that reading a value before any play context is established reports unavailable rather than a stale default.</summary>
        [Fact]
        public void TryGetCurrentValue_NoPlayContextYet_ReturnsUnavailable()
        {
            var playContextTracker = new FakePlayContextTracker();
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that applying a value before any play context is established fails loudly rather than silently accepting it.</summary>
        [Fact]
        public void Apply_NoPlayContextYet_Throws()
        {
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), new FakePlayContextTracker(), adapterTracker);

            Assert.Throws<InvalidOperationException>(() => publisher.Apply(
                adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, PlayContextId.NewId(), 0, AreaId, 1));
        }

        /// <summary>Verifies that the first applied value advances the revision past the initial revision and becomes the current value.</summary>
        [Fact]
        public void Apply_FirstValue_AdvancesRevisionAndBecomesCurrent()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(42, value);
            Assert.Equal(RevisionNumber.Initial.Next(), publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that the first applied value's returned result reports accepted, changed, and the exact before/after revisions.</summary>
        [Fact]
        public void Apply_FirstValue_ReturnsAcceptedChangedWithExactRevisions()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            StateApplyResult result = publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.True(result.Accepted);
            Assert.True(result.Changed);
            Assert.Equal(RevisionNumber.Initial, result.BaseRevision);
            Assert.Equal(RevisionNumber.Initial.Next(), result.Revision);
        }

        /// <summary>Verifies that applying the same value again reports accepted but unchanged, with the base and new revision equal.</summary>
        [Fact]
        public void Apply_SameValueAgain_ReturnsAcceptedUnchangedWithSameRevision()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);
            RevisionNumber revisionAfterFirstApply = publisher.CurrentRevision(AreaId);

            StateApplyResult result = publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.True(result.Accepted);
            Assert.False(result.Changed);
            Assert.Equal(revisionAfterFirstApply, result.BaseRevision);
            Assert.Equal(revisionAfterFirstApply, result.Revision);
        }

        /// <summary>Verifies that a rejected call's result reports the fixed <see cref="StateApplyResult.Rejected"/> shape rather than a stale or partial revision.</summary>
        [Fact]
        public void Apply_Rejected_ReturnsFixedRejectedResult()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Unavailable };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            StateApplyResult result = publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.Equal(StateApplyResult.Rejected, result);
        }

        /// <summary>
        /// Verifies that a first accepted resynchronization baseline's own result reports changed with the
        /// exact before/after revisions, symmetric with <see cref="Apply_FirstValue_ReturnsAcceptedChangedWithExactRevisions"/>
        /// -- <see cref="IStatePublisher{TState}.ApplyResynchronizationBaseline"/> shares the same ordering
        /// point as <see cref="IStatePublisher{TState}.Apply"/>, so its own result must behave identically.
        /// </summary>
        [Fact]
        public void ApplyResynchronizationBaseline_FirstValue_ReturnsAcceptedChangedWithExactRevisions()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            StateApplyResult result = publisher.ApplyResynchronizationBaseline(
                adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.True(result.Accepted);
            Assert.True(result.Changed);
            Assert.Equal(RevisionNumber.Initial, result.BaseRevision);
            Assert.Equal(RevisionNumber.Initial.Next(), result.Revision);
        }

        /// <summary>Verifies that a token-authorized Event is accepted during resynchronization with normal Event revisions.</summary>
        [Fact]
        public void ApplyEvent_CurrentResynchronization_ReturnsAcceptedChangedWithExactRevisions()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            StateApplyResult result = publisher.ApplyEvent(
                adapterTracker.CurrentInstanceId!.Value,
                adapterTracker.CurrentConnectionGeneration,
                context,
                playContextTracker.TransitionGeneration,
                adapterTracker.TryClaimResynchronizationToken(),
                AreaId,
                42);

            Assert.True(result.Accepted);
            Assert.True(result.Changed);
            Assert.Equal(RevisionNumber.Initial, result.BaseRevision);
            Assert.Equal(RevisionNumber.Initial.Next(), result.Revision);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that an Event rejects a token that is not current for the adapter during resynchronization.</summary>
        [Fact]
        public void ApplyEvent_StaleToken_IsRejected()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            Assert.False(publisher.ApplyEvent(
                adapterTracker.CurrentInstanceId!.Value,
                adapterTracker.CurrentConnectionGeneration,
                context,
                playContextTracker.TransitionGeneration,
                new ForeignResynchronizationToken(),
                AreaId,
                42).Accepted);
        }

        /// <summary>Verifies that an Event whose apply-time state is ordinary uses current adapter authority without a token.</summary>
        [Fact]
        public void ApplyEvent_ResynchronizationFinished_UsesOrdinaryAuthority()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = false };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            StateApplyResult result = publisher.ApplyEvent(
                adapterTracker.CurrentInstanceId!.Value,
                adapterTracker.CurrentConnectionGeneration,
                context,
                playContextTracker.TransitionGeneration,
                null,
                AreaId,
                42);

            Assert.True(result.Accepted);
            Assert.True(result.Changed);
            Assert.Equal(RevisionNumber.Initial.Next(), result.Revision);
        }

        /// <summary>Verifies that applying the same value again does not advance the revision a second time.</summary>
        [Fact]
        public void Apply_SameValueAgain_DoesNotAdvanceRevision()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);
            RevisionNumber revisionAfterFirstApply = publisher.CurrentRevision(AreaId);

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.Equal(revisionAfterFirstApply, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that applying a genuinely different value advances the revision again.</summary>
        [Fact]
        public void Apply_DifferentValue_AdvancesRevisionAgain()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 43);

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(43, value);
            Assert.Equal(RevisionNumber.Initial.Next().Next(), publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that a value is reported unavailable while the adapter is unavailable, even though it was already applied.</summary>
        [Fact]
        public void TryGetCurrentValue_AdapterUnavailable_ReturnsUnavailable()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            adapterTracker.Current = AdapterAvailability.Unavailable;

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a value is reported unavailable while the adapter still needs resynchronization, even if it reports available.</summary>
        [Fact]
        public void TryGetCurrentValue_AdapterNeedsResynchronization_ReturnsUnavailable()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            bool accepted = publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that an ordinary capture cannot be smuggled through the resynchronization gate before an explicit baseline.</summary>
        [Fact]
        public void Apply_WhileResynchronizationRequired_RejectsOrdinaryCaptureUntilBaselineIsAccepted()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            var adapterTracker = new FakeAdapterAvailabilityTracker
            {
                Current = AdapterAvailability.Available,
                CurrentInstanceId = instanceId,
                CurrentConnectionGeneration = 1,
                NeedsResynchronization = true,
            };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            Assert.False(publisher.Apply(instanceId, 1, context, playContextTracker.TransitionGeneration, AreaId, 41).Accepted);
            adapterTracker.NeedsResynchronization = false;
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));

            adapterTracker.NeedsResynchronization = true;
            Assert.True(publisher.ApplyResynchronizationBaseline(
                adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
            adapterTracker.NeedsResynchronization = false;

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(42, value);
        }

        /// <summary>Verifies that an old adapter value does not become visible after a new adapter resynchronizes.</summary>
        [Fact]
        public void TryGetCurrentValue_AfterResynchronization_ReturnsValueAgain()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterInstanceId = AdapterInstanceId.NewId();
            var adapterTracker = new FakeAdapterAvailabilityTracker
            {
                Current = AdapterAvailability.Available,
                CurrentInstanceId = adapterInstanceId,
            };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterInstanceId, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            adapterTracker.Current = AdapterAvailability.Available;
            adapterTracker.CurrentInstanceId = AdapterInstanceId.NewId();
            adapterTracker.NeedsResynchronization = false;

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a play-context transition clears the previous context's value and resets the revision for the new context.</summary>
        [Fact]
        public void PlayContextTransition_ClearsValueAndResetsRevision()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            playContextTracker.NotifyTransition(PlayContextId.NewId());

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that a value can be applied again under the new context after a transition, starting from the initial revision.</summary>
        [Fact]
        public void Apply_AfterPlayContextTransition_StartsFromInitialRevisionAgain()
        {
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(PlayContextId.NewId());
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, playContextTracker.Current!.Value, playContextTracker.TransitionGeneration, AreaId, 42);
            PlayContextId secondContext = PlayContextId.NewId();
            playContextTracker.NotifyTransition(secondContext);

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, secondContext, playContextTracker.TransitionGeneration, AreaId, 7);

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(7, value);
            Assert.Equal(RevisionNumber.Initial.Next(), publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that authoritative state does not survive a host restart: a freshly constructed publisher has no value for any area.</summary>
        [Fact]
        public void NewPublisher_HasNoValueForAnyArea()
        {
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(PlayContextId.NewId());
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that distinct state areas are tracked independently: applying one never affects another's value or revision.</summary>
        [Fact]
        public void Apply_DistinctAreas_TrackedIndependently()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            var otherAreaId = new StateAreaId("Inventory");

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42);

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int characterValue));
            Assert.Equal(42, characterValue);
            Assert.False(publisher.TryGetCurrentValue(otherAreaId, out _));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(otherAreaId));
        }

        /// <summary>
        /// Verifies that a real first transition -- fired after the publisher has already subscribed,
        /// so its handler actually observes a null previous play context -- does not throw.
        /// </summary>
        [Fact]
        public void PlayContextTransitioned_FirstRealTransitionAfterSubscribing_DoesNotThrow()
        {
            var playContextTracker = new FakePlayContextTracker();
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);

            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 1);
            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(1, value);
        }

        /// <summary>Verifies that a capture from an unavailable adapter is rejected and does not advance revision.</summary>
        [Fact]
        public void Apply_WhileAdapterUnavailable_RejectsCapture()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Unavailable };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a value from a stale adapter instance is rejected.</summary>
        [Fact]
        public void Apply_StaleAdapterInstance_IsRejected()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.Apply(AdapterInstanceId.NewId(), adapterTracker.CurrentConnectionGeneration, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that the same adapter instance's new connection cannot reveal its old value.</summary>
        [Fact]
        public void TryGetCurrentValue_SameInstanceNewConnectionGeneration_HidesOldValue()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            var adapterTracker = new FakeAdapterAvailabilityTracker
            {
                Current = AdapterAvailability.Available,
                CurrentInstanceId = instanceId,
                CurrentConnectionGeneration = 1,
                NeedsResynchronization = false,
            };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            Assert.True(publisher.Apply(instanceId, 1, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);

            adapterTracker.CurrentConnectionGeneration = 2;
            adapterTracker.NeedsResynchronization = false;

            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a matching fresh resynchronization becomes visible without advancing an unchanged revision.</summary>
        [Fact]
        public void Apply_MatchingResynchronization_SameValueKeepsRevision()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            var adapterTracker = new FakeAdapterAvailabilityTracker
            {
                Current = AdapterAvailability.Available,
                CurrentInstanceId = instanceId,
                CurrentConnectionGeneration = 1,
                NeedsResynchronization = false,
            };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            Assert.True(publisher.Apply(instanceId, 1, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
            RevisionNumber revision = publisher.CurrentRevision(AreaId);

            adapterTracker.CurrentConnectionGeneration = 2;
            adapterTracker.NeedsResynchronization = true;
            StateApplyResult resyncResult = publisher.ApplyResynchronizationBaseline(
                adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration, AreaId, 42);
            adapterTracker.NeedsResynchronization = false;

            Assert.True(resyncResult.Accepted);
            Assert.False(resyncResult.Changed);
            Assert.Equal(revision, resyncResult.BaseRevision);
            Assert.Equal(revision, resyncResult.Revision);
            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(42, value);
            Assert.Equal(revision, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that a baseline token from an older adapter connection cannot authorize a new connection.</summary>
        [Fact]
        public void ApplyResynchronizationBaseline_StaleToken_IsRejected()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            long generation = playContextTracker.TransitionGeneration;
            var adapterTracker = new AdapterAvailabilityTracker();
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            long firstGeneration = 1;
            PublishConnected(adapterTracker, instanceId, firstGeneration);
            IAdapterResynchronizationToken staleToken = adapterTracker.TryClaimResynchronizationToken()!;
            Assert.True(publisher.ApplyResynchronizationBaseline(staleToken, context, generation, AreaId, 1).Accepted);
            adapterTracker.NotifyResynchronized(instanceId, firstGeneration, staleToken);
            Assert.False(publisher.ApplyResynchronizationBaseline(staleToken, context, generation, AreaId, 2).Accepted);

            long secondGeneration = 2;
            PublishConnected(adapterTracker, instanceId, secondGeneration);

            Assert.False(publisher.ApplyResynchronizationBaseline(staleToken, context, generation, AreaId, 2).Accepted);
            Assert.False(publisher.ApplyResynchronizationBaseline(new ForeignResynchronizationToken(), context, generation, AreaId, 2).Accepted);
            IAdapterResynchronizationToken secondToken = adapterTracker.TryClaimResynchronizationToken()!;
            Assert.True(publisher.ApplyResynchronizationBaseline(secondToken, context, generation, AreaId, 2).Accepted);
            adapterTracker.NotifyResynchronized(instanceId, secondGeneration, secondToken);
            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(2, value);
        }

        /// <summary>Verifies that a new-context value accepted before transition notification cleanup is not erased.</summary>
        [Fact]
        public async Task Apply_DuringDelayedPlayContextTransition_PreservesNewContextValue()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId firstContext = PlayContextId.NewId();
            PlayContextId secondContext = PlayContextId.NewId();
            playContextTracker.NotifyTransition(firstContext);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            using var notificationEntered = new ManualResetEventSlim();
            using var releaseNotification = new ManualResetEventSlim();
            playContextTracker.Transitioned += transition =>
            {
                if (transition.NewPlayContextId == secondContext)
                {
                    notificationEntered.Set();
                    releaseNotification.Wait();
                }
            };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, firstContext, playContextTracker.TransitionGeneration, AreaId, 1);

            Task transitionTask = Task.Run(() => playContextTracker.NotifyTransition(secondContext));
            Assert.True(notificationEntered.Wait(TimeSpan.FromSeconds(5)));
            // The tracker's own current/generation already advanced to secondContext before invoking the
            // (still-blocked) transition handler, so a capture read fresh from the tracker at this exact
            // moment -- as production capture code would -- is already stamped with the new context.
            Assert.True(publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, secondContext, playContextTracker.TransitionGeneration, AreaId, 2).Accepted);
            releaseNotification.Set();
            await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(2, value);
        }

        /// <summary>Verifies that first-context cleanup cannot erase a value accepted after context publication.</summary>
        [Fact]
        public async Task Apply_DuringDelayedFirstPlayContextTransition_PreservesValue()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId firstContext = PlayContextId.NewId();
            using var transitionEntered = new ManualResetEventSlim();
            using var releaseTransition = new ManualResetEventSlim();
            playContextTracker.Transitioned += _ =>
            {
                transitionEntered.Set();
                releaseTransition.Wait();
            };
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            Task transitionTask = Task.Run(() => playContextTracker.NotifyTransition(firstContext));
            Assert.True(transitionEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(publisher.Apply(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration, firstContext, playContextTracker.TransitionGeneration, AreaId, 7).Accepted);
            releaseTransition.Set();
            await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(7, value);
        }

        /// <summary>Verifies that adapter availability handling cannot create a revision in a context whose cleanup is delayed.</summary>
        [Fact]
        public async Task AdapterAvailabilityDuringDelayedPlayContextTransition_DoesNotCreateGhostRevision()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId firstContext = PlayContextId.NewId();
            PlayContextId secondContext = PlayContextId.NewId();
            playContextTracker.NotifyTransition(firstContext);
            using var transitionEntered = new ManualResetEventSlim();
            using var releaseTransition = new ManualResetEventSlim();
            playContextTracker.Transitioned += transition =>
            {
                if (transition.NewPlayContextId == secondContext)
                {
                    transitionEntered.Set();
                    releaseTransition.Wait();
                }
            };
            var adapterTracker = new AdapterAvailabilityTracker();
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            long generation = 1;
            PublishConnected(adapterTracker, instanceId, generation);
            IAdapterResynchronizationToken firstBaselineToken = adapterTracker.TryClaimResynchronizationToken()!;
            Assert.True(publisher.ApplyResynchronizationBaseline(
                firstBaselineToken, firstContext, playContextTracker.TransitionGeneration, AreaId, 1).Accepted);
            adapterTracker.NotifyResynchronized(instanceId, generation, firstBaselineToken);

            Task transitionTask = Task.Run(() => playContextTracker.NotifyTransition(secondContext));
            Assert.True(transitionEntered.Wait(TimeSpan.FromSeconds(5)));

            PublishDisconnected(adapterTracker, instanceId, generation);

            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
            releaseTransition.Set();
            await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that adapter availability transitions advance populated-area revisions once each.</summary>
        [Fact]
        public void AdapterAvailabilityTransitions_AdvanceRevisionAndRequireFreshBaseline()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new AdapterAvailabilityTracker();
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            AdapterInstanceId instanceId = AdapterInstanceId.NewId();
            long firstGeneration = 1;
            PublishConnected(adapterTracker, instanceId, firstGeneration);
            IAdapterResynchronizationToken firstToken = adapterTracker.TryClaimResynchronizationToken()!;
            Assert.True(publisher.ApplyResynchronizationBaseline(
                firstToken, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
            adapterTracker.NotifyResynchronized(instanceId, firstGeneration, firstToken);
            RevisionNumber synchronizedRevision = publisher.CurrentRevision(AreaId);

            PublishDisconnected(adapterTracker, instanceId, firstGeneration);
            Assert.Equal(synchronizedRevision.Next(), publisher.CurrentRevision(AreaId));
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));

            long secondGeneration = 2;
            PublishConnected(adapterTracker, instanceId, secondGeneration);
            Assert.Equal(synchronizedRevision.Next().Next(), publisher.CurrentRevision(AreaId));
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
            IAdapterResynchronizationToken secondToken = adapterTracker.TryClaimResynchronizationToken()!;
            Assert.True(publisher.ApplyResynchronizationBaseline(
                secondToken, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
            adapterTracker.NotifyResynchronized(instanceId, secondGeneration, secondToken);

            Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
            Assert.Equal(42, value);
            Assert.Equal(synchronizedRevision.Next().Next(), publisher.CurrentRevision(AreaId));
        }

        /// <summary>
        /// Verifies the exact scenario captured play-context provenance exists to close: a value captured
        /// under one play context, then applied only after the tracker has already moved on to a later
        /// one, is rejected outright rather than being silently misattributed to the new context.
        /// </summary>
        [Fact]
        public void Apply_StaleCapturedPlayContext_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId capturedContext = PlayContextId.NewId();
            playContextTracker.NotifyTransition(capturedContext); // "Save A"
            long capturedGeneration = playContextTracker.TransitionGeneration;
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            playContextTracker.NotifyTransition(PlayContextId.NewId()); // "Save B" -- the delayed capture arrives after this

            bool accepted = publisher.Apply(
                adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration,
                capturedContext, capturedGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that a mismatched captured play-context id alone -- with a correct, current generation -- is rejected, isolating one side of the provenance check's OR condition.</summary>
        [Fact]
        public void Apply_CapturedPlayContextIdMismatchOnly_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(PlayContextId.NewId());
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.Apply(
                adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration,
                PlayContextId.NewId(), playContextTracker.TransitionGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a mismatched captured play-context generation alone -- with the correct, current context id -- is rejected, isolating the other side of the provenance check's OR condition.</summary>
        [Fact]
        public void Apply_CapturedPlayContextGenerationMismatchOnly_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.Apply(
                adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration,
                context, playContextTracker.TransitionGeneration + 1, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>
        /// Verifies the exact scenario resynchronization provenance exists to close, symmetric with
        /// <see cref="Apply_StaleCapturedPlayContext_RejectsWithoutApplying"/>: a resynchronization token
        /// claimed and a baseline captured under one play context remain valid tokens after a later
        /// play-context transition -- <see cref="IPlayContextTracker"/> and
        /// <see cref="IAdapterAvailabilityTracker"/> are independent authorities, so the transition never
        /// invalidates the token -- but applying that captured baseline under the new context must still
        /// be rejected rather than silently stored under it.
        /// </summary>
        [Fact]
        public void ApplyResynchronizationBaseline_StalePlayContextProvenance_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId capturedContext = PlayContextId.NewId();
            playContextTracker.NotifyTransition(capturedContext); // "Save A"
            long capturedGeneration = playContextTracker.TransitionGeneration;
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
            IAdapterResynchronizationToken token = adapterTracker.TryClaimResynchronizationToken()!;

            playContextTracker.NotifyTransition(PlayContextId.NewId()); // "Save B" -- the token remains valid across this

            bool accepted = publisher.ApplyResynchronizationBaseline(token, capturedContext, capturedGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
            Assert.Equal(RevisionNumber.Initial, publisher.CurrentRevision(AreaId));
        }

        /// <summary>Verifies that a mismatched captured play-context id alone -- with a correct, current generation -- is rejected for a resynchronization baseline, isolating one side of the provenance check's OR condition, symmetric with <see cref="Apply_CapturedPlayContextIdMismatchOnly_RejectsWithoutApplying"/>.</summary>
        [Fact]
        public void ApplyResynchronizationBaseline_CapturedPlayContextIdMismatchOnly_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(PlayContextId.NewId());
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.ApplyResynchronizationBaseline(
                adapterTracker.TryClaimResynchronizationToken()!, PlayContextId.NewId(), playContextTracker.TransitionGeneration, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Verifies that a mismatched captured play-context generation alone -- with the correct, current context id -- is rejected for a resynchronization baseline, isolating the other side of the provenance check's OR condition, symmetric with <see cref="Apply_CapturedPlayContextGenerationMismatchOnly_RejectsWithoutApplying"/>.</summary>
        [Fact]
        public void ApplyResynchronizationBaseline_CapturedPlayContextGenerationMismatchOnly_RejectsWithoutApplying()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
            var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

            bool accepted = publisher.ApplyResynchronizationBaseline(
                adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration + 1, AreaId, 42).Accepted;

            Assert.False(accepted);
            Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        }

        /// <summary>Commits and publishes a connected transition in one call, for tests that only care about the combined effect and not the two-step API split.</summary>
        private static void PublishConnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long generation)
        {
            AdapterAvailabilityTransition? transition = tracker.CommitConnected(instanceId, generation);
            if (transition is not null)
            {
                tracker.PublishTransition(transition);
            }
        }

        /// <summary>Commits and publishes a disconnected transition in one call, for tests that only care about the combined effect and not the two-step API split.</summary>
        private static void PublishDisconnected(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
        {
            AdapterAvailabilityTransition? transition = tracker.CommitDisconnected(instanceId, connectionGeneration);
            if (transition is not null)
            {
                tracker.PublishTransition(transition);
            }
        }

        private sealed class ForeignResynchronizationToken : IAdapterResynchronizationToken
        {
        }
    }

    /// <summary>Tests for <see cref="StatePublicationFeed"/>.</summary>
    public class StatePublicationFeedTests
    {
        private static readonly StateAreaId AreaId = new("character_health");
        private static readonly JsonElement Data = JsonDocument.Parse("""{"value":93.4}""").RootElement;

        private static (StatePublicationFeed Feed, FakeAdapterAvailabilityTracker AdapterTracker, FakePlayContextTracker PlayContextTracker, RegisteredStateAreaPolicy RegisteredAreas, PlayContextId Context)
            CreateReadyFeed()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var registeredAreas = new RegisteredStateAreaPolicy();
            registeredAreas.TryRegister(AreaId);
            var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas);
            return (feed, adapterTracker, playContextTracker, registeredAreas, context);
        }

        /// <summary>Verifies that a fresh, registered snapshot publishes: raises SnapshotChanged and becomes the value TryGetSnapshot returns.</summary>
        [Fact]
        public void PublishSnapshot_Fresh_RaisesSnapshotChangedAndUpdatesTryGetSnapshot()
        {
            (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
            StateSnapshotPublication? raised = null;
            feed.SnapshotChanged += publication => raised = publication;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.NotNull(raised);
            Assert.Equal(AreaId, raised!.StateArea);
            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
            Assert.Equal(raised, stored);
        }

        /// <summary>Verifies that a fresh, registered event publishes: raises EventOccurred and also becomes the value TryGetSnapshot returns, since an Event-mode area still answers baseline reads.</summary>
        [Fact]
        public void PublishEvent_Fresh_RaisesEventOccurredAndUpdatesTryGetSnapshot()
        {
            (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
            StateEventPublication? raised = null;
            feed.EventOccurred += publication => raised = publication;

            feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.NotNull(raised);
            Assert.Equal(AreaId, raised!.StateArea);
            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
            Assert.Equal(raised.Revision, stored!.Revision);
            Assert.Equal(raised.Data, stored.Data);
        }

        /// <summary>Verifies that publishing to an unregistered area is a silent no-op: no event, no stored value.</summary>
        [Fact]
        public void PublishSnapshot_AreaNotRegistered_DoesNothing()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var feed = new StatePublicationFeed(adapterTracker, playContextTracker, new RegisteredStateAreaPolicy());
            bool raised = false;
            feed.SnapshotChanged += _ => raised = true;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that publishing while the adapter is unavailable is a silent no-op.</summary>
        [Fact]
        public void PublishSnapshot_AdapterUnavailable_DoesNothing()
        {
            (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, _, PlayContextId context) = CreateReadyFeed();
            adapterTracker.Current = AdapterAvailability.Unavailable;
            bool raised = false;
            feed.SnapshotChanged += _ => raised = true;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>
        /// Verifies that publishing while the adapter still needs resynchronization still raises
        /// SnapshotChanged (a legitimate baseline must always be able to land), but a pull read through
        /// TryGetSnapshot withholds that same value until resynchronization actually completes -- a pull
        /// must never hand out state that could still be superseded by the rest of an in-progress
        /// resynchronization transaction.
        /// </summary>
        [Fact]
        public void PublishSnapshot_AdapterNeedsResynchronization_StillPublishesButTryGetSnapshotWithholdsIt()
        {
            // A resynchronization baseline is only ever accepted by
            // IStatePublisher<TState>.ApplyResynchronizationBaseline while NeedsResynchronization is
            // true, so this feed must not require it clear before publishing -- otherwise a legitimate
            // baseline could never be published at all.
            (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, _, PlayContextId context) = CreateReadyFeed();
            adapterTracker.NeedsResynchronization = true;
            bool raised = false;
            feed.SnapshotChanged += _ => raised = true;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.True(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));

            adapterTracker.NeedsResynchronization = false;
            Assert.True(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that an unchanged baseline raises only an availability hint after the adapter clears its resynchronization gate.</summary>
        [Fact]
        public void EstablishBaseline_WhileResynchronizing_RaisesAvailabilityAfterResynchronizationWithoutSnapshotChanged()
        {
            (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, _, PlayContextId context) = CreateReadyFeed();
            adapterTracker.NeedsResynchronization = true;
            bool snapshotChanged = false;
            bool snapshotAvailabilityChanged = false;
            bool resynchronizationGateWasClearAtNotification = false;
            feed.SnapshotChanged += _ => snapshotChanged = true;
            feed.SnapshotAvailabilityChanged += () =>
            {
                snapshotAvailabilityChanged = true;
                resynchronizationGateWasClearAtNotification = !adapterTracker.NeedsResynchronization;
            };

            feed.EstablishBaseline(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.False(snapshotChanged);
            Assert.False(snapshotAvailabilityChanged);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));

            IAdapterResynchronizationToken token = Assert.IsAssignableFrom<IAdapterResynchronizationToken>(adapterTracker.TryClaimResynchronizationToken());
            AdapterInstanceId instanceId = adapterTracker.CurrentInstanceId!.Value;
            adapterTracker.NotifyResynchronized(instanceId, adapterTracker.CurrentConnectionGeneration, token);

            Assert.False(snapshotChanged);
            Assert.True(snapshotAvailabilityChanged);
            Assert.True(resynchronizationGateWasClearAtNotification);
            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
            Assert.Equal(AreaId, stored!.StateArea);
        }

        /// <summary>Verifies that a publish stamped with a play context other than the current one is a silent no-op, isolating one side of the freshness check's OR condition.</summary>
        [Fact]
        public void PublishSnapshot_StalePlayContextId_DoesNothing()
        {
            (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, _) = CreateReadyFeed();
            bool raised = false;
            feed.SnapshotChanged += _ => raised = true;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, PlayContextId.NewId(), playContextTracker.TransitionGeneration, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that a publish stamped with a play-context generation other than the current one is a silent no-op, isolating the other side of the freshness check's OR condition.</summary>
        [Fact]
        public void PublishSnapshot_StalePlayContextGeneration_DoesNothing()
        {
            (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, PlayContextId context) = CreateReadyFeed();
            bool raised = false;
            feed.SnapshotChanged += _ => raised = true;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, playContextTracker.TransitionGeneration + 1, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that a second publish for the same area replaces the value TryGetSnapshot returns and raises again with the new value.</summary>
        [Fact]
        public void PublishSnapshot_SecondPublish_ReplacesStoredValueAndRaisesAgain()
        {
            (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
            var secondData = JsonDocument.Parse("""{"value":50.0}""").RootElement;
            var raised = new List<StateSnapshotPublication>();
            feed.SnapshotChanged += publication => raised.Add(publication);

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next().Next(), secondData, context, 1, DateTimeOffset.UtcNow);

            Assert.Equal(2, raised.Count);
            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
            Assert.Equal(RevisionNumber.Initial.Next().Next(), stored!.Revision);
            Assert.Equal(secondData, stored.Data);
        }

        /// <summary>Verifies that reading a state area that was never published reports unavailable.</summary>
        [Fact]
        public void TryGetSnapshot_NeverPublished_ReturnsFalse()
        {
            (StatePublicationFeed feed, _, _, _, _) = CreateReadyFeed();

            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that publishing to an unregistered area is a silent no-op for an event too, symmetric with <see cref="PublishSnapshot_AreaNotRegistered_DoesNothing"/>.</summary>
        [Fact]
        public void PublishEvent_AreaNotRegistered_DoesNothing()
        {
            var playContextTracker = new FakePlayContextTracker();
            PlayContextId context = PlayContextId.NewId();
            playContextTracker.NotifyTransition(context);
            var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
            var feed = new StatePublicationFeed(adapterTracker, playContextTracker, new RegisteredStateAreaPolicy());
            bool raised = false;
            feed.EventOccurred += _ => raised = true;

            feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that a stale play context is a silent no-op for an event too, symmetric with <see cref="PublishSnapshot_StalePlayContextId_DoesNothing"/> -- PublishEvent shares the same freshness gate as PublishSnapshot.</summary>
        [Fact]
        public void PublishEvent_StalePlayContextId_DoesNothing()
        {
            (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, _) = CreateReadyFeed();
            bool raised = false;
            feed.EventOccurred += _ => raised = true;

            feed.PublishEvent(AreaId, RevisionNumber.Initial, RevisionNumber.Initial.Next(), Data, PlayContextId.NewId(), playContextTracker.TransitionGeneration, DateTimeOffset.UtcNow);

            Assert.False(raised);
            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }

        /// <summary>Verifies that two different state areas publish and read independently, without one affecting the other.</summary>
        [Fact]
        public void PublishSnapshot_TwoDifferentAreas_AreIndependent()
        {
            (StatePublicationFeed feed, _, _, RegisteredStateAreaPolicy registeredAreas, PlayContextId context) = CreateReadyFeed();
            var otherAreaId = new StateAreaId("character_magicka");
            registeredAreas.TryRegister(otherAreaId);
            var otherData = JsonDocument.Parse("""{"value":71.0}""").RootElement;

            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
            feed.PublishSnapshot(otherAreaId, RevisionNumber.Initial.Next(), otherData, context, 1, DateTimeOffset.UtcNow);

            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? healthSnapshot));
            Assert.Equal(Data, healthSnapshot!.Data);
            Assert.True(feed.TryGetSnapshot(otherAreaId, out StateSnapshotPublication? magickaSnapshot));
            Assert.Equal(otherData, magickaSnapshot!.Data);
        }

        /// <summary>Verifies that one SnapshotChanged subscriber throwing does not prevent the stored value from updating or another subscriber from being invoked.</summary>
        [Fact]
        public void PublishSnapshot_SubscriberThrows_StoredValueStillUpdatesAndOtherSubscriberStillRuns()
        {
            (StatePublicationFeed feed, _, _, _, PlayContextId context) = CreateReadyFeed();
            bool otherSubscriberRan = false;
            feed.SnapshotChanged += _ => throw new InvalidOperationException("boom");
            feed.SnapshotChanged += _ => otherSubscriberRan = true;

            Exception? escaped = Record.Exception(() => feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow));

            Assert.Null(escaped);
            Assert.True(otherSubscriberRan);
            Assert.True(feed.TryGetSnapshot(AreaId, out StateSnapshotPublication? stored));
            Assert.Equal(Data, stored!.Data);
        }

        /// <summary>
        /// Verifies that any adapter availability transition -- proactive defense in depth alongside
        /// TryGetSnapshot's own re-check -- clears every previously stored snapshot across every area, so
        /// a later TryGetSnapshot can never resurface pre-transition data even with no new publish
        /// attempt in between.
        /// </summary>
        [Fact]
        public void AvailabilityChanged_AfterSuccessfulPublish_ClearsStoredSnapshotsAcrossAllAreas()
        {
            (StatePublicationFeed feed, FakeAdapterAvailabilityTracker adapterTracker, _, RegisteredStateAreaPolicy registeredAreas, PlayContextId context) = CreateReadyFeed();
            var otherAreaId = new StateAreaId("character_magicka");
            registeredAreas.TryRegister(otherAreaId);
            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
            feed.PublishSnapshot(otherAreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
            Assert.True(feed.TryGetSnapshot(AreaId, out _));
            Assert.True(feed.TryGetSnapshot(otherAreaId, out _));

            adapterTracker.PublishTransition(new AdapterAvailabilityTransition(AdapterAvailability.Available, AdapterAvailability.Unavailable, null, 1));

            Assert.False(feed.TryGetSnapshot(AreaId, out _));
            Assert.False(feed.TryGetSnapshot(otherAreaId, out _));
        }

        /// <summary>
        /// Verifies that a play-context transition -- proactive defense in depth alongside
        /// TryGetSnapshot's own re-check -- clears a previously stored snapshot, so a later
        /// TryGetSnapshot can never resurface pre-transition data even with no new publish attempt in
        /// between.
        /// </summary>
        [Fact]
        public void PlayContextTransitioned_AfterSuccessfulPublish_ClearsStoredSnapshot()
        {
            (StatePublicationFeed feed, _, FakePlayContextTracker playContextTracker, _, PlayContextId context) = CreateReadyFeed();
            feed.PublishSnapshot(AreaId, RevisionNumber.Initial.Next(), Data, context, 1, DateTimeOffset.UtcNow);
            Assert.True(feed.TryGetSnapshot(AreaId, out _));

            playContextTracker.NotifyTransition(PlayContextId.NewId());

            Assert.False(feed.TryGetSnapshot(AreaId, out _));
        }
    }

    /// <summary>Tests for live-state catalog validation and resynchronization plans.</summary>
    public class LiveStateCatalogTests
    {
        /// <summary>Verifies that the default catalog defines exactly the five state areas Stage 4's "Real capture and host integration" slice names, each with its documented update mode.</summary>
        [Fact]
        public void Default_DefinesExactlyTheFiveProductionStateAreasWithTheirUpdateModes()
        {
            Dictionary<string, UpdateMode> byId = LiveStateCatalog.Default.StateAreas.ToDictionary(area => area.Id.Value, area => area.UpdateMode);

            Assert.Equal(5, byId.Count);
            Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterHealthStateArea]);
            Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterMagickaStateArea]);
            Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterStaminaStateArea]);
            Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterXpStateArea]);
            Assert.Equal(UpdateMode.Event, byId[Constants.CharacterLevelStateArea]);
        }

        /// <summary>Verifies that the vitals capture unit is one coherent Fast sample feeding all three resource areas.</summary>
        [Fact]
        public void Default_VitalsCaptureUnit_IsOneFastSampleFeedingAllThreeResourceAreas()
        {
            CaptureUnitDefinition vitals = LiveStateCatalog.Default.CaptureUnits.Single(unit => unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);

            Assert.Equal(CaptureSourceKind.Sample, vitals.Source);
            Assert.Equal(RateClass.Fast, vitals.RateClass);
            Assert.Equal(SynchronizationRole.BaselineSample, vitals.SynchronizationRole);
            Assert.Equal(
                [new StateAreaId(Constants.CharacterHealthStateArea), new StateAreaId(Constants.CharacterMagickaStateArea), new StateAreaId(Constants.CharacterStaminaStateArea)],
                vitals.StateAreas);
        }

        /// <summary>Verifies that the experience capture unit is a Medium sample feeding only the experience area.</summary>
        [Fact]
        public void Default_XpCaptureUnit_IsMediumSampleFeedingOnlyXpArea()
        {
            CaptureUnitDefinition xp = LiveStateCatalog.Default.CaptureUnits.Single(unit => unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp && unit.Source == CaptureSourceKind.Sample);

            Assert.Equal(RateClass.Medium, xp.RateClass);
            Assert.Equal(SynchronizationRole.BaselineSample, xp.SynchronizationRole);
            Assert.Equal([new StateAreaId(Constants.CharacterXpStateArea)], xp.StateAreas);
        }

        /// <summary>Verifies that the level state area is fed by exactly two capture units: the baseline sample and the level-changed event, neither polled on any cadence.</summary>
        [Fact]
        public void Default_LevelStateArea_IsFedByBaselineSampleAndEventNeitherOnACadence()
        {
            var levelAreaId = new StateAreaId(Constants.CharacterLevelStateArea);
            List<CaptureUnitDefinition> feedingLevel = LiveStateCatalog.Default.CaptureUnits.Where(unit => unit.StateAreas.Contains(levelAreaId)).ToList();

            Assert.Equal(2, feedingLevel.Count);
            CaptureUnitDefinition baseline = Assert.Single(feedingLevel, unit => unit.Source == CaptureSourceKind.Sample);
            Assert.Equal((uint)CharacterSampleToken.CharacterLevelBaseline, baseline.CaptureKey);
            Assert.Null(baseline.RateClass);
            Assert.Equal(SynchronizationRole.BaselineSample, baseline.SynchronizationRole);
            CaptureUnitDefinition levelChanged = Assert.Single(feedingLevel, unit => unit.Source == CaptureSourceKind.Event);
            Assert.Equal((uint)CharacterEventKey.CharacterLevelChanged, levelChanged.CaptureKey);
            Assert.Null(levelChanged.RateClass);
            Assert.Equal(SynchronizationRole.PersistentEvent, levelChanged.SynchronizationRole);
        }

        /// <summary>
        /// Verifies that SynchronizationRole is not inferable from RateClass: the level baseline sample
        /// has no RateClass (it is polled on no cadence) yet is still a BaselineSample, while Vitals and
        /// XP have a RateClass yet are also BaselineSample -- the two properties vary independently, per
        /// <see cref="DovahLink.Host.SynchronizationRole"/>'s own documentation.
        /// </summary>
        [Fact]
        public void Default_SynchronizationRole_IsIndependentOfRateClass()
        {
            CaptureUnitDefinition[] baselineSamples = [.. LiveStateCatalog.Default.CaptureUnits.Where(unit => unit.SynchronizationRole == SynchronizationRole.BaselineSample)];

            Assert.Equal(3, baselineSamples.Length);
            Assert.Contains(baselineSamples, unit => unit.RateClass == RateClass.Fast);
            Assert.Contains(baselineSamples, unit => unit.RateClass == RateClass.Medium);
            Assert.Contains(baselineSamples, unit => unit.RateClass == null);

            CaptureUnitDefinition persistentEvent = Assert.Single(LiveStateCatalog.Default.CaptureUnits, unit => unit.SynchronizationRole == SynchronizationRole.PersistentEvent);
            Assert.Null(persistentEvent.RateClass);

            //  Exhaustiveness: every capture unit falls into exactly one of the two roles checked above,
            //  so a future unit added with no role assignment (or an unexpected one) cannot silently
            //  evade both counts.
            Assert.Equal(LiveStateCatalog.Default.CaptureUnits.Count, baselineSamples.Length + 1);
        }

        /// <summary>Verifies that every capture unit's state areas are already registered in the catalog's own state-area list, so nothing feeds an area the catalog does not also define.</summary>
        [Fact]
        public void Default_EveryCaptureUnitStateArea_IsDefinedInStateAreas()
        {
            var definedAreaIds = LiveStateCatalog.Default.StateAreas.Select(area => area.Id).ToHashSet();

            foreach (CaptureUnitDefinition unit in LiveStateCatalog.Default.CaptureUnits)
            {
                foreach (StateAreaId areaId in unit.StateAreas)
                {
                    Assert.Contains(areaId, definedAreaIds);
                }
            }
        }

        /// <summary>Verifies the inverse of <see cref="Default_EveryCaptureUnitStateArea_IsDefinedInStateAreas"/>: every defined state area is fed by at least one capture unit, so nothing is registered as servable without any way to ever receive a value.</summary>
        [Fact]
        public void Default_EveryStateArea_IsFedByAtLeastOneCaptureUnit()
        {
            var fedAreaIds = LiveStateCatalog.Default.CaptureUnits.SelectMany(unit => unit.StateAreas).ToHashSet();

            foreach (StateAreaDefinition area in LiveStateCatalog.Default.StateAreas)
            {
                Assert.Contains(area.Id, fedAreaIds);
            }
        }

        /// <summary>Verifies that the default catalog builds its event and baseline-sample plan from synchronization roles.</summary>
        [Fact]
        public void Default_BuildsExpectedResynchronizationPlan()
        {
            ResynchronizationPlan plan = LiveStateCatalog.Default.BuildResynchronizationPlan();

            Assert.Equal([(uint)CharacterEventKey.CharacterLevelChanged], plan.PersistentEventKeys);
            uint[] expectedSamples =
            [
                (uint)CharacterSampleToken.CharacterVitals,
                (uint)CharacterSampleToken.CharacterXp,
                (uint)CharacterSampleToken.CharacterLevelBaseline,
            ];
            Assert.Equal(expectedSamples.OrderBy(token => token), plan.BaselineSampleTokens.OrderBy(token => token));
        }

        /// <summary>Verifies that a future baseline sample joins the plan without a scheduling special case.</summary>
        [Fact]
        public void BuildResynchronizationPlan_IncludesFutureSampleToken()
        {
            var catalog = new LiveStateCatalog(
                [new CaptureUnitDefinition(CaptureSourceKind.Sample, 999, RateClass: null, SynchronizationRole.BaselineSample, [])],
                []);

            ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

            Assert.Equal([999u], plan.BaselineSampleTokens);
            Assert.Empty(plan.PersistentEventKeys);
        }

        /// <summary>Verifies that a future persistent event joins the plan without a connection special case.</summary>
        [Fact]
        public void BuildResynchronizationPlan_IncludesFuturePersistentEventKey()
        {
            var catalog = new LiveStateCatalog(
                [new CaptureUnitDefinition(CaptureSourceKind.Event, 998, RateClass: null, SynchronizationRole.PersistentEvent, [])],
                []);

            ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

            Assert.Equal([998u], plan.PersistentEventKeys);
            Assert.Empty(plan.BaselineSampleTokens);
        }

        /// <summary>Verifies that an empty catalog intentionally produces a valid empty plan.</summary>
        [Fact]
        public void BuildResynchronizationPlan_EmptyCatalog_ReturnsNoOpPlan()
        {
            ResynchronizationPlan plan = new LiveStateCatalog([], []).BuildResynchronizationPlan();

            Assert.Empty(plan.PersistentEventKeys);
            Assert.Empty(plan.BaselineSampleTokens);
        }

        /// <summary>Verifies that a repeated Sample identity is rejected even when the units feed different state areas.</summary>
        [Fact]
        public void LiveStateCatalog_DuplicateSampleIdentity_Throws()
        {
            CaptureUnitDefinition first = new(
                CaptureSourceKind.Sample,
                999,
                RateClass.Fast,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterHealthStateArea)]);
            CaptureUnitDefinition second = new(
                CaptureSourceKind.Sample,
                999,
                RateClass.Medium,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterMagickaStateArea)]);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new LiveStateCatalog([first, second], []));

            Assert.Contains("Sample", exception.Message);
            Assert.Contains("999", exception.Message);
        }

        /// <summary>Verifies that a repeated Event identity is rejected during catalog construction.</summary>
        [Fact]
        public void LiveStateCatalog_DuplicateEventIdentity_Throws()
        {
            CaptureUnitDefinition unit = new(
                CaptureSourceKind.Event,
                999,
                RateClass: null,
                SynchronizationRole.PersistentEvent,
                []);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new LiveStateCatalog([unit, unit], []));

            Assert.Contains("Event", exception.Message);
            Assert.Contains("999", exception.Message);
        }

        /// <summary>Verifies that one raw key can identify a Sample and an Event because they occupy different source namespaces.</summary>
        [Fact]
        public void LiveStateCatalog_SameKeyAcrossSources_IsAllowed()
        {
            var catalog = new LiveStateCatalog(
            [
                new CaptureUnitDefinition(CaptureSourceKind.Sample, 999, null, SynchronizationRole.BaselineSample, []),
                new CaptureUnitDefinition(CaptureSourceKind.Event, 999, null, SynchronizationRole.PersistentEvent, []),
            ], []);

            ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

            Assert.Equal([999u], plan.BaselineSampleTokens);
            Assert.Equal([999u], plan.PersistentEventKeys);
        }

        /// <summary>Verifies that Event captures with a rate class fail during catalog construction with a clear configuration error.</summary>
        [Fact]
        public void LiveStateCatalog_EventWithRateClass_Throws()
        {
            var eventUnit = new CaptureUnitDefinition(
                CaptureSourceKind.Event,
                999,
                RateClass.Fast,
                SynchronizationRole.PersistentEvent,
                []);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new LiveStateCatalog([eventUnit], []));

            Assert.Contains("Event", exception.Message);
            Assert.Contains("rate class", exception.Message);
        }

        /// <summary>Verifies that unique identities at both exact plan bounds remain valid and adding a duplicate identity fails.</summary>
        [Fact]
        public void BuildResynchronizationPlan_AtBoundUniqueCatalogSucceeds_DuplicateAppendThrows()
        {
            CaptureUnitDefinition[] eventUnits = Enumerable.Range(1, Constants.MaxResynchronizationEventKeys)
                .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)key, null, SynchronizationRole.PersistentEvent, []))
                .ToArray();
            CaptureUnitDefinition[] sampleUnits = Enumerable.Range(1, Constants.MaxResynchronizationSampleTokens)
                .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Sample, (uint)key, null, SynchronizationRole.BaselineSample, []))
                .ToArray();

            ResynchronizationPlan plan = new LiveStateCatalog([.. eventUnits, .. sampleUnits], []).BuildResynchronizationPlan();

            Assert.Equal(Constants.MaxResynchronizationEventKeys, plan.PersistentEventKeys.Count);
            Assert.Equal(Constants.MaxResynchronizationSampleTokens, plan.BaselineSampleTokens.Count);
            Assert.Throws<InvalidOperationException>(
                () => new LiveStateCatalog([.. eventUnits, eventUnits[0]], []));
            Assert.Throws<InvalidOperationException>(
                () => new LiveStateCatalog([.. sampleUnits, sampleUnits[0]], []));
        }

        /// <summary>Verifies that source and synchronization role must refer to the same key namespace.</summary>
        /// <param name="source">The capture unit's declared key namespace.</param>
        /// <param name="role">The capture unit's declared resynchronization role.</param>
        [Theory]
        [InlineData(CaptureSourceKind.Event, SynchronizationRole.BaselineSample)]
        [InlineData(CaptureSourceKind.Sample, SynchronizationRole.PersistentEvent)]
        public void BuildResynchronizationPlan_MismatchedSourceAndRole_Throws(
            CaptureSourceKind source,
            SynchronizationRole role)
        {
            var catalog = new LiveStateCatalog(
                [new CaptureUnitDefinition(source, 998, null, role, [])],
                []);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());

            Assert.Contains("synchronization role", exception.Message);
            Assert.Contains("source", exception.Message);
        }

        /// <summary>Verifies that zero catalog keys cannot become resynchronization intents.</summary>
        [Fact]
        public void BuildResynchronizationPlan_ZeroCaptureKey_Throws()
        {
            var catalog = new LiveStateCatalog(
                [new CaptureUnitDefinition(CaptureSourceKind.Sample, 0, null, SynchronizationRole.BaselineSample, [])],
                []);

            Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());
        }

        /// <summary>Verifies that an undefined synchronization role fails with a clear catalog error.</summary>
        [Fact]
        public void BuildResynchronizationPlan_UnsupportedRole_Throws()
        {
            var catalog = new LiveStateCatalog(
                [new CaptureUnitDefinition(CaptureSourceKind.Sample, 1, null, (SynchronizationRole)byte.MaxValue, [])],
                []);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());

            Assert.Contains("unsupported synchronization role", exception.Message);
        }

        /// <summary>Verifies that the Host plan builder fails instead of exceeding either private IPC count bound.</summary>
        [Fact]
        public void BuildResynchronizationPlan_OverBoundNamespace_Throws()
        {
            CaptureUnitDefinition[] tooManyEvents = Enumerable.Range(1, Constants.MaxResynchronizationEventKeys + 1)
                .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)key, null, SynchronizationRole.PersistentEvent, []))
                .ToArray();
            CaptureUnitDefinition[] tooManySamples = Enumerable.Range(1, Constants.MaxResynchronizationSampleTokens + 1)
                .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Sample, (uint)key, null, SynchronizationRole.BaselineSample, []))
                .ToArray();

            Assert.Throws<InvalidOperationException>(() => new LiveStateCatalog(tooManyEvents, []).BuildResynchronizationPlan());
            Assert.Throws<InvalidOperationException>(() => new LiveStateCatalog(tooManySamples, []).BuildResynchronizationPlan());
        }
    }

    // TODO(stage4-file-extraction): Move LiveStateCatalogFixtureTests to its own
    // LiveStateCatalogFixtureTests.cs in the post-Stage-4 structural cleanup PR.
    // Temporarily colocated with the other tests for the same class to hold
    // this PR's changed-file count down; extraction only, no behavior change.
    /// <summary>
    /// Verifies that the host's hardcoded live-state capture enums remain synchronized with the
    /// shared <c>adapter-host-ipc/fixtures/live-state-catalog.json</c> contract fixture, the same way
    /// <see cref="Adapter.Ipc.AdapterIpcConnectionTests"/> already does for the private-IPC rate limit.
    /// </summary>
    public class LiveStateCatalogFixtureTests
    {
        /// <summary>Reads the shared native/host live-state capture catalog fixture.</summary>
        private static JsonElement ReadFixture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "adapter-host-ipc", "fixtures", "live-state-catalog.json");
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        }

        /// <summary>Verifies that the host's hardcoded sample-token enum values remain synchronized with the shared contract fixture.</summary>
        [Fact]
        public void SampleTokens_MatchSharedCatalogFixture()
        {
            JsonElement sampleTokens = ReadFixture().GetProperty("sampleTokens");

            Assert.Equal((uint)CharacterSampleToken.CharacterVitals, sampleTokens.GetProperty("characterVitals").GetUInt32());
            Assert.Equal((uint)CharacterSampleToken.CharacterXp, sampleTokens.GetProperty("characterXp").GetUInt32());
            Assert.Equal((uint)CharacterSampleToken.CharacterLevelBaseline, sampleTokens.GetProperty("characterLevelBaseline").GetUInt32());
        }

        /// <summary>Verifies that the host's hardcoded event-key enum values remain synchronized with the shared contract fixture.</summary>
        [Fact]
        public void EventKeys_MatchSharedCatalogFixture()
        {
            JsonElement eventKeys = ReadFixture().GetProperty("eventKeys");

            Assert.Equal((uint)CharacterEventKey.CharacterLevelChanged, eventKeys.GetProperty("characterLevelChanged").GetUInt32());
        }
    }
}

// TODO(stage4-file-extraction): Move LiveStateApplicationTests and
// LiveStateSchedulerTests to their own files in the post-Stage-4
// structural cleanup PR. Temporarily colocated with the other state
// tests to hold this PR's changed-file count down; extraction only,
// no behavior change.
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
            var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas);
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
    }

    /// <summary>Tests for <see cref="LiveStateScheduler"/>.</summary>
    public class LiveStateSchedulerTests
    {
        /// <summary>A tiny interval map so tests run fast instead of waiting on production Fast/Medium cadences.</summary>
        private static readonly IReadOnlyDictionary<RateClass, TimeSpan> FastIntervals = new Dictionary<RateClass, TimeSpan>
        {
            [RateClass.Fast] = TimeSpan.FromMilliseconds(10),
            [RateClass.Medium] = TimeSpan.FromMilliseconds(25),
        };

        /// <summary>Verifies that a Fast-classed capture unit is sent repeatedly while an adapter is connected.</summary>
        [Fact]
        public async Task RunAsync_WhileConnected_RepeatedlySendsTheFastCaptureUnit()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(55));
            cancellation.Cancel();
            await run;

            Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2);
        }

        /// <summary>Verifies that the Medium-classed capture unit is sent on its own, slower cadence.</summary>
        [Fact]
        public async Task RunAsync_WhileConnected_SendsTheMediumCaptureUnitOnItsOwnCadence()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(55));
            cancellation.Cancel();
            await run;

            Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp) >= 1);
        }

        /// <summary>Verifies that only rate-classed capture units are ever sent -- never the event-sourced or baseline-only units, which the adapter's own resynchronization sequence handles instead.</summary>
        [Fact]
        public async Task RunAsync_WhileConnected_NeverSendsUnratedCaptureUnits()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(55));
            cancellation.Cancel();
            await run;

            Assert.DoesNotContain((uint)CharacterSampleToken.CharacterLevelBaseline, connection.ReadSampleCalls);
            Assert.Empty(connection.PairingDisplayCalls); // sanity: TrySendListenEvent is never wired through TrySendPairingDisplay
        }

        /// <summary>Verifies that no send is attempted while no adapter is connected.</summary>
        [Fact]
        public async Task RunAsync_WithNoConnection_SendsNothing()
        {
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            cancellation.Cancel();
            await run;
        }

        /// <summary>Verifies that pending resynchronization suppresses both Fast and Medium ordinary samples.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationPending_SuppressesFastAndMediumSamples()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that a play-context re-arm which commits before final queue admission rejects a sample prepared from an older snapshot.</summary>
        [Fact]
        public async Task RunAsync_RearmWinsWhileSampleIsPrepared_DoesNotEnqueueSample()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releasePreparation = new();
            TaskCompletionSource preparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedPreparations = 0;
            connection.OnPrepareReadSample = sampleToken =>
            {
                if (sampleToken == (uint)CharacterSampleToken.CharacterVitals && Interlocked.Exchange(ref blockedPreparations, 1) == 0)
                {
                    preparationStarted.TrySetResult();
                    releasePreparation.Wait();
                }
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            try
            {
                await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                tracker.RearmResynchronizationForPlayContextTransition();
            }
            finally
            {
                releasePreparation.Set();
                cancellation.Cancel();
                await run;
            }

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that a generation change after preparation rejects the old connection's sample and polling resumes on the resynchronized generation.</summary>
        [Fact]
        public async Task RunAsync_ConnectionGenerationChangesWhileSampleIsPrepared_RejectsOldConnection()
        {
            FakeAdapterIpcConnection oldConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releasePreparation = new();
            TaskCompletionSource preparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedPreparations = 0;
            oldConnection.OnPrepareReadSample = sampleToken =>
            {
                if (sampleToken == (uint)CharacterSampleToken.CharacterVitals && Interlocked.Exchange(ref blockedPreparations, 1) == 0)
                {
                    preparationStarted.TrySetResult();
                    releasePreparation.Wait();
                }
            };
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                ConnectionGeneration = 2,
            };
            TaskCompletionSource newConnectionPreparationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            newConnection.OnPrepareReadSample = _ => newConnectionPreparationStarted.TrySetResult();
            FakeAdapterIpcListener listener = new() { CurrentConnection = oldConnection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            try
            {
                await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AdapterAvailabilitySnapshot oldSnapshot = tracker.GetSnapshot();
                AdapterInstanceId oldInstanceId = oldSnapshot.CurrentInstanceId
                    ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
                Assert.NotNull(tracker.CommitDisconnected(oldInstanceId, oldSnapshot.ConnectionGeneration));
                Assert.NotNull(tracker.CommitConnected(AdapterInstanceId.NewId(), 2));
                listener.CurrentConnection = newConnection;
                CompleteAdapterResynchronization(tracker);
                Assert.False(tracker.NeedsResynchronization);
                releasePreparation.Set();
                await newConnectionPreparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(() => newConnection.ReadSampleCalls.Count > 0, run);
            }
            finally
            {
                releasePreparation.Set();
                cancellation.Cancel();
                await run;
            }

            Assert.Empty(oldConnection.ReadSampleCalls);
            Assert.NotEmpty(newConnection.ReadSampleCalls);
        }

        /// <summary>Verifies that an admitted send holds the tracker gate until queue admission and slot registration finish, so a re-arm commits afterward.</summary>
        [Fact]
        public async Task RunAsync_SendAdmissionWinsAgainstRearm_RearmWaitsForAdmission()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            using ManualResetEventSlim releaseAdmission = new();
            TaskCompletionSource admissionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource rearmStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockedAdmissions = 0;
            connection.OnTrySendReadSample = () =>
            {
                if (Interlocked.Exchange(ref blockedAdmissions, 1) == 0)
                {
                    admissionStarted.TrySetResult();
                    releaseAdmission.Wait();
                }
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker tracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, SingleVitalsCatalog(), new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), tracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            Task? rearm = null;
            try
            {
                await admissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                rearm = Task.Run(() =>
                {
                    rearmStarted.TrySetResult();
                    tracker.RearmResynchronizationForPlayContextTransition();
                });
                await rearmStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(TimeSpan.FromMilliseconds(20));
                Assert.False(rearm.IsCompleted);
            }
            finally
            {
                releaseAdmission.Set();
                if (rearm is not null)
                {
                    await rearm;
                }

                cancellation.Cancel();
                await run;
            }

            Assert.Equal([(uint)CharacterSampleToken.CharacterVitals], connection.ReadSampleCalls);
            Assert.True(tracker.NeedsResynchronization);
        }

        /// <summary>Verifies that an unavailable Adapter suppresses ordinary samples with an active play context.</summary>
        [Fact]
        public async Task RunAsync_AdapterUnavailable_SendsNothing()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = new();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that Fast and Medium polling resume after successful resynchronization.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationCompleted_ResumesFastAndMediumSamples()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(70));
            Assert.Empty(connection.ReadSampleCalls);

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(
                () => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals)
                    && connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterXp),
                run);
            cancellation.Cancel();
            await run;

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp));
        }

        /// <summary>Verifies that a long resynchronization pause resumes without replaying missed Fast or Medium ticks.</summary>
        [Fact]
        public async Task RunAsync_LongResynchronization_DoesNotCatchUpBurst()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildPendingAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(275));
            Assert.Empty(connection.ReadSampleCalls);

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 1, run);
            await Task.Delay(TimeSpan.FromMilliseconds(15));
            cancellation.Cancel();
            await run;

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.InRange(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterXp), 0, 1);
        }

        /// <summary>Verifies that resynchronization leaves a pre-existing ordinary request outstanding and sends no replacement.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationStartsWithOutstandingRequest_PreservesItsSlot()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            FakeLiveCaptureSink liveCaptureSink = new();
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();
            await Task.Delay(TimeSpan.FromMilliseconds(120));

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.DoesNotContain(42UL, connection.CancelCalls);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60));

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that a pre-resynchronization request can time out without sending its retry until resynchronization completes.</summary>
        [Fact]
        public async Task RunAsync_ResynchronizationPending_ProcessesOutstandingTimeoutWithoutRetry()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();

            await WaitUntilAsync(() => connection.CancelCalls.Contains(42UL), run);
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));

            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) > 1, run);
            cancellation.Cancel();
            await run;

            Assert.Equal(2, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that no send is attempted while no play context is active, even with a connection ready to accept one.</summary>
        [Fact]
        public async Task RunAsync_NoActivePlayContext_SendsNothing()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), new FakePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(55));
            cancellation.Cancel();
            await run;

            Assert.Empty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that sends start once a play context becomes active mid-run, having sent nothing before it did.</summary>
        [Fact]
        public async Task RunAsync_PlayContextBecomesActiveMidRun_StartsSendingOnceEstablished()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var playContextTracker = new FakePlayContextTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), playContextTracker, BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            Assert.Empty(connection.ReadSampleCalls);

            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            cancellation.Cancel();
            await run;

            Assert.NotEmpty(connection.ReadSampleCalls);
        }

        /// <summary>Verifies that <see cref="LiveStateScheduler.RunAsync"/> completes once its token is cancelled, rather than hanging.</summary>
        [Fact]
        public async Task RunAsync_Cancelled_Completes()
        {
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            cancellation.Cancel();

            Task completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(run, completed);
        }

        /// <summary>Verifies that sends start once a connection appears mid-run, having sent nothing before it did.</summary>
        [Fact]
        public async Task RunAsync_ConnectionAppearsMidRun_StartsSendingOnceAvailable()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            Assert.Empty(connection.ReadSampleCalls);

            listener.CurrentConnection = connection;
            await Task.Delay(TimeSpan.FromMilliseconds(30));
            cancellation.Cancel();
            await run;

            Assert.NotEmpty(connection.ReadSampleCalls);
        }

        /// <summary>
        /// Verifies that sends stop once the active connection is cleared mid-run: at most one already
        /// in-flight tick (read <see cref="FakeAdapterIpcListener.CurrentConnection"/> as non-null a
        /// moment before it was cleared) may still land, but sends never keep arriving on every
        /// subsequent tick afterward.
        /// </summary>
        [Fact]
        public async Task RunAsync_ConnectionClearedMidRun_StopsSending()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream()) { TrySendReadSampleResult = true, ConnectionGeneration = 1 };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(15));
            listener.CurrentConnection = null;
            int callsShortlyAfterClear = connection.ReadSampleCalls.Count;
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            callsShortlyAfterClear = Math.Max(callsShortlyAfterClear, connection.ReadSampleCalls.Count);
            await Task.Delay(TimeSpan.FromMilliseconds(60));
            cancellation.Cancel();
            await run;

            Assert.Equal(callsShortlyAfterClear, connection.ReadSampleCalls.Count);
        }

        /// <summary>Verifies that a catalog with no rate-classed capture units completes immediately instead of hanging.</summary>
        [Fact]
        public async Task RunAsync_NoRateClassedCaptureUnits_CompletesImmediately()
        {
            LiveStateCatalog emptyCatalog = new(
                captureUnits: [new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, RateClass: null, SynchronizationRole.PersistentEvent, [new StateAreaId(Constants.CharacterLevelStateArea)])],
                stateAreas: [new StateAreaDefinition(new StateAreaId(Constants.CharacterLevelStateArea), UpdateMode.Event)]);
            FakeAdapterIpcListener listener = new() { CurrentConnection = null };
            LiveStateScheduler scheduler = new(listener, emptyCatalog, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), FastIntervals);

            Task completed = await Task.WhenAny(scheduler.RunAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(completed.IsCompletedSuccessfully);
        }

        // ---- One-in-flight outstanding-request tracking ----

        /// <summary>
        /// A larger interval map for the outstanding-slot/timeout tests below, so their timing margins
        /// comfortably tolerate scheduler jitter under a loaded test run instead of racing a tight window
        /// against <see cref="Constants.LiveStateSampleTimeoutTicks"/> ticks of <see cref="FastIntervals"/>'
        /// much shorter cadence. Unrelated cadence tests above keep using <see cref="FastIntervals"/>, since
        /// they only need to prove a send happened at all, not race a fixed timeout window.
        /// </summary>
        private static readonly IReadOnlyDictionary<RateClass, TimeSpan> SlotIntervals = new Dictionary<RateClass, TimeSpan>
        {
            [RateClass.Fast] = TimeSpan.FromMilliseconds(50),
            [RateClass.Medium] = TimeSpan.FromMilliseconds(100),
        };

        /// <summary>Verifies that a unit with an outstanding, unanswered request skips every subsequent tick rather than sending a second, overlapping request.</summary>
        [Fact]
        public async Task RunAsync_RequestOutstanding_SkipsSubsequentTicksUntilReleased()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // About two Fast ticks' worth of time, comfortably under the five-tick (250ms) timeout
            // budget: only the first tick's send should ever land, since every later tick finds the
            // slot still outstanding with no reply ever reported.
            await Task.Delay(TimeSpan.FromMilliseconds(120));
            cancellation.Cancel();
            await run;

            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that a matching capture result immediately releases the outstanding slot, letting the very next tick send a fresh request instead of waiting out the timeout.</summary>
        [Fact]
        public async Task RunAsync_MatchingCaptureResultReceived_ReleasesSlotForNextTick()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));

            // Released immediately: the next Fast tick (well before the five-tick timeout would have
            // released it on its own) already sends again.
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2, run);
            cancellation.Cancel();
            await run;
        }

        /// <summary>Verifies that a result carrying a stale connection generation never releases the current slot, even though its sample token and correlation id both match.</summary>
        [Fact]
        public async Task RunAsync_ResultFromOlderConnectionGeneration_DoesNotReleaseSlot()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 2,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var liveCaptureSink = new FakeLiveCaptureSink(); // stale: the slot was sent under generation 2
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(2), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>
        /// Verifies that a Sample-source result for a token this scheduler does not poll (for example
        /// the level baseline sample, which is Sample-sourced but not rate-classed, so it has no
        /// outstanding-slot entry at all) is a harmless no-op that never touches an unrelated unit's slot.
        /// </summary>
        [Fact]
        public async Task RunAsync_SampleResultForNonRateClassedToken_IsHarmlessNoOp()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            Exception? exception = Record.Exception(() => liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, default, [0, 1]), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1)));
            Assert.Null(exception);
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that a result carrying a foreign correlation id never releases the current slot, even though its sample token and connection generation both match.</summary>
        [Fact]
        public async Task RunAsync_ResultWithMismatchedCorrelationId_DoesNotReleaseSlot()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                999, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>
        /// Verifies that a result reported for an Event-sourced key never releases a Sample unit's slot,
        /// even when the raw key values happen to collide (<see cref="CharacterSampleToken.CharacterVitals"/>
        /// and <see cref="CharacterEventKey.CharacterLevelChanged"/> both share the raw value 1).
        /// </summary>
        [Fact]
        public async Task RunAsync_EventResultWithCollidingRawKey_DoesNotReleaseSampleSlot()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var liveCaptureSink = new FakeLiveCaptureSink();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                42, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, default, [0, 1]), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one tick, comfortably under the five-tick (250ms) timeout budget

            cancellation.Cancel();
            await run;
            Assert.Equal(1, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
        }

        /// <summary>Verifies that an outstanding slot with no reply times out, best-effort cancels the stale correlation, and releases for exactly the next tick -- never a burst of catch-up sends.</summary>
        [Fact]
        public async Task RunAsync_NoReplyWithinTimeout_CancelsAndReleasesForNextTickOnly()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // Five ticks (the timeout budget) plus margin, with no reply ever reported.
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2, run);
            int countJustAfterRetry = connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals);
            await Task.Delay(TimeSpan.FromMilliseconds(60)); // just over one more tick's worth: must not burst past the single retry

            cancellation.Cancel();
            await run;

            Assert.Equal(countJustAfterRetry, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));
            Assert.Contains(42UL, connection.CancelCalls);
        }

        /// <summary>
        /// Verifies that a timed-out slot's best-effort cancel is never sent on a different connection
        /// generation than the one the timed-out request was actually sent on: a reconnect between the
        /// send and the timeout leaves the listener's current connection on a newer generation whose own
        /// correlation ids restart from the same small integers, so cancelling on it with the stale id
        /// could otherwise hit an unrelated request that connection genuinely has outstanding.
        /// </summary>
        [Fact]
        public async Task RunAsync_NoReplyWithinTimeoutButConnectionGenerationChanged_NeverCancelsOnTheNewerGeneration()
        {
            FakeAdapterIpcConnection originalConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = originalConnection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => originalConnection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            // A reconnect: a new connection object reusing the same small correlation id, on a newer
            // generation, before the original request's own timeout has elapsed.
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 2,
            };
            listener.CurrentConnection = newConnection;

            // Comfortably past the original request's five-tick (250ms) timeout, but short of a second one.
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            cancellation.Cancel();
            await run;

            Assert.DoesNotContain(42UL, newConnection.CancelCalls);
        }

        /// <summary>
        /// Verifies that a timed-out slot whose connection was cleared entirely -- not merely replaced by
        /// a newer generation -- before the timeout elapsed never throws attempting its best-effort
        /// cancel, and still resumes sending normally once a connection becomes available again.
        /// </summary>
        [Fact]
        public async Task RunAsync_NoReplyWithinTimeoutAndConnectionClearedEntirely_DoesNotThrowAndResumesOnceReconnected()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            AdapterAvailabilityTracker adapterAvailabilityTracker = BuildAvailableAdapterAvailabilityTracker();
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), Fixtures.BuildActivePlayContextTracker(), adapterAvailabilityTracker, SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            // Cleared entirely, not merely replaced, before the outstanding request's own timeout elapses.
            listener.CurrentConnection = null;
            AdapterAvailabilitySnapshot disconnectedSnapshot = adapterAvailabilityTracker.GetSnapshot();
            AdapterInstanceId disconnectedInstanceId = disconnectedSnapshot.CurrentInstanceId
                ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
            adapterAvailabilityTracker.CommitDisconnected(disconnectedInstanceId, disconnectedSnapshot.ConnectionGeneration);

            Exception? exception = await Record.ExceptionAsync(() => Task.Delay(TimeSpan.FromMilliseconds(350)));
            Assert.Null(exception);

            // Reconnecting lets the released slot send again.
            FakeAdapterIpcConnection newConnection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 99,
                ConnectionGeneration = 2,
            };
            adapterAvailabilityTracker.CommitConnected(AdapterInstanceId.NewId(), 2);
            CompleteAdapterResynchronization(adapterAvailabilityTracker);
            listener.CurrentConnection = newConnection;
            await WaitUntilAsync(() => newConnection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);

            cancellation.Cancel();
            await run;
        }

        /// <summary>
        /// Verifies that an immediate reply racing the scheduler's own send cannot be missed. The reply is
        /// applied from a genuinely different, already-started thread -- not a same-thread reentrant call,
        /// which the scheduler's own lock is reentrant against and so would not exercise this at all --
        /// deliberately given a full window to reach and attempt its own lock acquisition while the send
        /// call itself is held open, mirroring the real inbound read-loop thread racing the outbound send.
        /// Under the fix, that background attempt must block on the scheduler's own held lock for the
        /// whole window and only then correctly release the slot; without it, the background thread finds
        /// nothing holding the lock, observes the slot not yet marked outstanding, and misses it -- so this
        /// deterministically distinguishes the two, rather than depending on raw thread-scheduling luck.
        /// The slot must still end up released for the very next tick, rather than being silently dropped
        /// and stranding it until its own five-tick (250ms) timeout.
        /// </summary>
        [Fact]
        public async Task RunAsync_ImmediateReplyRacesQueueAdmissionOnAnotherThread_StillReleasesSlotForNextTick()
        {
            var liveCaptureSink = new FakeLiveCaptureSink();
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            using SemaphoreSlim backgroundReplyStarted = new(0, 1);
            // Filtered to the Vitals token specifically: this fake's single hook fires for every
            // queue-admission call, including the catalog's independent Xp/Medium loop's own ticks, and
            // must not race-release the Vitals slot for an unrelated unit's send.
            connection.OnTrySendReadSample = () =>
            {
                if (connection.ReadSampleCalls[^1] != (uint)CharacterSampleToken.CharacterVitals)
                {
                    return;
                }

                var backgroundReplyThread = new Thread(() =>
                {
                    backgroundReplyStarted.Release();
                    liveCaptureSink.ApplyCaptureResult(new IpcCaptureResultMessage(
                        42, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Unavailable, default, []), new AdapterCaptureSource(AdapterInstanceId.NewId(), 1));
                });
                backgroundReplyThread.Start();
                // Waits for the background thread to have at least started, then holds this call open for
                // a further deliberate window: long enough that thread-scheduling jitter cannot explain
                // either outcome -- under the fix, this call itself runs inside the scheduler's own held
                // lock (see LiveStateScheduler.RunSampleLoopAsync), so the background thread spends this
                // whole window genuinely blocked on it rather than racing to finish first.
                backgroundReplyStarted.Wait(TimeSpan.FromMilliseconds(200));
                Thread.Sleep(TimeSpan.FromMilliseconds(20));
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, liveCaptureSink, Fixtures.BuildActivePlayContextTracker(), BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            // Comfortably past the two ticks plus hook-sleep overhead a promptly-released slot needs to
            // send a second time, but well short of the five-tick (250ms) timeout a stuck slot would
            // instead need: a second send landing in this window is only possible if the raced reply
            // actually released the slot promptly, not via the timeout.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            cancellation.Cancel();
            await run;

            Assert.True(connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) >= 2);
        }

        /// <summary>
        /// Verifies that an outstanding slot pauses rather than losing its state when the play context
        /// clears mid-request: no timeout/retry burst happens while cleared, and normal ticking (up to
        /// and including the timeout-driven retry) resumes once a play context is active again.
        /// </summary>
        [Fact]
        public async Task RunAsync_PlayContextClearedWhileSlotOutstanding_PausesThenResumesOnceReestablished()
        {
            FakeAdapterIpcConnection connection = new(new MemoryStream())
            {
                TrySendReadSampleResult = true,
                TrySendReadSampleCorrelationId = 42,
                ConnectionGeneration = 1,
            };
            FakeAdapterIpcListener listener = new() { CurrentConnection = connection };
            var playContextTracker = new FakePlayContextTracker();
            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            LiveStateScheduler scheduler = new(listener, LiveStateCatalog.Default, new FakeLiveCaptureSink(), playContextTracker, BuildAvailableAdapterAvailabilityTracker(), SlotIntervals);
            using CancellationTokenSource cancellation = new();

            Task run = scheduler.RunAsync(cancellation.Token);
            await WaitUntilAsync(() => connection.ReadSampleCalls.Contains((uint)CharacterSampleToken.CharacterVitals), run);
            int countWhileOutstanding = connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals);

            playContextTracker.ClearCurrent();
            // Comfortably longer than the five-tick timeout budget: paused, so no retry burst can happen
            // even though the outstanding slot was never released.
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.Equal(countWhileOutstanding, connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals));

            playContextTracker.NotifyTransition(new PlayContextId(Guid.NewGuid()));
            // Normal ticking resumes: the timeout-driven retry this same slot was always going to reach
            // eventually still lands, proving the pause never lost or corrupted its state.
            await WaitUntilAsync(() => connection.ReadSampleCalls.Count(token => token == (uint)CharacterSampleToken.CharacterVitals) > countWhileOutstanding, run);

            cancellation.Cancel();
            await run;
        }

        /// <summary>
        /// Polls a condition until it becomes true, failing the test if it never does within a bounded
        /// time. If <paramref name="guardTask"/> completes first, awaits it so a fault in the scheduler's
        /// own run loop surfaces directly instead of being masked by a confusing timeout failure.
        /// </summary>
        private static async Task WaitUntilAsync(Func<bool> condition, Task guardTask)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition())
            {
                if (guardTask.IsCompleted)
                {
                    await guardTask;
                }

                Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
                await Task.Delay(2);
            }
        }

        /// <summary>Creates connected Adapter state with a completed resynchronization.</summary>
        /// <returns>Availability state that permits ordinary scheduled samples.</returns>
        private static AdapterAvailabilityTracker BuildAvailableAdapterAvailabilityTracker(long generation = 1)
        {
            AdapterAvailabilityTracker tracker = BuildPendingAdapterAvailabilityTracker(generation);
            CompleteAdapterResynchronization(tracker);
            return tracker;
        }

        /// <summary>Creates a catalog containing only the Fast Vitals unit for deterministic scheduler race tests.</summary>
        /// <returns>The default catalog's Vitals unit and state-area definitions.</returns>
        private static LiveStateCatalog SingleVitalsCatalog()
        {
            CaptureUnitDefinition vitals = LiveStateCatalog.Default.CaptureUnits
                .Single(unit => unit.Source == CaptureSourceKind.Sample
                    && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);
            return new LiveStateCatalog([vitals], LiveStateCatalog.Default.StateAreas);
        }

        /// <summary>Creates connected Adapter state with a pending resynchronization.</summary>
        /// <returns>Availability state that suppresses ordinary scheduled samples.</returns>
        private static AdapterAvailabilityTracker BuildPendingAdapterAvailabilityTracker(long generation = 1)
        {
            AdapterAvailabilityTracker tracker = new();
            tracker.CommitConnected(AdapterInstanceId.NewId(), generation);
            return tracker;
        }

        /// <summary>Completes the current Adapter resynchronization.</summary>
        /// <param name="tracker">The connected Adapter availability tracker to update.</param>
        /// <exception cref="InvalidOperationException">Thrown when the tracker has no pending connected resynchronization.</exception>
        private static void CompleteAdapterResynchronization(AdapterAvailabilityTracker tracker)
        {
            AdapterAvailabilitySnapshot snapshot = tracker.GetSnapshot();
            AdapterInstanceId instanceId = snapshot.CurrentInstanceId
                ?? throw new InvalidOperationException("The connected Adapter identity is missing.");
            IAdapterResynchronizationToken token = tracker.TryClaimResynchronizationToken()
                ?? throw new InvalidOperationException("No Adapter resynchronization is pending.");
            tracker.NotifyResynchronized(instanceId, snapshot.ConnectionGeneration, token);
        }
    }
}
