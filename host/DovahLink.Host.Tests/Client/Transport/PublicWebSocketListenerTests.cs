using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicWebSocketListener"/>.</summary>
public class PublicWebSocketListenerTests
{
    /// <summary>Verifies that binding with port zero assigns a nonzero port shared by both loopback addresses.</summary>
    [Fact]
    public async Task Constructor_PortZero_BindsASharedPortReachableViaBothLoopbackAddresses()
    {
        using var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        Assert.NotEqual(0, listener.BoundPort);
        using Socket ipv4Client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        using Socket ipv6Client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
    }

    /// <summary>Verifies that a fresh listener reports no active connection.</summary>
    [Fact]
    public void CurrentConnection_BeforeAnyAccept_IsNull()
    {
        using var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        Assert.Empty(listener.CurrentConnections);
    }

    /// <summary>Verifies that accepting an IPv4 connection reports it as current while it runs, and clears it once it ends.</summary>
    [Fact]
    public async Task RunAsync_AcceptedIPv4Connection_ReportsCurrentWhileRunningThenClears()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.Single(listener.CurrentConnections);

        connections.ElementAt(0).Complete();
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that accepting an IPv6 connection reports it as current while it runs, and clears it once it ends.</summary>
    [Fact]
    public async Task RunAsync_AcceptedIPv6Connection_ReportsCurrentWhileRunningThenClears()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.Single(listener.CurrentConnections);

