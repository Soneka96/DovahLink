using DovahLink.Host.Adapter;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;
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
        _ = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

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
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

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
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

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
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

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
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

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
        var trigger = new PlayContextResynchronizationTrigger(playContextTracker, availabilityTracker, listener);

        trigger.HandleTransition(new PlayContextTransition(PlayContextId.NewId(), null));

        Assert.False(availabilityTracker.NeedsResynchronization);
        Assert.Equal(0, connection.ResynchronizeRequestCalls);
        Assert.Equal(0, connection.RequestCloseCalls);
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
}
