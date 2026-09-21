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

/// <summary>Tests for <see cref="AdapterIpcListener"/>.</summary>
public class AdapterIpcListenerTests
{
    /// <summary>Verifies that binding with port zero lets the operating system assign a real, nonzero port.</summary>
    [Fact]
    public void Constructor_PortZero_BindsAnAssignedPort()
    {
        using var listener = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));

        Assert.NotEqual(0, listener.BoundPort);
    }

    /// <summary>Verifies that the default listener configuration uses the operating system's assigned port.</summary>
    [Fact]
    public void Constructor_DefaultConfiguration_BindsAnAssignedPort()
    {
        using var listener = new AdapterIpcListener(stream => new FakeAdapterIpcConnection(stream));

        Assert.Equal(0, Constants.AdapterIpcLoopbackPort);
        Assert.NotEqual(0, listener.BoundPort);
    }

    /// <summary>Verifies that the options-based constructor binds the configured port and wires the supplied connection factory.</summary>
    [Fact]
    public async Task Constructor_OptionsAndConnectionFactory_BindsConfiguredPortAndUsesFactory()
    {
        var connectionFactory = new StubAdapterConnectionFactory(stream => new FakeAdapterIpcConnection(stream));
        using var listener = new AdapterIpcListener(new AdapterIpcOptions(0), connectionFactory);

        Assert.NotEqual(0, listener.BoundPort);

        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);
        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connectionFactory.CreateCallCount == 1);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a fresh listener reports no active connection.</summary>
    [Fact]
    public void CurrentConnection_BeforeAnyAccept_IsNull()
    {
        using var listener = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));

        Assert.Null(listener.CurrentConnection);
    }

    /// <summary>Verifies that accepting a connection reports it as current while it runs, and clears it once it ends.</summary>
    [Fact]
    public async Task RunAsync_AcceptedConnection_ReportsCurrentWhileRunningThenClears()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.NotNull(listener.CurrentConnection);

        connections.ElementAt(0).Complete();
        await WaitUntilAsync(() => listener.CurrentConnection is null);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the listener registers its active connection with continuity recovery and clears it after ending.</summary>
    [Fact]
    public async Task RunAsync_AcceptedConnection_RegistersContinuityRecoveryConnection()
    {
        var recovery = new FakeAdapterContinuityRecovery();
        FakeAdapterIpcConnection? connection = null;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            connection = new FakeAdapterIpcConnection(stream) { ConnectionGeneration = 7 };
            return connection;
        }, recovery);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connection is not null && recovery.CurrentConnectionCalls.Count == 1);
        recovery.RequestRecovery(7);
        Assert.Equal([7L], recovery.RecoveryRequests);

        FakeAdapterIpcConnection acceptedConnection = connection!;
        acceptedConnection.Complete();
        await WaitUntilAsync(() => recovery.CurrentConnectionCalls.Count == 2);
        Assert.Null(recovery.CurrentConnectionCalls[1]);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection ending normally lets the listener accept a new connection, supporting reconnect.</summary>
    [Fact]
    public async Task RunAsync_AfterConnectionEnds_AcceptsAnotherConnection()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        connections.ElementAt(0).Complete();

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 2);

        connections.ElementAt(1).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that cancelling before any adapter ever connects ends the accept loop without throwing.</summary>
    [Fact]
    public async Task RunAsync_CancelledBeforeAnyConnection_EndsWithoutThrowing()
    {
        using var listener = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));
        using var cancellation = new CancellationTokenSource();

        Task runTask = listener.RunAsync(cancellation.Token);
        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that cancelling while a connection is active ends the loop without throwing, once that connection observes the cancellation.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileConnectionActive_EndsWithoutThrowing()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection failing with an unexpected exception does not end the accept loop for later connections.</summary>
    [Fact]
    public async Task RunAsync_ConnectionThrows_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        ThrowingAdapterIpcConnection? failingConnection = null;
        int firstAccept = 1;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (Interlocked.Exchange(ref firstAccept, 0) == 1)
            {
                failingConnection = new ThrowingAdapterIpcConnection(waitForRelease: true);
                return failingConnection;
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => Volatile.Read(ref firstAccept) == 0);
        await failingConnection!.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => listener.CurrentConnection is not null);
        failingConnection.ReleaseFailure();
        await WaitUntilAsync(() => listener.CurrentConnection is null);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the connection factory throwing does not end the accept loop for later connections.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryThrows_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        int factoryCalls = 0;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                throw new InvalidOperationException("Simulated factory failure.");
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => Volatile.Read(ref factoryCalls) == 1);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a factory raising ObjectDisposedException does not end the accept loop.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryObjectDisposedException_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        int firstAccept = 1;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (Interlocked.Exchange(ref firstAccept, 0) == 1)
            {
                throw new ObjectDisposedException("factory");
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => Volatile.Read(ref firstAccept) == 0);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection raising ObjectDisposedException does not end the accept loop.</summary>
    [Fact]
    public async Task RunAsync_ConnectionObjectDisposedException_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        int firstAccept = 1;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (Interlocked.Exchange(ref firstAccept, 0) == 1)
            {
                return new ThrowingAdapterIpcConnection(new ObjectDisposedException("connection"));
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => Volatile.Read(ref firstAccept) == 0);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a stream accepted for a failed factory attempt is disposed before retrying.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryThrows_DisposesAcceptedStream()
    {
        Stream? acceptedStream = null;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            acceptedStream = stream;
            throw new InvalidOperationException("Simulated factory failure.");
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => acceptedStream is not null && !acceptedStream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => acceptedStream!.ReadByte());

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that disposing the listener stops accepting but leaves its active connection running.</summary>
    [Fact]
    public async Task Dispose_WhileConnectionActive_LeavesConnectionRunningUntilItEnds()
    {
        ConcurrentQueue<FakeAdapterIpcConnection> connections = new();
        var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.NotNull(listener.CurrentConnection);

        listener.Dispose();
        Assert.NotNull(listener.CurrentConnection);

        connections.ElementAt(0).Complete();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(listener.CurrentConnection);
    }

    /// <summary>Verifies that disposing the listener while it is waiting to accept ends the loop promptly instead of spinning.</summary>
    [Fact]
    public async Task RunAsync_ListenerDisposedWhileWaitingToAccept_EndsWithoutSpinning()
    {
        var listener = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));

        Task runTask = listener.RunAsync(CancellationToken.None);
        listener.Dispose();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that constructing a second listener on a port already bound by another fails fast.</summary>
    [Fact]
    public void Constructor_PortAlreadyInUse_Throws()
    {
        using var first = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));

        Assert.Throws<SocketException>(() => new AdapterIpcListener(first.BoundPort, stream => new FakeAdapterIpcConnection(stream)));
    }

    /// <summary>Verifies that the options-based constructor also fails fast on a port already bound by another, matching the low-level constructor it delegates to.</summary>
    [Fact]
    public void Constructor_OptionsAndConnectionFactory_PortAlreadyInUse_Throws()
    {
        using var first = new AdapterIpcListener(0, stream => new FakeAdapterIpcConnection(stream));

        Assert.Throws<SocketException>(() => new AdapterIpcListener(
            new AdapterIpcOptions(first.BoundPort), new StubAdapterConnectionFactory(stream => new FakeAdapterIpcConnection(stream))));
    }

    /// <summary>Polls a condition until it becomes true, failing the test if it never does within a bounded time.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met within the expected time.");
            await Task.Delay(10);
        }
    }

    /// <summary>Connects a plain client socket to the listener's bound loopback port.</summary>
    private static async Task<Socket> ConnectClientAsync(int port)
    {
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }

    /// <summary>A minimal <see cref="IAdapterConnectionFactory"/> stand-in delegating to a supplied function and counting calls.</summary>
    private sealed class StubAdapterConnectionFactory : IAdapterConnectionFactory
    {
        /// <summary>The function this stub delegates <see cref="Create"/> to.</summary>
        private readonly Func<Stream, IAdapterIpcConnection> create;

        /// <summary>The number of times <see cref="Create"/> has been called, safe to read from another thread.</summary>
        private int createCallCount;

        /// <summary>Creates a stub delegating to an explicit function.</summary>
        /// <param name="create">The function this stub delegates <see cref="Create"/> to.</param>
        public StubAdapterConnectionFactory(Func<Stream, IAdapterIpcConnection> create)
        {
            this.create = create;
        }

        /// <summary>The number of times <see cref="Create"/> has been called.</summary>
        public int CreateCallCount => Volatile.Read(ref createCallCount);

        /// <inheritdoc/>
        public IAdapterIpcConnection Create(Stream stream)
        {
            Interlocked.Increment(ref createCallCount);
            return create(stream);
        }
    }
}

