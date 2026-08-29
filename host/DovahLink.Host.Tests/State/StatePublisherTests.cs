using DovahLink.Host.Identity;
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
        var publisher = new StatePublisher<int>(new RevisionTracker(), new FakePlayContextTracker(), new FakeAdapterAvailabilityTracker());

        Assert.Throws<InvalidOperationException>(() => publisher.Apply(AreaId, 1));
    }

    /// <summary>Verifies that the first applied value advances the revision past the initial revision and becomes the current value.</summary>
    [Fact]
    public void Apply_FirstValue_AdvancesRevisionAndBecomesCurrent()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

        publisher.Apply(AreaId, 42);

        Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
        Assert.Equal(42, value);
        Assert.Equal(RevisionNumber.Initial.Next(), publisher.CurrentRevision(AreaId));
    }

    /// <summary>Verifies that applying the same value again does not advance the revision a second time.</summary>
    [Fact]
    public void Apply_SameValueAgain_DoesNotAdvanceRevision()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);
        RevisionNumber revisionAfterFirstApply = publisher.CurrentRevision(AreaId);

        publisher.Apply(AreaId, 42);

        Assert.Equal(revisionAfterFirstApply, publisher.CurrentRevision(AreaId));
    }

    /// <summary>Verifies that applying a genuinely different value advances the revision again.</summary>
    [Fact]
    public void Apply_DifferentValue_AdvancesRevisionAgain()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);

        publisher.Apply(AreaId, 43);

        Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
        Assert.Equal(43, value);
        Assert.Equal(RevisionNumber.Initial.Next().Next(), publisher.CurrentRevision(AreaId));
    }

    /// <summary>Verifies that a value is reported unavailable while the adapter is unavailable, even though it was already applied.</summary>
    [Fact]
    public void TryGetCurrentValue_AdapterUnavailable_ReturnsUnavailable()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);

        adapterTracker.Current = AdapterAvailability.Unavailable;

        Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
    }

    /// <summary>Verifies that a value is reported unavailable while the adapter still needs resynchronization, even if it reports available.</summary>
    [Fact]
    public void TryGetCurrentValue_AdapterNeedsResynchronization_ReturnsUnavailable()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available, NeedsResynchronization = true };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);

        Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
    }

    /// <summary>Verifies that a value becomes visible again once the adapter is available and resynchronized.</summary>
    [Fact]
    public void TryGetCurrentValue_AfterResynchronization_ReturnsValueAgain()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Unavailable };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);

        adapterTracker.Current = AdapterAvailability.Available;
        adapterTracker.NeedsResynchronization = false;

        Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
        Assert.Equal(42, value);
    }

    /// <summary>Verifies that a play-context transition clears the previous context's value and resets the revision for the new context.</summary>
    [Fact]
    public void PlayContextTransition_ClearsValueAndResetsRevision()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        publisher.Apply(AreaId, 42);

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
        publisher.Apply(AreaId, 42);
        playContextTracker.NotifyTransition(PlayContextId.NewId());

        publisher.Apply(AreaId, 7);

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
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);
        var otherAreaId = new StateAreaId("Inventory");

        publisher.Apply(AreaId, 42);

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

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        publisher.Apply(AreaId, 1);
        Assert.True(publisher.TryGetCurrentValue(AreaId, out int value));
        Assert.Equal(1, value);
    }

    /// <summary>
    /// Verifies that Apply still records a captured value and advances its revision even while the
    /// adapter is reported unavailable -- availability only masks reads, it does not reject writes.
    /// </summary>
    [Fact]
    public void Apply_WhileAdapterUnavailable_StillAdvancesRevisionButStaysMasked()
    {
        var playContextTracker = new FakePlayContextTracker();
        playContextTracker.NotifyTransition(PlayContextId.NewId());
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Unavailable };
        var publisher = new StatePublisher<int>(new RevisionTracker(), playContextTracker, adapterTracker);

        publisher.Apply(AreaId, 42);

        Assert.Equal(RevisionNumber.Initial.Next(), publisher.CurrentRevision(AreaId));
        Assert.False(publisher.TryGetCurrentValue(AreaId, out _));
    }
}