        connections.ElementAt(0).Complete();
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection ending normally lets the listener accept a new connection, supporting reconnect.</summary>
    [Fact]
    public async Task RunAsync_AfterConnectionEnds_AcceptsAnotherConnection()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);
        connections.ElementAt(0).Complete();
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 2);

        connections.ElementAt(1).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies, using real <see cref="PublicWebSocketConnection"/> instances (not the fake), that the
    /// admission slot only becomes reusable after the first connection's mandatory
    /// <see cref="IPublicWebSocketMessageHandler.HandleConnectionEnded"/> invalidation has already run
    /// -- the transport-level ordering proof the fake-connection reconnect test above cannot exercise,
    /// since the listener has no visibility into a fake connection's handler calls.
    /// </summary>
    [Fact]
    public async Task RunAsync_AfterRealConnectionEnds_SecondConnectionIsAdmittedOnlyAfterFirstInvalidated()
    {
        var handler = new FakePublicWebSocketMessageHandler();
        using var listener = new PublicWebSocketListener(0, stream =>
            Fixtures.BuildPublicWebSocketConnection(stream, handler));
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using var firstClient = new ClientWebSocket();
        await firstClient.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.BoundPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => listener.CurrentConnections.Count > 0);

        await firstClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        Assert.Equal(1, handler.ConnectionEndedCalls);

        using var secondClient = new ClientWebSocket();
        await secondClient.ConnectAsync(new Uri($"ws://127.0.0.1:{listener.BoundPort}/"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => listener.CurrentConnections.Count > 0);

        await secondClient.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that, with a bound of one connection, a second attempt on the same address family while the slot is occupied is rejected without replacing the active connection.</summary>
    [Fact]
    public async Task RunAsync_SecondConnectionSameFamilyWhileSlotOccupied_IsRejected()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        }, maxConcurrentConnections: 1);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => IsDisconnected(secondClient));

        Assert.Single(connections);
        Assert.Single(listener.CurrentConnections);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that, with a bound of one connection, a second attempt on the other address family while the slot is occupied is also rejected -- the bound is shared across both loopback addresses.</summary>
    [Fact]
    public async Task RunAsync_SecondConnectionCrossFamilyWhileSlotOccupied_IsRejected()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        }, maxConcurrentConnections: 1);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
        await WaitUntilAsync(() => IsDisconnected(secondClient));

        Assert.Single(connections);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that, with a bound above one, several devices are admitted and served concurrently rather than one replacing another.</summary>
    [Fact]
    public async Task RunAsync_MultipleConnectionsWithinBound_AllAdmittedConcurrently()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        }, maxConcurrentConnections: 3);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
        using Socket thirdClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 3);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 3);

        foreach (FakePublicWebSocketConnection connection in connections)
        {
            connection.Complete();
        }

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that, with a bound above one, an attempt past the bound is rejected while every already-admitted connection keeps running undisturbed.</summary>
    [Fact]
    public async Task RunAsync_ConnectionBeyondBound_IsRejectedWhileOthersStayConnected()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        }, maxConcurrentConnections: 2);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 2);

        using Socket thirdClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => IsDisconnected(thirdClient));

        Assert.Equal(2, connections.Count);
        Assert.Equal(2, listener.CurrentConnections.Count);

        foreach (FakePublicWebSocketConnection connection in connections)
        {
            connection.Complete();
        }

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that one connection ending frees exactly its own slot, admitting a replacement without disturbing the other already-admitted connection.</summary>
    [Fact]
    public async Task RunAsync_SlotFreesIndependently_OneDisconnectAdmitsNextWithoutAffectingOthers()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        }, maxConcurrentConnections: 2);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 2);
        FakePublicWebSocketConnection untouchedConnection = connections.ElementAt(0);

        connections.ElementAt(1).Complete();
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 1);
        Assert.Contains(untouchedConnection, listener.CurrentConnections);

        using Socket thirdClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 3);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 2);
        Assert.Contains(untouchedConnection, listener.CurrentConnections);

        untouchedConnection.Complete();
        connections.ElementAt(2).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a non-positive admission bound is rejected.</summary>
    [Fact]
    public void Constructor_NonPositiveMaxConcurrentConnections_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream), maxConcurrentConnections: 0));
    }

    /// <summary>Verifies that cancelling before any client ever connects ends the accept loop without throwing.</summary>
    [Fact]
    public async Task RunAsync_CancelledBeforeAnyConnection_EndsWithoutThrowing()
    {
        using var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));
        using var cancellation = new CancellationTokenSource();

        Task runTask = listener.RunAsync(cancellation.Token);
        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that cancelling while a connection is active ends the loop without throwing, once that connection observes the cancellation.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileConnectionActive_EndsWithoutThrowing()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);

        cancellation.Cancel();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that <see cref="PublicWebSocketListener.RunAsync"/> does not return until the currently-served connection's own teardown has finished, not merely once the accept loops themselves stop.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileConnectionTearsDownSlowly_DoesNotReturnUntilConnectionEnds()
    {
        var teardownDelay = TimeSpan.FromMilliseconds(300);
        using var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream, teardownDelay));
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => listener.CurrentConnections.Count > 0);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // The fake connection's teardown waits out this same teardownDelay via Task.Delay, whose
        // own timer is not exact against Stopwatch's independent clock -- a real, observed CI
        // failure landed 1.3ms short of the raw bound. This tolerance absorbs that timer slop
        // without weakening the actual proof: RunAsync must still wait out nearly the whole delay,
        // not return as soon as cancellation is requested.
        var timerSlop = TimeSpan.FromMilliseconds(20);
        Assert.True(
            stopwatch.Elapsed >= teardownDelay - timerSlop,
            $"RunAsync returned after {stopwatch.Elapsed}, before the connection's {teardownDelay} teardown delay (- {timerSlop} timer slop) elapsed.");
    }

    /// <summary>
    /// Verifies that, with several concurrently admitted connections, <see cref="PublicWebSocketListener.RunAsync"/>
    /// waits out every one of their teardowns -- not merely the first to finish -- proving the bounded
    /// admission model awaits the full set of serve tasks rather than a single one.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelledWithMultipleConnectionsTearingDownAtDifferentSpeeds_WaitsForTheSlowestOne()
    {
        var fastTeardownDelay = TimeSpan.FromMilliseconds(50);
        var slowTeardownDelay = TimeSpan.FromMilliseconds(300);
        int connectionIndex = 0;
        using var listener = new PublicWebSocketListener(0, stream =>
            new FakePublicWebSocketConnection(stream, Interlocked.Increment(ref connectionIndex) == 1 ? fastTeardownDelay : slowTeardownDelay),
            maxConcurrentConnections: 2);
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 2);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Same timer-slop tolerance as the single-connection case above: RunAsync must still wait out
        // nearly the slower connection's whole delay, not return as soon as the faster one finishes.
        var timerSlop = TimeSpan.FromMilliseconds(20);
        Assert.True(
            stopwatch.Elapsed >= slowTeardownDelay - timerSlop,
            $"RunAsync returned after {stopwatch.Elapsed}, before the slower connection's {slowTeardownDelay} teardown delay (- {timerSlop} timer slop) elapsed.");
    }

    /// <summary>Verifies that a connection failing with an unexpected exception does not end the accept loop for later connections.</summary>
    [Fact]
    public async Task RunAsync_ConnectionThrows_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        ThrowingPublicWebSocketConnection? failingConnection = null;
        int firstAccept = 1;
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            if (Interlocked.Exchange(ref firstAccept, 0) == 1)
            {
                failingConnection = new ThrowingPublicWebSocketConnection(waitForRelease: true);
                return failingConnection;
            }

            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => Volatile.Read(ref firstAccept) == 0);
        await failingConnection!.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => listener.CurrentConnections.Count > 0);
        failingConnection.ReleaseFailure();
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the connection factory throwing does not end the accept loop for later connections.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryThrows_StillAcceptsSubsequentConnection()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        int factoryCalls = 0;
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                throw new InvalidOperationException("Simulated factory failure.");
            }

            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => Volatile.Read(ref factoryCalls) == 1);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);

        connections.ElementAt(0).Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a connection whose <see cref="IPublicWebSocketConnection.RunAsync"/> completes
    /// synchronously (before the accept loop ever stores it as current) is still cleared correctly and
    /// never left visible as a stale current connection, and that the admission slot it held is freed
    /// for the next connection.
    /// </summary>
    [Fact]
    public async Task RunAsync_ConnectionCompletesSynchronously_NeverExposesStaleConnectionAndAdmitsNext()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connection.Complete();
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 2);
        Assert.NotSame(connections.ElementAt(0), connections.ElementAt(1));
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies the same synchronous-completion race fix on the IPv6 loopback accept loop.</summary>
    [Fact]
    public async Task RunAsync_ConnectionCompletesSynchronouslyOnIPv6_NeverExposesStaleConnectionAndAdmitsNext()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        using var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connection.Complete();
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
        await WaitUntilAsync(() => connections.Count == 1);
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetworkV6);
        await WaitUntilAsync(() => connections.Count == 2);
        Assert.NotSame(connections.ElementAt(0), connections.ElementAt(1));
        await WaitUntilAsync(() => listener.CurrentConnections.Count == 0);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that disposing the listener stops accepting but leaves its active connection running.</summary>
    [Fact]
    public async Task Dispose_WhileConnectionActive_LeavesConnectionRunningUntilItEnds()
    {
        ConcurrentQueue<FakePublicWebSocketConnection> connections = new();
        var listener = new PublicWebSocketListener(0, stream =>
        {
            var connection = new FakePublicWebSocketConnection(stream);
            connections.Enqueue(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort, AddressFamily.InterNetwork);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.Single(listener.CurrentConnections);

        listener.Dispose();
        Assert.Single(listener.CurrentConnections);

        connections.ElementAt(0).Complete();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(listener.CurrentConnections);
    }

    /// <summary>Verifies that disposing the listener while it is waiting to accept ends the loop promptly instead of spinning.</summary>
    [Fact]
    public async Task RunAsync_ListenerDisposedWhileWaitingToAccept_EndsWithoutSpinning()
    {
        var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        Task runTask = listener.RunAsync(CancellationToken.None);
        listener.Dispose();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that disposing an already-disposed listener does not throw.</summary>
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        listener.Dispose();
        listener.Dispose();
    }

    /// <summary>
    /// Verifies that construction binds the explicit loopback address on each socket rather than a
    /// wildcard address, deterministically: reads the sockets' own bound <see cref="IPEndPoint"/>
    /// rather than depending on a LAN interface, external networking, or shelling out to a diagnostic
    /// tool. A future change from <see cref="IPAddress.Loopback"/>/<see cref="IPAddress.IPv6Loopback"/>
    /// to a wildcard bind address fails this test immediately, even though the accepted-remote-address
    /// check in <see cref="PublicWebSocketListener.IsLoopbackRemote"/> would still defend against a
    /// non-loopback peer on its own.
    /// </summary>
    [Fact]
    public void Constructor_BindsExplicitLoopbackAddresses_NotWildcard()
    {
        using var listener = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        Assert.Equal(IPAddress.Loopback, listener.BoundIPv4Address);
        Assert.Equal(IPAddress.IPv6Loopback, listener.BoundIPv6Address);
        Assert.NotEqual(IPAddress.Any, listener.BoundIPv4Address);
        Assert.NotEqual(IPAddress.IPv6Any, listener.BoundIPv6Address);
    }

    /// <summary>Verifies that constructing a second listener on a port already bound by another fails fast.</summary>
    [Fact]
    public void Constructor_PortAlreadyInUse_Throws()
    {
        using var first = new PublicWebSocketListener(0, stream => new FakePublicWebSocketConnection(stream));

        Assert.Throws<SocketException>(() => new PublicWebSocketListener(first.BoundPort, stream => new FakePublicWebSocketConnection(stream)));
    }

    /// <summary>Verifies that IPv4 and IPv6 loopback addresses are recognized as loopback.</summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void IsLoopbackRemote_LoopbackAddress_ReturnsTrue(string address)
    {
        Assert.True(PublicWebSocketListener.IsLoopbackRemote(new IPEndPoint(IPAddress.Parse(address), 12345)));
    }

    /// <summary>Verifies that a non-loopback address is rejected.</summary>
    [Fact]
    public void IsLoopbackRemote_NonLoopbackAddress_ReturnsFalse()
    {
        Assert.False(PublicWebSocketListener.IsLoopbackRemote(new IPEndPoint(IPAddress.Parse("10.0.0.5"), 12345)));
    }

    /// <summary>Verifies that a null or non-IP endpoint is rejected rather than assumed safe.</summary>
    [Fact]
    public void IsLoopbackRemote_NullEndPoint_ReturnsFalse()
    {
        Assert.False(PublicWebSocketListener.IsLoopbackRemote(null));
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

    /// <summary>Whether a connected socket has since been closed by the remote peer.</summary>
    /// <param name="socket">The socket to probe.</param>
    private static bool IsDisconnected(Socket socket)
    {
        try
        {
            return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>Connects a plain client socket to the listener's bound loopback port over the given address family.</summary>
    /// <param name="port">The listener's bound port.</param>
    /// <param name="addressFamily">Which loopback address to connect through.</param>
    private static async Task<Socket> ConnectClientAsync(int port, AddressFamily addressFamily)
    {
        IPAddress address = addressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var client = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(address, port);
        return client;
    }
}