// TODO(stage4-file-extraction): Move AdapterContinuityRecoveryTests and
// PlayContextResynchronizationTriggerTests to their own files in the
// post-Stage-4 structural cleanup PR. Temporarily colocated with the tests
// for the listener their subjects were co-located with in production code.

/// <summary>Tests for <see cref="AdapterContinuityRecovery"/>.</summary>
public class AdapterContinuityRecoveryTests
{
    /// <summary>Verifies that recovery closes the active connection for the matching generation.</summary>
    [Fact]
    public void RequestRecovery_MatchingGeneration_RequestsClose()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { ConnectionGeneration = 7 };
        var recovery = new AdapterContinuityRecovery();
        recovery.SetCurrentConnection(connection);

        recovery.RequestRecovery(7);

        Assert.Equal(1, connection.RequestCloseCalls);
    }

    /// <summary>Verifies that a stale generation cannot close a newer active connection.</summary>
    [Fact]
    public void RequestRecovery_StaleGeneration_DoesNotRequestClose()
    {
        var connection = new FakeAdapterIpcConnection(new MemoryStream()) { ConnectionGeneration = 8 };
        var recovery = new AdapterContinuityRecovery();
        recovery.SetCurrentConnection(connection);

        recovery.RequestRecovery(7);

        Assert.Equal(0, connection.RequestCloseCalls);
    }
}

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
    /// its own live, independent watchdog. Uses a wide 300ms timeout with a 200ms supersession offset
    /// so the no-recovery check sits comfortably (100ms) on both sides of A's and B's deadlines, rather
    /// than the narrow ~40ms margin that was flaky on Windows/CI scheduling, and polls for B's eventual
    /// recovery instead of assuming a fixed delay proves its timer continuation has already run.
    /// </summary>
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

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        playContextTracker.NotifyTransition(PlayContextId.NewId()); // Context B: supersedes A immediately.

        // Past A's original 300ms deadline (measured from t=0), but 100ms before B's own fresh 300ms
        // deadline (measured from t=200, due at t=500ms). No capture for B ever reached the coordinator.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Empty(continuityRecovery.RecoveryRequests);

        // Past B's own independent deadline: B never completed, so it must still fire on its own
        // bound, proving the trigger armed a real watchdog for B rather than leaving it unbounded.
        await WaitUntilAsync(() => continuityRecovery.RecoveryRequests.Count > 0);
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

// TODO(stage4-file-extraction): Move CharacterCaptureHandlerTests and
// LiveCaptureSinkTests back to their own files in the post-Stage-4
// structural cleanup PR. Temporarily colocated here to hold this PR's
// changed-file count down; extraction only, no behavior change.

