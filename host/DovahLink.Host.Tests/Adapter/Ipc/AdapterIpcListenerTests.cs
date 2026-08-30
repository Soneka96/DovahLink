using System.Net;
using System.Net.Sockets;
using DovahLink.Host.Adapter.Ipc;
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
        List<FakeAdapterIpcConnection> connections = [];
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.NotNull(listener.CurrentConnection);

        connections[0].Complete();
        await WaitUntilAsync(() => listener.CurrentConnection is null);

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection ending normally lets the listener accept a new connection, supporting reconnect.</summary>
    [Fact]
    public async Task RunAsync_AfterConnectionEnds_AcceptsAnotherConnection()
    {
        List<FakeAdapterIpcConnection> connections = [];
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        connections[0].Complete();

        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 2);

        connections[1].Complete();
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
        List<FakeAdapterIpcConnection> connections = [];
        using var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
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
        List<FakeAdapterIpcConnection> connections = [];
        ThrowingAdapterIpcConnection? failingConnection = null;
        bool firstAccept = true;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (firstAccept)
            {
                firstAccept = false;
                failingConnection = new ThrowingAdapterIpcConnection(waitForRelease: true);
                return failingConnection;
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => !firstAccept);
        await failingConnection!.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => listener.CurrentConnection is not null);
        failingConnection.ReleaseFailure();
        await WaitUntilAsync(() => listener.CurrentConnection is null);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections[0].Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that the connection factory throwing does not end the accept loop for later connections.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryThrows_StillAcceptsSubsequentConnection()
    {
        List<FakeAdapterIpcConnection> connections = [];
        int factoryCalls = 0;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            factoryCalls++;
            if (factoryCalls == 1)
            {
                throw new InvalidOperationException("Simulated factory failure.");
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => factoryCalls == 1);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections[0].Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a factory raising ObjectDisposedException does not end the accept loop.</summary>
    [Fact]
    public async Task RunAsync_ConnectionFactoryObjectDisposedException_StillAcceptsSubsequentConnection()
    {
        List<FakeAdapterIpcConnection> connections = [];
        bool firstAccept = true;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (firstAccept)
            {
                firstAccept = false;
                throw new ObjectDisposedException("factory");
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => !firstAccept);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections[0].Complete();
        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a connection raising ObjectDisposedException does not end the accept loop.</summary>
    [Fact]
    public async Task RunAsync_ConnectionObjectDisposedException_StillAcceptsSubsequentConnection()
    {
        List<FakeAdapterIpcConnection> connections = [];
        bool firstAccept = true;
        using var listener = new AdapterIpcListener(0, stream =>
        {
            if (firstAccept)
            {
                firstAccept = false;
                return new ThrowingAdapterIpcConnection(new ObjectDisposedException("connection"));
            }

            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket firstClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => !firstAccept);
        using Socket secondClient = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);

        connections[0].Complete();
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
        List<FakeAdapterIpcConnection> connections = [];
        var listener = new AdapterIpcListener(0, stream =>
        {
            var connection = new FakeAdapterIpcConnection(stream);
            connections.Add(connection);
            return connection;
        });
        using var cancellation = new CancellationTokenSource();
        Task runTask = listener.RunAsync(cancellation.Token);

        using Socket client = await ConnectClientAsync(listener.BoundPort);
        await WaitUntilAsync(() => connections.Count == 1);
        Assert.NotNull(listener.CurrentConnection);

        listener.Dispose();
        Assert.NotNull(listener.CurrentConnection);

        connections[0].Complete();
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
}
