using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>Tests for <see cref="PlayContextResynchronizationTrigger"/>.</summary>
public class PlayContextResynchronizationTriggerTests
{
    /// <summary>Verifies that constructing the trigger subscribes it to the play-context tracker, so a real transition re-arms the availability tracker and sends a fresh request on the currently active connection.</summary>
    [Fact]
    public void RealTransition_RearmsAvailabilityTrackerAndSendsFreshRequestOnCurrentConnection()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        Resynchronize(availabilityTracker, instanceId, 1);
        Assert.False(availabilityTracker.NeedsResynchronization);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        _ = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        Assert.True(availabilityTracker.NeedsResynchronization);
        Assert.Equal(1, connection.ResynchronizeRequestCalls);
    }

    /// <summary>Verifies that a transition with no currently active connection still re-arms the availability tracker, and does not throw for the missing connection.</summary>
    [Fact]
    public void HandleTransition_NoCurrentConnection_StillRearmsAndDoesNotThrow()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        Resynchronize(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener { CurrentConnection = null };
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        Exception? exception = Record.Exception(() => trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId())));

        Assert.Null(exception);
        Assert.True(availabilityTracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that every call unconditionally re-arms and re-sends -- no internal state suppresses a
    /// second transition, matching the documented "no additional gating" contract.
    /// </summary>
    [Fact]
    public void HandleTransition_CalledSeveralTimes_EachCallRearmsAndSendsAgain()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId()));
        Resynchronize(availabilityTracker, instanceId, 1);
        trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId()));
        Resynchronize(availabilityTracker, instanceId, 1);
        trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId()));

        Assert.Equal(3, connection.ResynchronizeRequestCalls);
        Assert.True(availabilityTracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a failed send (for example a full outbound queue) never throws -- this trigger
    /// is best-effort, matching every other host-directed send in this area -- and forces the
    /// connection closed instead of leaving the re-armed requirement with no request ever having gone
    /// out: the adapter's normal reconnect then drives a fresh initial resynchronization.
    /// </summary>
    [Fact]
    public void HandleTransition_SendFails_ClosesConnectionAndDoesNotThrow()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = false };
        listener.CurrentConnection = connection;
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        Exception? exception = Record.Exception(() => trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId())));

        Assert.Null(exception);
        Assert.Equal(1, connection.ResynchronizeRequestCalls);
        Assert.Equal(1, connection.RequestCloseCalls);
    }

    /// <summary>Verifies that a successful send never forces the connection closed -- RequestClose is reserved for the failure path alone.</summary>
    [Fact]
    public void HandleTransition_SendSucceeds_DoesNotRequestClose()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId()));

        Assert.Equal(1, connection.ResynchronizeRequestCalls);
        Assert.Equal(0, connection.RequestCloseCalls);
    }

    /// <summary>
    /// Verifies that a transition to a null play context (the play context ending) neither re-arms
    /// resynchronization nor sends a request: no play context exists to resynchronize.
    /// </summary>
    [Fact]
    public void HandleTransition_NewContextIsNull_DoesNotRearmOrSend()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        Resynchronize(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, new FakeResynchronizationTransactionCoordinator());

        trigger.HandleTransition(new PlayContextTransition(PlayContextId.NewId(), null));

        Assert.False(availabilityTracker.NeedsResynchronization);
        Assert.Equal(0, connection.ResynchronizeRequestCalls);
        Assert.Equal(0, connection.RequestCloseCalls);
    }

    /// <summary>
    /// Verifies that a real transition immediately supersedes the coordinator's own tracked
    /// transaction via <see cref="IResynchronizationTransactionCoordinator.BeginTransaction"/>, using
    /// the just-rearmed availability snapshot's instance and connection generation together with the
    /// play-context tracker's own just-committed transition generation.
    /// </summary>
    [Fact]
    public void HandleTransition_ConnectedAdapter_BeginsTransactionWithCurrentTuple()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        var coordinator = new FakeResynchronizationTransactionCoordinator();
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, coordinator);

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        var call = Assert.Single(coordinator.BeginTransactionCalls);
        Assert.Equal(instanceId, call.InstanceId);
        Assert.Equal(1, call.ConnectionGeneration);
        Assert.Equal(playContextTracker.Current, call.PlayContextId);
        Assert.Equal(1, call.PlayContextGeneration);
    }

    /// <summary>Verifies that the coordinator's own transaction is still superseded even when the send itself fails and this trigger closes the connection -- the coordinator's requirement for the new context must not depend on the send succeeding.</summary>
    [Fact]
    public void HandleTransition_SendFails_StillBeginsTransactionBeforeClosing()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = false };
        listener.CurrentConnection = connection;
        var coordinator = new FakeResynchronizationTransactionCoordinator();
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, coordinator);

        playContextTracker.NotifyTransition(PlayContextId.NewId());

        Assert.Single(coordinator.BeginTransactionCalls);
        Assert.Equal(1, connection.RequestCloseCalls);
    }

    /// <summary>Verifies that no adapter connected leaves the coordinator's tracked transaction untouched -- there is no live connection generation for a play-context trigger to supersede anything against.</summary>
    [Fact]
    public void HandleTransition_NoAdapterConnected_DoesNotBeginTransaction()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        var listener = new FakeAdapterIpcListener();
        var coordinator = new FakeResynchronizationTransactionCoordinator();
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, coordinator);

        trigger.HandleTransition(new PlayContextTransition(null, PlayContextId.NewId()));

        Assert.Empty(coordinator.BeginTransactionCalls);
    }

    /// <summary>
    /// Reproduces the play-context-supersession lifecycle gap end to end: without this trigger
    /// invalidating the coordinator's tracked transaction the moment it sends the new resynchronize
    /// request, the coordinator would keep tracking context A until B's first baseline or result ever
    /// reached it -- leaving A's own watchdog free to expire and recover the connection context B now
    /// owns. With the fix, a real transition from A to B supersedes A immediately: A's watchdog can
    /// never fire, even though no capture for B ever arrives at the coordinator, while B still gets
    /// its own live, independent watchdog.
    /// </summary>
    /// <remarks>
    /// Notifies B with no delay after A: superseding a tracked transaction cancels the previous
    /// watchdog's <see cref="CancellationTokenSource"/> synchronously, inside the same call stack as
    /// <see cref="FakePlayContextTracker.NotifyTransition"/> -- it does not depend on any elapsed real
    /// time, only on the supersession call happening before the watchdog's own deadline, which a
    /// same-thread, no-await second call trivially satisfies regardless of scheduler load. The
    /// previous version instead separated the two notifications with a fixed
    /// <c>Task.Delay(200)</c> and asserted "no recovery yet" at a fixed absolute offset chosen to
    /// sit between A's and B's deadlines -- under Windows/CI scheduler jitter, that
    /// <c>Task.Delay(200)</c> could itself run long enough for A's real 300ms watchdog to have
    /// already elapsed and recorded a recovery before the test ever called
    /// <see cref="FakePlayContextTracker.NotifyTransition"/> for B, which is exactly the observed CI
    /// failure (<c>Assert.Empty()</c> seeing <c>[1]</c>). Asserting only the final state after a
    /// bounded wait for B's own eventual recovery removes that window entirely: the short watchdog
    /// timeout below only needs to be large enough for the immediate, same-thread supersession to
    /// consistently land before it -- not to carve out a race-free gap between two wall-clock
    /// deadlines.
    /// </remarks>
    [Fact]
    public async Task HandleTransition_SupersedesTrackedCoordinatorTransaction_OldWatchdogCannotRecoverNewerContext()
    {
        var playContextTracker = new FakePlayContextTracker();
        var availabilityTracker = new AdapterAvailabilityTracker();
        AdapterInstanceId instanceId = AdapterInstanceId.NewId();
        Connect(availabilityTracker, instanceId, 1);
        var continuityRecovery = new FakeAdapterContinuityRecovery();
        var coordinator = new ResynchronizationTransactionCoordinator(
            LiveStateCatalog.Default, availabilityTracker, continuityRecovery, TimeSpan.FromMilliseconds(300));
        var listener = new FakeAdapterIpcListener();
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { TrySendResynchronizeRequestResult = true };
        listener.CurrentConnection = connection;
        _ = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener, coordinator);

        playContextTracker.NotifyTransition(PlayContextId.NewId()); // Context A: arms its own 300ms watchdog.
        playContextTracker.NotifyTransition(PlayContextId.NewId()); // Context B: supersedes A immediately, before A's watchdog can elapse.

        // B never completed, so it must still fire on its own bound, proving the trigger armed a
        // real watchdog for B rather than leaving it unbounded.
        await WaitUntilAsync(() => continuityRecovery.RecoveryRequests.Count > 0);

        // Exactly one recovery, ever: had A's watchdog not truly been cancelled, it would have
        // independently elapsed and appended its own entry by the time B's has fired.
        Assert.Equal([1L], continuityRecovery.RecoveryRequests);
    }

    /// <summary>Commits and publishes a connected transition in one call.</summary>
    private static void Connect(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long generation)
    {
        AdapterAvailabilityTransition? transition = tracker.CommitConnected(instanceId, generation);
        if (transition is not null)
        {
            tracker.PublishTransition(transition);
        }
    }

    /// <summary>Claims the current connection's resynchronization token and reports it resynchronized in one call.</summary>
    private static void Resynchronize(IAdapterAvailabilityTracker tracker, AdapterInstanceId instanceId, long connectionGeneration)
    {
        IAdapterResynchronizationToken? token = tracker.TryClaimResynchronizationToken();
        if (token is not null)
        {
            tracker.NotifyResynchronized(instanceId, connectionGeneration, token);
        }
    }

    /// <summary>Polls <paramref name="condition"/> until it is true, rather than assuming a fixed delay proves a timer continuation has already run.</summary>
    /// <param name="condition">The condition to poll.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(10);
        }
    }
}