/// <summary>Tests Character payload decoding, area mapping, and forwarding to shared application authority.</summary>
public class CharacterCaptureHandlerTests
{
    private static readonly StateAreaId HealthArea = new(Constants.CharacterHealthStateArea);
    private static readonly StateAreaId MagickaArea = new(Constants.CharacterMagickaStateArea);
    private static readonly StateAreaId StaminaArea = new(Constants.CharacterStaminaStateArea);
    private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
    private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);

    /// <summary>Records one value sent to the shared application service.</summary>
    /// <param name="StateType">The generic publisher state type.</param>
    /// <param name="Publisher">The typed publisher supplied by the handler.</param>
    /// <param name="Mode">The canonical Snapshot or Event mode supplied by the handler.</param>
    /// <param name="AreaId">The destination state area.</param>
    /// <param name="Value">The decoded value or unavailable marker.</param>
    /// <param name="IsBaseline">Whether the handler marked the value as a resynchronization baseline.</param>
    /// <param name="Source">The exact adapter source forwarded by the handler.</param>
    /// <param name="AdapterSnapshot">The validated adapter availability snapshot.</param>
    /// <param name="PlayContextId">The validated play-context identity.</param>
    /// <param name="PlayContextGeneration">The validated play-context generation.</param>
    /// <param name="OccurredAt">The accepted capture timestamp.</param>
    private sealed record ApplyCall(
        Type StateType,
        object Publisher,
        UpdateMode Mode,
        StateAreaId AreaId,
        object? Value,
        bool IsBaseline,
        AdapterCaptureSource Source,
        AdapterAvailabilitySnapshot AdapterSnapshot,
        PlayContextId PlayContextId,
        long PlayContextGeneration,
        DateTimeOffset OccurredAt);

    /// <summary>A strict recorder for calls to shared Host authority.</summary>
    private sealed class RecordingLiveStateApplication : ILiveStateApplication
    {
        /// <summary>Every decoded value sent to the application service, in call order.</summary>
        public List<ApplyCall> ApplyCalls { get; } = [];

        /// <inheritdoc/>
        public void Apply<TState>(
            IStatePublisher<TState> publisher,
            UpdateMode mode,
            StateAreaId areaId,
            TState value,
            bool isResynchronizationBaseline,
            AdapterCaptureSource source,
            AdapterAvailabilitySnapshot adapterSnapshot,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            DateTimeOffset occurredAt) =>
            ApplyCalls.Add(new ApplyCall(
                typeof(TState),
                publisher,
                mode,
                areaId,
                value,
                isResynchronizationBaseline,
                source,
                adapterSnapshot,
                capturedPlayContextId,
                capturedPlayContextGeneration,
                occurredAt));
    }

    /// <summary>A publisher stub that fails if a handler bypasses the shared application service.</summary>
    /// <typeparam name="TState">The state value type represented by this publisher.</typeparam>
    private sealed class UnusedStatePublisher<TState> : IStatePublisher<TState>
    {
        /// <inheritdoc/>
        public bool TryGetCurrentValue(StateAreaId areaId, [MaybeNullWhen(false)] out TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public RevisionNumber CurrentRevision(StateAreaId areaId) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult Apply(
            AdapterInstanceId sourceInstanceId,
            long sourceConnectionGeneration,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult ApplyResynchronizationBaseline(
            IAdapterResynchronizationToken resynchronizationToken,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");

        /// <inheritdoc/>
        public StateApplyResult ApplyEvent(
            AdapterInstanceId sourceInstanceId,
            long sourceConnectionGeneration,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            IAdapterResynchronizationToken? resynchronizationToken,
            StateAreaId areaId,
            TState value) =>
            throw new InvalidOperationException("Character handlers must apply values through ILiveStateApplication.");
    }

    /// <summary>The handler and test doubles used to observe its application calls.</summary>
    /// <param name="Handler">The Character handler under test.</param>
    /// <param name="Application">The recorder for decoded values.</param>
    /// <param name="FloatPublisher">The strict publisher for float-valued areas.</param>
    /// <param name="LevelPublisher">The strict publisher for level.</param>
    private sealed record Fixture(
        CharacterCaptureHandler Handler,
        RecordingLiveStateApplication Application,
        IStatePublisher<float?> FloatPublisher,
        IStatePublisher<ushort?> LevelPublisher);

    /// <summary>Builds a handler with strict publishers and an application-call recorder.</summary>
    private static Fixture CreateReady()
    {
        var application = new RecordingLiveStateApplication();
        IStatePublisher<float?> floatPublisher = new UnusedStatePublisher<float?>();
        IStatePublisher<ushort?> levelPublisher = new UnusedStatePublisher<ushort?>();
        var handler = new CharacterCaptureHandler(floatPublisher, levelPublisher, application);
        return new Fixture(handler, application, floatPublisher, levelPublisher);
    }

    /// <summary>Builds validated dispatch metadata for a capture in the production catalog.</summary>
    /// <param name="captureResult">The result to pair with its catalog unit.</param>
    /// <param name="captureUnitOverride">An alternate matching unit for malformed catalog-mapping cases.</param>
    /// <returns>The result, exact catalog unit, and deterministic authority metadata.</returns>
    private static LiveCaptureContext BuildContext(
        IpcCaptureResultMessage captureResult,
        CaptureUnitDefinition? captureUnitOverride = null)
    {
        CaptureUnitDefinition captureUnit = captureUnitOverride ?? LiveStateCatalog.Default.CaptureUnits.Single(
            candidate => candidate.Source == captureResult.Source && candidate.CaptureKey == captureResult.CaptureKey);
        var source = new AdapterCaptureSource(AdapterInstanceId.NewId(), 7);
        var adapterSnapshot = new AdapterAvailabilitySnapshot(
            AdapterAvailability.Available,
            source.InstanceId,
            captureResult.CorrelationId == 0,
            source.ConnectionGeneration);
        return new LiveCaptureContext(
            captureResult,
            source,
            captureUnit,
            adapterSnapshot,
            captureResult.PlayContextId,
            11,
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Asserts that one handler application call preserves its publisher, value, mode, and capture authority.</summary>
    /// <param name="call">The recorded application call.</param>
    /// <param name="publisher">The expected typed publisher instance.</param>
    /// <param name="stateType">The expected generic state type.</param>
    /// <param name="mode">The expected publication mode.</param>
    /// <param name="areaId">The expected destination area.</param>
    /// <param name="value">The expected decoded value.</param>
    /// <param name="isBaseline">Whether the capture is expected to establish a baseline.</param>
    /// <param name="context">The validated context whose provenance must be preserved.</param>
    private static void AssertApplyCall(
        ApplyCall call,
        object publisher,
        Type stateType,
        UpdateMode mode,
        StateAreaId areaId,
        object? value,
        bool isBaseline,
        LiveCaptureContext context)
    {
        Assert.Equal(stateType, call.StateType);
        Assert.Same(publisher, call.Publisher);
        Assert.Equal(mode, call.Mode);
        Assert.Equal(areaId, call.AreaId);
        Assert.Equal(value, call.Value);
        Assert.Equal(isBaseline, call.IsBaseline);
        Assert.Equal(context.Source, call.Source);
        Assert.Equal(context.AdapterSnapshot, call.AdapterSnapshot);
        Assert.Equal(context.PlayContextId, call.PlayContextId);
        Assert.Equal(context.PlayContextGeneration, call.PlayContextGeneration);
        Assert.Equal(context.OccurredAt, call.OccurredAt);
    }

    /// <summary>Encodes a Vitals sample in health, magicka, and stamina order.</summary>
    /// <param name="health">The encoded health value.</param>
    /// <param name="magicka">The encoded magicka value.</param>
    /// <param name="stamina">The encoded stamina value.</param>
    /// <returns>The 12-byte little-endian sample.</returns>
    private static byte[] EncodeVitals(float health, float magicka, float stamina)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0, 4), health);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4, 4), magicka);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), stamina);
        return bytes;
    }

    /// <summary>Encodes one little-endian float.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The four-byte float payload.</returns>
    private static byte[] EncodeFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Encodes one little-endian 16-bit unsigned integer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The two-byte level payload.</returns>
    private static byte[] EncodeUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Verifies that the handler advertises exactly its four Character capture identities.</summary>
    [Fact]
    public void SupportedCaptures_ListsTheCharacterCaptureSet()
    {
        Fixture fixture = CreateReady();

        Assert.Equal(
            new (CaptureSourceKind Source, uint CaptureKey)[]
            {
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp),
                (CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline),
                (CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged),
            },
            fixture.Handler.SupportedCaptures);
    }

    /// <summary>Verifies that one Vitals sample maps finite values to all three areas atomically.</summary>
    [Fact]
    public void Handle_VitalsAvailable_AppliesEveryAreaInCatalogOrder()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(93.4f, 71.0f, 100.0f));
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, HealthArea, 93.4f, false, context),
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, MagickaArea, 71.0f, false, context),
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, StaminaArea, 100.0f, false, context));
    }

    /// <summary>Verifies that an unavailable Vitals sample sets all three areas unavailable.</summary>
    [Fact]
    public void Handle_VitalsUnavailable_AppliesNullToEveryArea()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        LiveCaptureContext context = BuildContext(captureResult);

        fixture.Handler.Handle(context);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, HealthArea, null, false, context),
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, MagickaArea, null, false, context),
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, StaminaArea, null, false, context));
    }

    /// <summary>Verifies that malformed, non-finite, or unavailable-with-payload Vitals captures apply no area.</summary>
    [Fact]
    public void Handle_VitalsMalformedCapture_AppliesNoArea()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Available, PlayContextId.NewId(), new byte[10]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(93.4f, float.NaN, 100.0f)),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                CaptureAvailability.Unavailable, PlayContextId.NewId(), [1]),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies that XP available and unavailable payloads map to one Snapshot value.</summary>
    [Fact]
    public void Handle_XpAvailableAndUnavailable_AppliesFiniteValueOrNull()
    {
        Fixture fixture = CreateReady();
        var available = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(50.5f));
        var unavailable = new IpcCaptureResultMessage(
            2, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Unavailable, available.PlayContextId, []);
        LiveCaptureContext availableContext = BuildContext(available);
        LiveCaptureContext unavailableContext = BuildContext(unavailable);

        fixture.Handler.Handle(availableContext);
        fixture.Handler.Handle(unavailableContext);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, XpArea, 50.5f, false, availableContext),
            call => AssertApplyCall(call, fixture.FloatPublisher, typeof(float?), UpdateMode.Snapshot, XpArea, null, false, unavailableContext));
    }

    /// <summary>Verifies that malformed or non-finite XP samples are dropped.</summary>
    [Fact]
    public void Handle_XpMalformedCapture_AppliesNothing()
    {
        IpcCaptureResultMessage[] invalidCaptures =
        [
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Available, PlayContextId.NewId(), new byte[3]),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(float.PositiveInfinity)),
            new(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                CaptureAvailability.Unavailable, PlayContextId.NewId(), EncodeFloat(50.0f)),
        ];

        foreach (IpcCaptureResultMessage captureResult in invalidCaptures)
        {
            Fixture fixture = CreateReady();
            fixture.Handler.Handle(BuildContext(captureResult));

            Assert.Empty(fixture.Application.ApplyCalls);
        }
    }

    /// <summary>Verifies ordinary Level samples, resynchronization baselines, and native Events keep distinct modes.</summary>
    [Fact]
    public void Handle_LevelSampleAndEvent_PreserveSnapshotBaselineAndEventSemantics()
    {
        Fixture fixture = CreateReady();
        var ordinarySample = new IpcCaptureResultMessage(
            5, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(11));
        var baselineSample = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(12));
        var levelEvent = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(13));
        var unavailableSample = new IpcCaptureResultMessage(
            6, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        var unavailableEvent = new IpcCaptureResultMessage(
            0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Unavailable, PlayContextId.NewId(), []);
        LiveCaptureContext ordinaryContext = BuildContext(ordinarySample);
        LiveCaptureContext baselineContext = BuildContext(baselineSample);
        LiveCaptureContext eventContext = BuildContext(levelEvent);
        LiveCaptureContext unavailableContext = BuildContext(unavailableSample);
        LiveCaptureContext unavailableEventContext = BuildContext(unavailableEvent);

        fixture.Handler.Handle(ordinaryContext);
        fixture.Handler.Handle(baselineContext);
        fixture.Handler.Handle(eventContext);
        fixture.Handler.Handle(unavailableContext);
        fixture.Handler.Handle(unavailableEventContext);

        Assert.Collection(
            fixture.Application.ApplyCalls,
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, (ushort)11, false, ordinaryContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, (ushort)12, true, baselineContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Event, LevelArea, (ushort)13, false, eventContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Snapshot, LevelArea, null, false, unavailableContext),
            call => AssertApplyCall(call, fixture.LevelPublisher, typeof(ushort?), UpdateMode.Event, LevelArea, null, false, unavailableEventContext));
    }

    /// <summary>Verifies that a malformed Level payload does not reach shared application authority.</summary>
    [Fact]
    public void Handle_LevelMalformedPayload_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(
            1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available, PlayContextId.NewId(), new byte[1]);

        fixture.Handler.Handle(BuildContext(captureResult));

        Assert.Empty(fixture.Application.ApplyCalls);
    }

    /// <summary>Verifies that mismatched state-area counts fail closed for Vitals, XP, and Level units.</summary>
    [Fact]
    public void Handle_CaptureUnitWithInvalidAreaMapping_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        CaptureUnitDefinition defaultVitals = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);
        CaptureUnitDefinition defaultXp = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp);
        CaptureUnitDefinition defaultLevel = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterLevelBaseline);
        var invalidMappings = new (IpcCaptureResultMessage CaptureResult, CaptureUnitDefinition Unit)[]
        {
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeVitals(90.0f, 80.0f, 70.0f)),
                defaultVitals with { StateAreas = [HealthArea] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(50.0f)),
                defaultXp with { StateAreas = [] }),
            (
                new IpcCaptureResultMessage(
                    1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline,
                    CaptureAvailability.Available, PlayContextId.NewId(), EncodeUInt16(12)),
                defaultLevel with { StateAreas = [] }),
        };

        foreach ((IpcCaptureResultMessage captureResult, CaptureUnitDefinition unit) in invalidMappings)
        {
            fixture.Handler.Handle(BuildContext(captureResult, unit));
        }

        Assert.Empty(fixture.Application.ApplyCalls);
    }
}

