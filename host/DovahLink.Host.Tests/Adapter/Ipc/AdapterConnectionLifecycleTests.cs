using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="AdapterConnectionLifecycle"/>.</summary>
public class AdapterConnectionLifecycleTests
{
    /// <summary>Verifies that a lease is already active, with its generation already assigned, by the time an Available subscriber runs synchronously.</summary>
    [Fact]
    public void Activate_SetsLeaseActiveAndGeneration_BeforeTrackerPublishesAvailable()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        bool? wasActiveDuringCallback = null;
        long? generationDuringCallback = null;
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Available)
            {
                wasActiveDuringCallback = lifecycle.IsActive(lease);
                generationDuringCallback = lease.Generation;
            }
        };

        lifecycle.Activate(lease, AdapterInstanceId.NewId());

        Assert.True(wasActiveDuringCallback);
        Assert.Equal(1, generationDuringCallback);
    }

    /// <summary>Verifies that a lease is already inactive by the time an Unavailable subscriber runs synchronously.</summary>
    [Fact]
    public void Deactivate_MarksLeaseInactive_BeforeTrackerPublishesUnavailable()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());
        bool? wasActiveDuringCallback = null;
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Unavailable)
            {
                wasActiveDuringCallback = lifecycle.IsActive(lease);
            }
        };

        lifecycle.Deactivate(lease);

        Assert.False(wasActiveDuringCallback);
    }

    /// <summary>Verifies that activating a second lease for the same adapter instance supersedes the first: the old lease stops being eligible even though it shares the same instance id.</summary>
    [Fact]
    public void Activate_SecondLeaseSameAdapterInstanceId_SupersedesFirstLease()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        lifecycle.Activate(firstLease, instanceId);

        AdapterConnectionLease secondLease = lifecycle.CreateLease();
        lifecycle.Activate(secondLease, instanceId);

        Assert.False(lifecycle.IsActive(firstLease));
        Assert.True(lifecycle.IsActive(secondLease));
    }

    /// <summary>Verifies that a concurrent Activate cannot start allocating or publishing until a prior Activate's own (contained) subscriber has finished, so generations are always committed in allocation order.</summary>
    [Fact]
    public async Task ConcurrentActivations_PreserveMonotonicGenerationOrderingAndSerializePublication()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        using var firstCallbackEntered = new ManualResetEventSlim();
        using var releaseFirstCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Available && transition.ConnectionGeneration == 1)
            {
                firstCallbackEntered.Set();
                releaseFirstCallback.Wait();
            }
        };
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        AdapterConnectionLease secondLease = lifecycle.CreateLease();

        Task firstActivation = Task.Run(() => lifecycle.Activate(firstLease, AdapterInstanceId.NewId()));
        Assert.True(firstCallbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task secondActivation = Task.Run(() => lifecycle.Activate(secondLease, AdapterInstanceId.NewId()));

        Task completed = await Task.WhenAny(secondActivation, Task.Delay(100));
        Assert.NotSame(secondActivation, completed);
        releaseFirstCallback.Set();
        await Task.WhenAll(firstActivation, secondActivation);

        Assert.Equal(1, firstLease.Generation);
        Assert.Equal(2, secondLease.Generation);
        Assert.False(lifecycle.IsActive(firstLease));
        Assert.True(lifecycle.IsActive(secondLease));
    }

    /// <summary>Verifies that transitionGate still serializes a complete commit-then-publish sequence across two different kinds of operation, not only two of the same kind: a concurrent Activate cannot start allocating a new generation until a prior Deactivate's own (contained) subscriber has finished.</summary>
    [Fact]
    public async Task ConcurrentDeactivateThenActivate_SerializePublicationAcrossDifferentOperations()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        lifecycle.Activate(firstLease, AdapterInstanceId.NewId());
        using var deactivateCallbackEntered = new ManualResetEventSlim();
        using var releaseDeactivateCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Unavailable)
            {
                deactivateCallbackEntered.Set();
                releaseDeactivateCallback.Wait();
            }
        };

        Task deactivation = Task.Run(() => lifecycle.Deactivate(firstLease));
        Assert.True(deactivateCallbackEntered.Wait(TimeSpan.FromSeconds(5)));
        AdapterConnectionLease secondLease = lifecycle.CreateLease();
        Task activation = Task.Run(() => lifecycle.Activate(secondLease, AdapterInstanceId.NewId()));

        Task completed = await Task.WhenAny(activation, Task.Delay(100));
        Assert.NotSame(activation, completed);
        releaseDeactivateCallback.Set();
        await Task.WhenAll(deactivation, activation);

        Assert.Equal(2, secondLease.Generation);
        Assert.True(lifecycle.IsActive(secondLease));
    }

    /// <summary>Verifies that resynchronization completes while the lease is still active and clears the tracker's resynchronization requirement.</summary>
    [Fact]
    public void TryCompleteResynchronization_LeaseStillActive_CompletesAndClearsNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());

        bool completed = lifecycle.TryCompleteResynchronization(lease);

        Assert.True(completed);
        Assert.False(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a since-deactivated lease can no longer complete resynchronization, and that the tracker still reports needing one.</summary>
    [Fact]
    public void TryCompleteResynchronization_LeaseDeactivated_ReturnsFalseAndTrackerStillNeedsResynchronization()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());
        lifecycle.Deactivate(lease);

        bool completed = lifecycle.TryCompleteResynchronization(lease);

        Assert.False(completed);
        Assert.True(tracker.NeedsResynchronization);
    }

    /// <summary>Verifies that a throwing Available subscriber does not prevent Activate from completing, the lease from becoming active, or a later Deactivate from correctly reaching the tracker.</summary>
    [Fact]
    public void Activate_ThrowingAvailableSubscriber_LeaseStillActiveAndLaterDeactivateStillPublishesUnavailable()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");
        AdapterConnectionLease lease = lifecycle.CreateLease();

        lifecycle.Activate(lease, AdapterInstanceId.NewId());

        Assert.True(lifecycle.IsActive(lease));
        Assert.Equal(AdapterAvailability.Available, tracker.Current);

        lifecycle.Deactivate(lease);

        Assert.False(lifecycle.IsActive(lease));
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that a throwing Unavailable subscriber does not prevent Deactivate from completing or returning normally.</summary>
    [Fact]
    public void Deactivate_ThrowingUnavailableSubscriber_LeaseStillInactiveAndReturnsNormally()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());
        tracker.AvailabilityChanged += _ => throw new InvalidOperationException("Simulated subscriber failure.");

        lifecycle.Deactivate(lease);

        Assert.False(lifecycle.IsActive(lease));
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that a lease no one has activated is never mistaken for the active connection.</summary>
    [Fact]
    public void IsActive_NeverActivatedLease_ReturnsFalse()
    {
        var lifecycle = new AdapterConnectionLifecycle(new FakeAdapterAvailabilityTracker());
        AdapterConnectionLease lease = lifecycle.CreateLease();

        Assert.False(lifecycle.IsActive(lease));
    }

    /// <summary>Verifies that deactivating a lease that was never the active connection is a harmless no-op.</summary>
    [Fact]
    public void Deactivate_LeaseNeverActivated_DoesNotThrowAndDoesNotDisturbTracker()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();

        lifecycle.Deactivate(lease);

        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that activating a second lease for a different adapter instance still unambiguously supersedes the first: only one lease is ever active regardless of instance identity.</summary>
    [Fact]
    public void Activate_SecondLeaseDifferentAdapterInstanceId_SupersedesFirstLease()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        lifecycle.Activate(firstLease, AdapterInstanceId.NewId());

        AdapterConnectionLease secondLease = lifecycle.CreateLease();
        lifecycle.Activate(secondLease, AdapterInstanceId.NewId());

        Assert.False(lifecycle.IsActive(firstLease));
        Assert.True(lifecycle.IsActive(secondLease));
    }

    /// <summary>Verifies that deactivating a lease already superseded by a later activation (never explicitly deactivated itself) is a harmless no-op and does not disturb the tracker's view of the still-active lease.</summary>
    [Fact]
    public void Deactivate_LeaseAlreadySuperseded_DoesNotThrowAndDoesNotDisturbCurrentLease()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        lifecycle.Activate(firstLease, AdapterInstanceId.NewId());
        AdapterConnectionLease secondLease = lifecycle.CreateLease();
        lifecycle.Activate(secondLease, AdapterInstanceId.NewId());

        lifecycle.Deactivate(firstLease);

        Assert.True(lifecycle.IsActive(secondLease));
        Assert.Equal(AdapterAvailability.Available, tracker.Current);
    }

    /// <summary>Verifies that a lease already superseded by a later activation (never explicitly deactivated itself) can no longer complete resynchronization, purely on reference identity.</summary>
    [Fact]
    public void TryCompleteResynchronization_LeaseAlreadySuperseded_ReturnsFalse()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease firstLease = lifecycle.CreateLease();
        lifecycle.Activate(firstLease, AdapterInstanceId.NewId());
        AdapterConnectionLease secondLease = lifecycle.CreateLease();
        lifecycle.Activate(secondLease, AdapterInstanceId.NewId());

        bool completed = lifecycle.TryCompleteResynchronization(firstLease);

        Assert.False(completed);
    }

    /// <summary>
    /// Verifies that a concurrent <c>IsActive</c> call is never blocked by an Available subscriber
    /// still running -- unlike the tracker's old combined publish operation, publication happens
    /// after <c>stateGate</c> has already been released -- and already reflects the lease/tracker
    /// commit, which lands before the subscriber ever runs.
    /// </summary>
    [Fact]
    public async Task ConcurrentIsActive_DuringActivateSubscriber_ReturnsCommittedStateWithoutBlocking()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Available)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            }
        };
        AdapterConnectionLease lease = lifecycle.CreateLease();

        Task activation = Task.Run(() => lifecycle.Activate(lease, AdapterInstanceId.NewId()));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> concurrentIsActive = Task.Run(() => lifecycle.IsActive(lease));

        //  The subscriber is still blocked here: concurrentIsActive must still complete well before
        //  the long delay below, rather than waiting for releaseCallback.Set() further down.
        Task completed = await Task.WhenAny(concurrentIsActive, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(concurrentIsActive, completed);
        Assert.True(await concurrentIsActive);

        releaseCallback.Set();
        await activation;
        Assert.Equal(AdapterAvailability.Available, tracker.Current);
    }

    /// <summary>
    /// Verifies that a concurrent <c>IsActive</c> call is never blocked by an Unavailable subscriber
    /// still running, and already reflects the lease/tracker commit, which lands before the
    /// subscriber ever runs.
    /// </summary>
    [Fact]
    public async Task ConcurrentIsActive_DuringDeactivateSubscriber_ReturnsCommittedStateWithoutBlocking()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Unavailable)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
            }
        };

        Task deactivation = Task.Run(() => lifecycle.Deactivate(lease));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> concurrentIsActive = Task.Run(() => lifecycle.IsActive(lease));

        Task completed = await Task.WhenAny(concurrentIsActive, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(concurrentIsActive, completed);
        Assert.False(await concurrentIsActive);

        releaseCallback.Set();
        await deactivation;
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }

    /// <summary>Verifies that a concurrent <c>IsActive</c> call is unaffected by an Available subscriber that ultimately throws: it is neither blocked by the still-running subscriber nor corrupted by its eventual exception.</summary>
    [Fact]
    public async Task ConcurrentIsActive_DuringActivateWithThrowingSubscriber_ReturnsCommittedStateWithoutBlocking()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Available)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
                throw new InvalidOperationException("Simulated subscriber failure.");
            }
        };
        AdapterConnectionLease lease = lifecycle.CreateLease();

        Task activation = Task.Run(() => lifecycle.Activate(lease, AdapterInstanceId.NewId()));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> concurrentIsActive = Task.Run(() => lifecycle.IsActive(lease));

        Task completed = await Task.WhenAny(concurrentIsActive, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(concurrentIsActive, completed);
        Assert.True(await concurrentIsActive);

        releaseCallback.Set();
        await activation;
        Assert.Equal(AdapterAvailability.Available, tracker.Current);
    }

    /// <summary>Verifies that a concurrent <c>IsActive</c> call is unaffected by an Unavailable subscriber that ultimately throws: it is neither blocked by the still-running subscriber nor corrupted by its eventual exception.</summary>
    [Fact]
    public async Task ConcurrentIsActive_DuringDeactivateWithThrowingSubscriber_ReturnsCommittedStateWithoutBlocking()
    {
        var tracker = new AdapterAvailabilityTracker();
        var lifecycle = new AdapterConnectionLifecycle(tracker);
        AdapterConnectionLease lease = lifecycle.CreateLease();
        lifecycle.Activate(lease, AdapterInstanceId.NewId());
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        tracker.AvailabilityChanged += transition =>
        {
            if (transition.Current == AdapterAvailability.Unavailable)
            {
                callbackEntered.Set();
                releaseCallback.Wait();
                throw new InvalidOperationException("Simulated subscriber failure.");
            }
        };

        Task deactivation = Task.Run(() => lifecycle.Deactivate(lease));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<bool> concurrentIsActive = Task.Run(() => lifecycle.IsActive(lease));

        Task completed = await Task.WhenAny(concurrentIsActive, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(concurrentIsActive, completed);
        Assert.False(await concurrentIsActive);

        releaseCallback.Set();
        await deactivation;
        Assert.Equal(AdapterAvailability.Unavailable, tracker.Current);
    }
}
