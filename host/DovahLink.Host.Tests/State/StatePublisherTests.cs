using DovahLink.Host.Adapter;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.State;

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
        adapterTracker.NotifyResynchronized(instanceId, firstGeneration);
        Assert.False(publisher.ApplyResynchronizationBaseline(staleToken, context, generation, AreaId, 2).Accepted);

        long secondGeneration = 2;
        PublishConnected(adapterTracker, instanceId, secondGeneration);

        Assert.False(publisher.ApplyResynchronizationBaseline(staleToken, context, generation, AreaId, 2).Accepted);
        Assert.False(publisher.ApplyResynchronizationBaseline(new ForeignResynchronizationToken(), context, generation, AreaId, 2).Accepted);
        Assert.True(publisher.ApplyResynchronizationBaseline(
            adapterTracker.TryClaimResynchronizationToken()!, context, generation, AreaId, 2).Accepted);
        adapterTracker.NotifyResynchronized(instanceId, secondGeneration);
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
        Assert.True(publisher.ApplyResynchronizationBaseline(
            adapterTracker.TryClaimResynchronizationToken()!, firstContext, playContextTracker.TransitionGeneration, AreaId, 1).Accepted);
        adapterTracker.NotifyResynchronized(instanceId, generation);

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
        Assert.True(publisher.ApplyResynchronizationBaseline(
            adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
        adapterTracker.NotifyResynchronized(instanceId, firstGeneration);
        RevisionNumber synchronizedRevision = publisher.CurrentRevision(AreaId);

        PublishDisconnected(adapterTracker, instanceId, firstGeneration);
        Assert.Equal(synchronizedRevision.Next(), publisher.CurrentRevision(AreaId));
        Assert.False(publisher.TryGetCurrentValue(AreaId, out _));

        long secondGeneration = 2;
        PublishConnected(adapterTracker, instanceId, secondGeneration);
        Assert.Equal(synchronizedRevision.Next().Next(), publisher.CurrentRevision(AreaId));
        Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
        Assert.True(publisher.ApplyResynchronizationBaseline(
            adapterTracker.TryClaimResynchronizationToken()!, context, playContextTracker.TransitionGeneration, AreaId, 42).Accepted);
        adapterTracker.NotifyResynchronized(instanceId, secondGeneration);

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