/// <summary>Tests for <see cref="LiveCaptureSink"/>.</summary>
public class LiveCaptureSinkTests
{
    private static readonly StateAreaId HealthArea = new(Constants.CharacterHealthStateArea);
    private static readonly StateAreaId MagickaArea = new(Constants.CharacterMagickaStateArea);
    private static readonly StateAreaId StaminaArea = new(Constants.CharacterStaminaStateArea);
    private static readonly StateAreaId XpArea = new(Constants.CharacterXpStateArea);
    private static readonly StateAreaId LevelArea = new(Constants.CharacterLevelStateArea);

    /// <summary>The sink and observable state collaborators used by capture-result tests.</summary>
    /// <param name="Sink">The capture sink under test.</param>
    /// <param name="Catalog">The catalog used to recognize capture results.</param>
    /// <param name="Feed">The publication feed that exposes applied values.</param>
    /// <param name="FloatPublisher">The publisher used to inspect float-area revisions.</param>
    /// <param name="AdapterTracker">The controllable adapter authority source.</param>
    /// <param name="PlayContextTracker">The active play-context source.</param>
    /// <param name="Context">The play context stamped on test captures.</param>
    /// <param name="Coordinator">The resynchronization coordinator used by the sink.</param>
    /// <param name="ContinuityRecovery">The controlled Event-recovery recorder.</param>
    /// <param name="Source">The exact adapter connection stamped on test captures.</param>
    /// <param name="Clock">The clock used for publication timestamps.</param>
    private sealed record Fixture(
        LiveCaptureSink Sink,
        LiveStateCatalog Catalog,
        StatePublicationFeed Feed,
        IStatePublisher<float?> FloatPublisher,
        FakeAdapterAvailabilityTracker AdapterTracker,
        FakePlayContextTracker PlayContextTracker,
        PlayContextId Context,
        IResynchronizationTransactionCoordinator Coordinator,
        FakeAdapterContinuityRecovery ContinuityRecovery,
        AdapterCaptureSource Source,
        FakeClock Clock);

    /// <summary>Records calls through the generic application boundary for sink interaction tests.</summary>
    private sealed class RecordingLiveStateApplication : ILiveStateApplication
    {
        /// <summary>Every applied value and its validated capture context, in call order.</summary>
        public List<(Type StateType, UpdateMode Mode, StateAreaId AreaId, object? Value, bool IsBaseline, AdapterCaptureSource Source, AdapterAvailabilitySnapshot AdapterSnapshot, PlayContextId PlayContextId, long PlayContextGeneration, DateTimeOffset OccurredAt)> ApplyCalls { get; } = [];

        /// <inheritdoc/>
        public void Apply<TState>(
            IStatePublisher<TState> publisher,
            UpdateMode mode,
            StateAreaId areaId,
            TState value,
            bool isResynchronizationBaseline,
            AdapterCaptureSource source,
            AdapterAvailabilitySnapshot adapterSnapshot,
            PlayContextId capturedPlayContextId,
            long capturedPlayContextGeneration,
            DateTimeOffset occurredAt) =>
            ApplyCalls.Add((typeof(TState), mode, areaId, value, isResynchronizationBaseline, source, adapterSnapshot,
                capturedPlayContextId, capturedPlayContextGeneration, occurredAt));
    }

    /// <summary>Records validated contexts routed by the sink.</summary>
    private sealed class RecordingLiveCaptureHandler : ILiveCaptureHandler
    {
        /// <summary>The sole source and key identity owned by this test handler.</summary>
        private readonly IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> supportedCaptures;

        /// <summary>Every validated capture context delivered to this handler.</summary>
        public List<LiveCaptureContext> Contexts { get; } = [];

        /// <summary>Creates a handler that claims one capture identity.</summary>
        /// <param name="source">The capture source namespace to claim.</param>
        /// <param name="captureKey">The capture key to claim.</param>
        public RecordingLiveCaptureHandler(CaptureSourceKind source, uint captureKey)
        {
            supportedCaptures = [(source, captureKey)];
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<(CaptureSourceKind Source, uint CaptureKey)> SupportedCaptures => supportedCaptures;

        /// <inheritdoc/>
        public void Handle(LiveCaptureContext context) => Contexts.Add(context);
    }

