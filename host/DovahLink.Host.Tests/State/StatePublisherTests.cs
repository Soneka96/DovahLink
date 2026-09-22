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


}
