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