    /// <summary>
    /// Builds a sink wired exactly like production composition, but with controllable adapter/play-context
    /// trackers and a real StatePublisher/StatePublicationFeed pair so applied values are actually
    /// observable.
    /// </summary>
    /// <param name="coordinatorOverride">
    /// The resynchronization transaction coordinator to wire in, or <see langword="null"/> for the
    /// real <see cref="ResynchronizationTransactionCoordinator"/> -- override with a
    /// <see cref="FakeResynchronizationTransactionCoordinator"/> when a test needs to observe whether
    /// this sink ever claimed a token or recorded an area accepted, rather than only the resulting
    /// state.
    /// </param>
    /// <param name="applicationOverride">The application service to pass through the Character handler, or <see langword="null"/> for the real implementation.</param>
    /// <param name="clockOverride">The clock to use for accepted dispatch timestamps, or <see langword="null"/> for a new fake clock.</param>
    /// <param name="catalogOverride">The capture catalog to recognize, or <see langword="null"/> for the production catalog.</param>
    /// <param name="handlerOverrides">The explicit capture handlers to register, or <see langword="null"/> for the production Character handler.</param>
    private static Fixture CreateReady(
        IResynchronizationTransactionCoordinator? coordinatorOverride = null,
        ILiveStateApplication? applicationOverride = null,
        FakeClock? clockOverride = null,
        LiveStateCatalog? catalogOverride = null,
        IReadOnlyCollection<ILiveCaptureHandler>? handlerOverrides = null)
    {
        LiveStateCatalog catalog = catalogOverride ?? LiveStateCatalog.Default;
        var playContextTracker = new FakePlayContextTracker();
        PlayContextId context = PlayContextId.NewId();
        playContextTracker.NotifyTransition(context);
        var adapterTracker = new FakeAdapterAvailabilityTracker { Current = AdapterAvailability.Available };
        var registeredAreas = new RegisteredStateAreaPolicy();
        foreach (StateAreaDefinition area in catalog.StateAreas)
        {
            registeredAreas.TryRegister(area.Id);
        }

        var feed = new StatePublicationFeed(adapterTracker, playContextTracker, registeredAreas);
        var revisionTracker = new RevisionTracker();
        var floatPublisher = new StatePublisher<float?>(revisionTracker, playContextTracker, adapterTracker);
        var levelPublisher = new StatePublisher<ushort?>(revisionTracker, playContextTracker, adapterTracker);
        var continuityRecovery = new FakeAdapterContinuityRecovery();
        IResynchronizationTransactionCoordinator coordinator = coordinatorOverride
            ?? new ResynchronizationTransactionCoordinator(catalog, adapterTracker, continuityRecovery, TimeSpan.FromSeconds(30));
        FakeClock clock = clockOverride ?? new FakeClock();
        ILiveStateApplication application = applicationOverride ?? new LiveStateApplication(coordinator, continuityRecovery, feed);
        IReadOnlyCollection<ILiveCaptureHandler> handlers = handlerOverrides
            ?? new ILiveCaptureHandler[] { new CharacterCaptureHandler(floatPublisher, levelPublisher, application) };
        var sink = new LiveCaptureSink(catalog, handlers, adapterTracker, playContextTracker, clock);
        var source = new AdapterCaptureSource(adapterTracker.CurrentInstanceId!.Value, adapterTracker.CurrentConnectionGeneration);
        return new Fixture(sink, catalog, feed, floatPublisher, adapterTracker, playContextTracker, context, coordinator, continuityRecovery, source, clock);
    }

    /// <summary>Builds a one-unit catalog for generic handler-routing tests.</summary>
    /// <param name="source">The source namespace recognized by the catalog.</param>
    /// <param name="captureKey">The capture key recognized by the catalog.</param>
    /// <param name="areaId">The state area declared for the capture unit.</param>
    /// <param name="mode">The canonical publication mode of the area.</param>
    /// <returns>A catalog containing only the requested capture and state area.</returns>
    private static LiveStateCatalog BuildSingleCaptureCatalog(
        CaptureSourceKind source,
        uint captureKey,
        StateAreaId areaId,
        UpdateMode mode)
    {
        SynchronizationRole role = source == CaptureSourceKind.Sample
            ? SynchronizationRole.BaselineSample
            : SynchronizationRole.PersistentEvent;
        return new LiveStateCatalog(
            [new CaptureUnitDefinition(source, captureKey, RateClass: null, SynchronizationRole: role, StateAreas: [areaId])],
            [new StateAreaDefinition(areaId, mode)]);
    }

    /// <summary>Encodes a Vitals sample in health, magicka, and stamina order.</summary>
    /// <param name="health">The health value to encode.</param>
    /// <param name="magicka">The magicka value to encode.</param>
    /// <param name="stamina">The stamina value to encode.</param>
    /// <returns>The 12-byte little-endian Vitals payload.</returns>
    private static byte[] EncodeVitals(float health, float magicka, float stamina)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0, 4), health);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4, 4), magicka);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8, 4), stamina);
        return bytes;
    }

    /// <summary>Encodes one little-endian float.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The four-byte payload.</returns>
    private static byte[] EncodeFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Encodes one little-endian unsigned 16-bit integer.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The two-byte payload.</returns>
    private static byte[] EncodeUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    /// <summary>Reads the nullable float <c>value</c> from a state publication.</summary>
    /// <param name="data">The publication payload.</param>
    /// <returns>The decoded float value, or <see langword="null"/>.</returns>
    private static float? ReadValue(JsonElement data) =>
        data.GetProperty("value").ValueKind == JsonValueKind.Null ? null : data.GetProperty("value").GetSingle();

    /// <summary>Verifies that one coherent vitals capture applies all three resource areas independently.</summary>
    [Fact]
    public void ApplyCaptureResult_Vitals_AppliesAllThreeAreasIndependently()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, 71.0f, 100.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.True(fixture.Feed.TryGetSnapshot(HealthArea, out StateSnapshotPublication? health));
        Assert.Equal(93.4f, ReadValue(health!.Data));
        Assert.True(fixture.Feed.TryGetSnapshot(MagickaArea, out StateSnapshotPublication? magicka));
        Assert.Equal(71.0f, ReadValue(magicka!.Data));
        Assert.True(fixture.Feed.TryGetSnapshot(StaminaArea, out StateSnapshotPublication? stamina));
        Assert.Equal(100.0f, ReadValue(stamina!.Data));
    }

    /// <summary>Verifies that a coherent Vitals capture fans out through the shared application with its exact context.</summary>
    [Fact]
    public void ApplyCaptureResult_Vitals_UsesSharedApplicationForEachArea()
    {
        var application = new RecordingLiveStateApplication();
        Fixture fixture = CreateReady(applicationOverride: application);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, 71.0f, 100.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Collection(
            application.ApplyCalls,
            call => Assert.Equal((typeof(float?), UpdateMode.Snapshot, HealthArea, 93.4f), (call.StateType, call.Mode, call.AreaId, (float)call.Value!)),
            call => Assert.Equal((typeof(float?), UpdateMode.Snapshot, MagickaArea, 71.0f), (call.StateType, call.Mode, call.AreaId, (float)call.Value!)),
            call => Assert.Equal((typeof(float?), UpdateMode.Snapshot, StaminaArea, 100.0f), (call.StateType, call.Mode, call.AreaId, (float)call.Value!)));
        Assert.All(application.ApplyCalls, call =>
        {
            Assert.False(call.IsBaseline);
            Assert.Equal(fixture.Source, call.Source);
            Assert.Equal(fixture.AdapterTracker.GetSnapshot(), call.AdapterSnapshot);
            Assert.Equal(fixture.Context, call.PlayContextId);
            Assert.Equal(fixture.PlayContextTracker.TransitionGeneration, call.PlayContextGeneration);
            Assert.Equal(fixture.Clock.UtcNow, call.OccurredAt);
        });
    }

    /// <summary>Verifies that Level baseline Samples and native Events retain separate application modes.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelSampleAndEvent_KeepTheirApplicationSemantics()
    {
        var application = new RecordingLiveStateApplication();
        Fixture fixture = CreateReady(applicationOverride: application);
        fixture.Sink.ApplyCaptureResult(
            new IpcCaptureResultMessage(5, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(11)),
            fixture.Source);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fixture.Sink.ApplyCaptureResult(
            new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12)),
            fixture.Source);
        fixture.Sink.ApplyCaptureResult(
            new IpcCaptureResultMessage(0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, fixture.Context, EncodeUInt16(13)),
            fixture.Source);

        Assert.Equal(
            [
                (UpdateMode.Snapshot, false, (object?)(ushort)11),
                (UpdateMode.Snapshot, true, (object?)(ushort)12),
                (UpdateMode.Event, false, (object?)(ushort)13),
            ],
            application.ApplyCalls.Select(call => (call.Mode, call.IsBaseline, call.Value)));
        Assert.All(application.ApplyCalls, call => Assert.Equal(LevelArea, call.AreaId));
    }

    /// <summary>Verifies that only the area whose value actually changed advances its revision, matching the roadmap's "only Health revision advances" acceptance scenario.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsSecondCaptureChangesOnlyHealth_OnlyHealthRevisionAdvances()
    {
        Fixture fixture = CreateReady();
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, 71.0f, 100.0f)), fixture.Source);
        RevisionNumber healthRevisionBefore = fixture.FloatPublisher.CurrentRevision(HealthArea);
        RevisionNumber magickaRevisionBefore = fixture.FloatPublisher.CurrentRevision(MagickaArea);
        RevisionNumber staminaRevisionBefore = fixture.FloatPublisher.CurrentRevision(StaminaArea);

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(2, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(80.0f, 71.0f, 100.0f)), fixture.Source);

        Assert.NotEqual(healthRevisionBefore, fixture.FloatPublisher.CurrentRevision(HealthArea));
        Assert.Equal(magickaRevisionBefore, fixture.FloatPublisher.CurrentRevision(MagickaArea));
        Assert.Equal(staminaRevisionBefore, fixture.FloatPublisher.CurrentRevision(StaminaArea));
        Assert.True(fixture.Feed.TryGetSnapshot(HealthArea, out StateSnapshotPublication? health));
        Assert.Equal(80.0f, ReadValue(health!.Data));
    }

    /// <summary>Verifies that an unavailable capture applies an explicit null value, never a fabricated zero.</summary>
    [Fact]
    public void ApplyCaptureResult_XpUnavailable_AppliesExplicitNullNotZero()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Unavailable, fixture.Context, []);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Null(ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a level capture publishes through EventOccurred, not SnapshotChanged, matching its Event update mode.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEvent_PublishesThroughEventOccurredNotSnapshotChanged()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        StateEventPublication? raisedEvent = null;
        bool snapshotChangedRaised = false;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;
        fixture.Feed.SnapshotChanged += _ => snapshotChangedRaised = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.Equal(LevelArea, raisedEvent!.StateArea);
        Assert.Equal(RevisionNumber.Initial, raisedEvent.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvent.Revision);
        Assert.False(snapshotChangedRaised);
        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>
    /// Verifies that a level-changed Event arriving during resynchronization may be authorized and
    /// applied without being recorded as an accepted baseline area.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronization_DoesNotRecordAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Event, (uint)CharacterEventKey.CharacterLevelChanged, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotEmpty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that a level-changed Event during resynchronization remains an Event publication with the expected revisions.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronization_PublishesEventNotSnapshot()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        StateEventPublication? raisedEvent = null;
        bool snapshotChangedRaised = false;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;
        fixture.Feed.SnapshotChanged += _ => snapshotChangedRaised = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.Equal(LevelArea, raisedEvent!.StateArea);
        Assert.Equal(RevisionNumber.Initial, raisedEvent.BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvent.Revision);
        Assert.Equal(11, raisedEvent.Data.GetProperty("value").GetUInt16());
        Assert.False(snapshotChangedRaised);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that successive resynchronization Events chain their base and resulting revisions.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventsWhileNeedsResynchronization_ChainRevisions()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        var raisedEvents = new List<StateEventPublication>();
        fixture.Feed.EventOccurred += publication => raisedEvents.Add(publication);

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(12)), fixture.Source);

        Assert.Equal(2, raisedEvents.Count);
        Assert.Equal(RevisionNumber.Initial, raisedEvents[0].BaseRevision);
        Assert.Equal(RevisionNumber.Initial.Next(), raisedEvents[0].Revision);
        Assert.Equal(raisedEvents[0].Revision, raisedEvents[1].BaseRevision);
        Assert.Equal(raisedEvents[0].Revision.Next(), raisedEvents[1].Revision);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that an Event without current resynchronization authorization requests controlled recovery.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventWhileNeedsResynchronizationWithoutToken_RequestsRecovery()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        bool eventRaised = false;
        fixture.Feed.EventOccurred += _ => eventRaised = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotEmpty(fakeCoordinator.AcquireTokenCalls);
        Assert.False(eventRaised);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
        Assert.Equal([fixture.Source.ConnectionGeneration], fixture.ContinuityRecovery.RecoveryRequests);
    }

    /// <summary>Verifies that a resynchronization finishing before publisher apply lets the Event use ordinary authority.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEvent_ResynchronizationFinishesBeforeApply_PublishesWithoutRecovery()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
        fixture.AdapterTracker.OnGetSnapshot = callNumber =>
        {
            if (callNumber == 2)
            {
                fixture.AdapterTracker.NeedsResynchronization = false;
            }
        };
        StateEventPublication? raisedEvent = null;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.Empty(fixture.ContinuityRecovery.RecoveryRequests);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that a resynchronization beginning before publisher apply is retried with fresh authority.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEvent_ResynchronizationBeginsBeforeApply_RetriesWithResyncAuthority()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.OnGetSnapshot = callNumber =>
        {
            if (callNumber == 2)
            {
                fixture.AdapterTracker.RearmResynchronizationForPlayContextTransition();
                fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();
            }
        };
        StateEventPublication? raisedEvent = null;
        fixture.Feed.EventOccurred += publication => raisedEvent = publication;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.NotNull(raisedEvent);
        Assert.NotEmpty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fixture.ContinuityRecovery.RecoveryRequests);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that the actual level baseline sample records the Level area after an Event arrived first.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventBeforeLevelBaseline_OnlyBaselineRecordsAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        fakeCoordinator.AcquireTokenResult = fixture.AdapterTracker.TryClaimResynchronizationToken();

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterLevelBaseline,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);

        Assert.Single(fakeCoordinator.RecordAreaAcceptedCalls);
        Assert.Equal(LevelArea, fakeCoordinator.RecordAreaAcceptedCalls[0].AreaId);
    }

    /// <summary>Verifies that an Event cannot complete a transaction when the Level baseline remains outstanding.</summary>
    [Fact]
    public void ApplyCaptureResult_LevelChangedEventBeforeBaseline_CannotCompleteResynchronization()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;

        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterVitals,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeVitals(90.0f, 80.0f, 70.0f)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Sample,
            (uint)CharacterSampleToken.CharacterXp,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeFloat(50.0f)), fixture.Source);
        fixture.Sink.ApplyCaptureResult(new IpcCaptureResultMessage(
            0,
            CaptureSourceKind.Event,
            (uint)CharacterEventKey.CharacterLevelChanged,
            CaptureAvailability.Available,
            fixture.Context,
            EncodeUInt16(11)), fixture.Source);
        fixture.Coordinator.RecordAdapterPlanAccepted(true, fixture.Source.InstanceId, fixture.Source.ConnectionGeneration, fixture.Context, fixture.PlayContextTracker.TransitionGeneration);

        Assert.True(fixture.AdapterTracker.NeedsResynchronization);
    }

    /// <summary>
    /// Verifies that a level baseline sample -- unlike the level-changed event above -- publishes
    /// through SnapshotChanged, not EventOccurred: the baseline establishes the current authoritative
    /// level as replaceable state, not an ordered change, even though it shares the same decode and
    /// the same area as the level-changed event.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelBaselineSample_PublishesThroughSnapshotChangedNotEventOccurred()
    {
        Fixture fixture = CreateReady();
        // A baseline sample is only ever sent, and only ever a valid apply, while the adapter genuinely
        // needs resynchronization -- matching real production sequencing.
        fixture.AdapterTracker.NeedsResynchronization = true;
        StateSnapshotPublication? raisedSnapshot = null;
        bool eventOccurredRaised = false;
        fixture.Feed.SnapshotChanged += publication => raisedSnapshot = publication;
        fixture.Feed.EventOccurred += _ => eventOccurredRaised = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raisedSnapshot);
        Assert.Equal(LevelArea, raisedSnapshot!.StateArea);
        Assert.False(eventOccurredRaised);
    }

    /// <summary>Verifies that an unrecognized capture key is silently dropped rather than applied.</summary>
    [Fact]
    public void ApplyCaptureResult_UnknownCaptureKey_DoesNothing()
    {
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp);
        Fixture fixture = CreateReady(handlerOverrides: [handler]);
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, CaptureKey: 999, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Empty(handler.Contexts);
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a catalog-recognized capture without a handler fails closed without state or publication effects.</summary>
    [Fact]
    public void ApplyCaptureResult_RecognizedCaptureWithoutHandler_DropsWithoutMutationOrPublication()
    {
        LiveStateCatalog catalog = BuildSingleCaptureCatalog(CaptureSourceKind.Sample, 999, XpArea, UpdateMode.Snapshot);
        var application = new RecordingLiveStateApplication();
        Fixture fixture = CreateReady(
            applicationOverride: application,
            catalogOverride: catalog,
            handlerOverrides: []);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, 999, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));
        int snapshotCount = 0;
        int eventCount = 0;
        fixture.Feed.SnapshotChanged += _ => snapshotCount++;
        fixture.Feed.EventOccurred += _ => eventCount++;

        Exception? exception = Record.Exception(() => fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source));

        Assert.Null(exception);
        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        Assert.Empty(application.ApplyCalls);
        Assert.Equal(0, snapshotCount);
        Assert.Equal(0, eventCount);
    }

    /// <summary>Verifies that a capture identity owned by a registered handler is routed with its exact validated context.</summary>
    [Fact]
    public void ApplyCaptureResult_FutureCaptureHandler_ReceivesExactCaptureUnitAndContext()
    {
        LiveStateCatalog catalog = BuildSingleCaptureCatalog(CaptureSourceKind.Sample, 999, XpArea, UpdateMode.Snapshot);
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, 999);
        Fixture fixture = CreateReady(catalogOverride: catalog, handlerOverrides: [handler]);
        CaptureUnitDefinition unit = fixture.Catalog.CaptureUnits[0];
        var captureResult = new IpcCaptureResultMessage(7, CaptureSourceKind.Sample, 999, CaptureAvailability.Available, fixture.Context, EncodeFloat(17.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        LiveCaptureContext context = Assert.Single(handler.Contexts);
        Assert.Same(captureResult, context.CaptureResult);
        Assert.Equal(fixture.Source, context.Source);
        Assert.Same(unit, context.CaptureUnit);
        Assert.Equal(fixture.AdapterTracker.GetSnapshot(), context.AdapterSnapshot);
        Assert.Equal(fixture.Context, context.PlayContextId);
        Assert.Equal(fixture.PlayContextTracker.TransitionGeneration, context.PlayContextGeneration);
        Assert.Equal(fixture.Clock.UtcNow, context.OccurredAt);
    }

    /// <summary>Verifies that duplicate handler ownership fails at sink construction.</summary>
    [Fact]
    public void LiveCaptureSink_DuplicateHandlerIdentity_ThrowsClearCompositionFailure()
    {
        Fixture fixture = CreateReady();
        var first = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, 999);
        var second = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, 999);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new LiveCaptureSink(
            fixture.Catalog,
            [first, second],
            fixture.AdapterTracker,
            fixture.PlayContextTracker,
            fixture.Clock));

        Assert.Contains("Sample", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    /// <summary>
    /// Verifies that CaptureResultApplied is raised with the arriving result and the passed-in
    /// source's own connection generation -- not a value rediscovered from the adapter availability
    /// tracker's own current state, which this test deliberately sets to a different value to prove
    /// the passed source, not the tracker, is authoritative.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_RaisesCaptureResultAppliedWithSourcesConnectionGeneration()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.CurrentConnectionGeneration = 999;
        var captureResult = new IpcCaptureResultMessage(3, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));
        var source = new AdapterCaptureSource(fixture.AdapterTracker.CurrentInstanceId!.Value, 7);
        List<(IpcCaptureResultMessage CaptureResult, long ConnectionGeneration)> raised = [];
        fixture.Sink.CaptureResultApplied += (result, generation) => raised.Add((result, generation));

        fixture.Sink.ApplyCaptureResult(captureResult, source);

        Assert.Equal([(captureResult, 7L)], raised);
    }

    /// <summary>Verifies that a capture from the currently available adapter connection is accepted.</summary>
    [Fact]
    public void ApplyCaptureResult_CurrentExactSource_AppliesNormally()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.CurrentConnectionGeneration = 1;
        AdapterCaptureSource currentSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, currentSource);

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(50.0f, ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a capture from an old connection generation is dropped without state or publication effects.</summary>
    [Fact]
    public void ApplyCaptureResult_OldConnectionGeneration_DropsWithoutMutation()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp);
        Fixture fixture = CreateReady(coordinatorOverride: fakeCoordinator, handlerOverrides: [handler]);
        fixture.AdapterTracker.CurrentConnectionGeneration = 2;
        AdapterCaptureSource staleSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));
        int snapshotPublicationCount = 0;
        int eventPublicationCount = 0;
        fixture.Feed.SnapshotChanged += _ => snapshotPublicationCount++;
        fixture.Feed.EventOccurred += _ => eventPublicationCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, staleSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        Assert.Equal(0, snapshotPublicationCount);
        Assert.Equal(0, eventPublicationCount);
        Assert.Empty(handler.Contexts);
        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that a capture from an old adapter instance is dropped without state or publication effects.</summary>
    [Fact]
    public void ApplyCaptureResult_OldAdapterInstance_DropsWithoutMutation()
    {
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp);
        Fixture fixture = CreateReady(handlerOverrides: [handler]);
        AdapterCaptureSource staleSource = new(AdapterInstanceId.NewId(), fixture.Source.ConnectionGeneration);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));
        int snapshotPublicationCount = 0;
        fixture.Feed.SnapshotChanged += _ => snapshotPublicationCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, staleSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        Assert.Equal(0, snapshotPublicationCount);
        Assert.Empty(handler.Contexts);
    }

    /// <summary>Verifies that a delayed generation-one result is dropped after the same adapter reconnects as generation two.</summary>
    [Fact]
    public void ApplyCaptureResult_DelayedGenerationOneAfterReconnect_DropsWithoutMutation()
    {
        Fixture fixture = CreateReady();
        AdapterInstanceId instanceId = fixture.Source.InstanceId;
        fixture.AdapterTracker.CommitConnected(instanceId, 2);
        AdapterCaptureSource delayedSource = new(instanceId, 1);
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, delayedSource);

        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a delayed baseline cannot claim resynchronization progress for a newer connection generation.</summary>
    [Fact]
    public void ApplyCaptureResult_DelayedBaselineFromOldGeneration_DoesNotRecordNewGenerationProgress()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.CurrentConnectionGeneration = 2;
        fixture.AdapterTracker.NeedsResynchronization = true;
        AdapterCaptureSource delayedSource = new(fixture.Source.InstanceId, 1);
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, delayedSource);

        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
        Assert.Equal(RevisionNumber.Initial, fixture.FloatPublisher.CurrentRevision(XpArea));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>
    /// Verifies that CaptureResultApplied still fires for an unrecognized capture key -- before this
    /// sink's own recognition check -- so a listener learns a reply arrived even when this sink itself
    /// goes on to drop it.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_UnknownCaptureKey_StillRaisesCaptureResultApplied()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, CaptureKey: 999, CaptureAvailability.Available, fixture.Context, EncodeFloat(1.0f));
        int raisedCount = 0;
        fixture.Sink.CaptureResultApplied += (_, _) => raisedCount++;

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Equal(1, raisedCount);
    }

    /// <summary>Verifies that a capture stamped with a play context other than the current one is dropped rather than misattributed.</summary>
    [Fact]
    public void ApplyCaptureResult_StalePlayContext_DoesNothing()
    {
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp);
        Fixture fixture = CreateReady(handlerOverrides: [handler]);
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, PlayContextId.NewId(), EncodeFloat(1.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Empty(handler.Contexts);
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a vitals capture with a non-finite value applies none of the three areas, not just the invalid one.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsContainsNaN_AppliesNoneOfTheThreeAreas()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, EncodeVitals(93.4f, float.NaN, 100.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(HealthArea, out _));
        Assert.False(fixture.Feed.TryGetSnapshot(MagickaArea, out _));
        Assert.False(fixture.Feed.TryGetSnapshot(StaminaArea, out _));
    }

    /// <summary>Verifies that a vitals payload of the wrong length applies nothing.</summary>
    [Fact]
    public void ApplyCaptureResult_VitalsMalformedLength_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterVitals, CaptureAvailability.Available, fixture.Context, new byte[10]);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(HealthArea, out _));
    }

    /// <summary>
    /// Verifies that while the adapter needs resynchronization, a capture is still applied -- routed
    /// through the baseline path instead of the ordinary one -- and its value is genuinely stored
    /// (visible once resynchronization completes), even though StatePublicationFeed.TryGetSnapshot
    /// withholds it as a pull read for as long as resynchronization stays outstanding.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_WhileNeedsResynchronization_StillAppliesThroughBaselinePath()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
        fixture.AdapterTracker.NeedsResynchronization = false;
        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(50.0f, ReadValue(xp!.Data));
    }

    /// <summary>
    /// Verifies that an ordinary, scheduler-issued sample reply arriving while the adapter needs
    /// resynchronization never satisfies that resynchronization: capture purpose is decided from the
    /// capture's own correlation id -- zero only for a baseline or a native event, nonzero only for an
    /// ordinary ReadSample reply -- never inferred from the coincidence of a resynchronization
    /// happening to be outstanding when the reply arrives. Proven directly against the
    /// resynchronization coordinator rather than only the resulting state, since an ordinary apply's
    /// own rejection (proven separately by every other "while NeedsResynchronization" test in this
    /// file) would look identical from the outside whether or not it had also, incorrectly, claimed a
    /// baseline token along the way.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_OrdinarySampleWhileNeedsResynchronization_NeverRecordsAreaAccepted()
    {
        var fakeCoordinator = new FakeResynchronizationTransactionCoordinator();
        Fixture fixture = CreateReady(fakeCoordinator);
        fixture.AdapterTracker.NeedsResynchronization = true;
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Empty(fakeCoordinator.AcquireTokenCalls);
        Assert.Empty(fakeCoordinator.RecordAreaAcceptedCalls);
    }

    /// <summary>Verifies that applying the same value twice publishes only once, since the second apply does not change anything.</summary>
    [Fact]
    public void ApplyCaptureResult_SameValueTwice_PublishesOnlyOnce()
    {
        Fixture fixture = CreateReady();
        var raised = new List<StateSnapshotPublication>();
        fixture.Feed.SnapshotChanged += publication =>
        {
            if (publication.StateArea == XpArea)
            {
                raised.Add(publication);
            }
        };
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var captureResult = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);
        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Single(raised);
    }

    /// <summary>Verifies that a capture is rejected while the adapter is unavailable, since StatePublisher.Apply itself gates on it.</summary>
    [Fact]
    public void ApplyCaptureResult_AdapterUnavailable_DoesNothing()
    {
        var handler = new RecordingLiveCaptureHandler(CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp);
        Fixture fixture = CreateReady(handlerOverrides: [handler]);
        fixture.AdapterTracker.Current = AdapterAvailability.Unavailable;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.Empty(handler.Contexts);
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that an experience payload of the wrong length applies nothing, symmetric with the vitals case.</summary>
    [Fact]
    public void ApplyCaptureResult_XpMalformedLength_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, new byte[3]);

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that a non-finite experience value applies nothing, symmetric with the vitals case.</summary>
    [Fact]
    public void ApplyCaptureResult_XpNaN_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(float.NaN));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>Verifies that an unavailable experience capture carrying a nonempty payload (a malformed combination the wire codec should never actually produce) is defensively rejected rather than misread.</summary>
    [Fact]
    public void ApplyCaptureResult_XpUnavailableWithNonemptyPayload_AppliesNothing()
    {
        Fixture fixture = CreateReady();
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Unavailable, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }

    /// <summary>
    /// Verifies that the level baseline sample token -- not just the level-changed event -- decodes
    /// and applies to the same level area, including while resynchronizing, proving the generic
    /// apply-and-publish routing works for the ushort-valued publisher too. The applied value is
    /// genuinely stored (visible once resynchronization completes), even though
    /// StatePublicationFeed.TryGetSnapshot withholds it as a pull read until then.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_LevelBaselineSampleWhileNeedsResynchronization_AppliesToLevelAreaThroughBaselinePath()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterLevelBaseline, CaptureAvailability.Available, fixture.Context, EncodeUInt16(12));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.False(fixture.Feed.TryGetSnapshot(LevelArea, out _));
        fixture.AdapterTracker.NeedsResynchronization = false;
        Assert.True(fixture.Feed.TryGetSnapshot(LevelArea, out StateSnapshotPublication? level));
        Assert.Equal(12, level!.Data.GetProperty("value").GetUInt16());
    }

    /// <summary>
    /// Verifies that an accepted resynchronization baseline whose value genuinely changed still
    /// publishes through SnapshotChanged, proving the unchanged-baseline handling below did not fold
    /// this case into a silent EstablishBaseline-only path.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationBaselineChanged_StillRaisesSnapshotChanged()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        StateSnapshotPublication? raised = null;
        fixture.Feed.SnapshotChanged += publication =>
        {
            if (publication.StateArea == XpArea)
            {
                raised = publication;
            }
        };
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source);

        Assert.NotNull(raised);
        Assert.Equal(50.0f, ReadValue(raised!.Data));
    }

    /// <summary>
    /// Verifies that an accepted resynchronization baseline whose value is unchanged from what was
    /// already stored still restores the publication feed's pull-read cache after a continuity loss
    /// cleared it -- not only the publisher's own authoritative store, which a same-value baseline
    /// already updates correctly. Without this, a client requesting a snapshot right after
    /// resynchronization completes would be told no value is available merely because nothing about
    /// it changed.
    /// </summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationBaselineUnchangedAfterDisconnect_RestoresFeedSnapshot()
    {
        Fixture fixture = CreateReady();
        // A nonzero correlation id: an ordinary, scheduler-issued ReadSample reply, never a
        // resynchronization baseline (which always carries zero).
        var initialCapture = new IpcCaptureResultMessage(1, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(100.0f));
        fixture.Sink.ApplyCaptureResult(initialCapture, fixture.Source);
        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out _));

        // A continuity loss unconditionally clears the feed's own pull-read cache, per
        // StatePublicationFeed's documented defense-in-depth clearing.
        fixture.AdapterTracker.PublishTransition(new AdapterAvailabilityTransition(
            AdapterAvailability.Available, AdapterAvailability.Unavailable, fixture.AdapterTracker.CurrentInstanceId, fixture.AdapterTracker.CurrentConnectionGeneration));
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));

        // Reconnect and resynchronize the exact same value.
        fixture.AdapterTracker.Current = AdapterAvailability.Available;
        fixture.AdapterTracker.NeedsResynchronization = true;
        var resyncCapture = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(100.0f));
        fixture.Sink.ApplyCaptureResult(resyncCapture, fixture.Source);
        fixture.AdapterTracker.NeedsResynchronization = false;

        Assert.True(fixture.Feed.TryGetSnapshot(XpArea, out StateSnapshotPublication? xp));
        Assert.Equal(100.0f, ReadValue(xp!.Data));
    }

    /// <summary>Verifies that a resynchronization token already claimed by another caller is a silent drop, not a crash.</summary>
    [Fact]
    public void ApplyCaptureResult_ResynchronizationTokenAlreadyClaimed_DoesNothingAndDoesNotThrow()
    {
        Fixture fixture = CreateReady();
        fixture.AdapterTracker.NeedsResynchronization = true;
        fixture.AdapterTracker.TryClaimResynchronizationToken(); // claimed by someone else first
        var captureResult = new IpcCaptureResultMessage(0, CaptureSourceKind.Sample, (uint)CharacterSampleToken.CharacterXp, CaptureAvailability.Available, fixture.Context, EncodeFloat(50.0f));

        Exception? escaped = Record.Exception(() => fixture.Sink.ApplyCaptureResult(captureResult, fixture.Source));

        Assert.Null(escaped);
        Assert.False(fixture.Feed.TryGetSnapshot(XpArea, out _));
    }
}
