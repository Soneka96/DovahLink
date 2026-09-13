using System.Net;
using System.Net.Sockets;

namespace DovahLink.Host.Client.Transport;

/// <summary>
/// The public client channel's listening side: binds both supported loopback addresses,
/// <see cref="IPAddress.Loopback"/> and <see cref="IPAddress.IPv6Loopback"/>, on one port and serves
/// up to <see cref="PublicWebSocketListener"/>'s configured bound of concurrent public WebSocket
/// connections, supporting multiple simultaneous devices. A connection attempt while every slot is
/// occupied is rejected outright -- closed before its handshake even begins -- never queued and
/// never allowed to replace an active connection. Accepts a fresh connection again as soon as any
/// slot frees, whether that is the same device reconnecting or an additional device connecting for
/// the first time.
/// </summary>
public interface IPublicWebSocketListener : IDisposable
{
    /// <summary>The actual loopback port both listening sockets are bound to.</summary>
    int BoundPort { get; }

    /// <summary>
    /// Every currently active connection, as a point-in-time snapshot -- mutating it does not affect
    /// this listener. Empty when no public client is connected.
    /// </summary>
    IReadOnlyCollection<IPublicWebSocketConnection> CurrentConnections { get; }

    /// <summary>
    /// Runs both loopback addresses' accept loops until <paramref name="cancellationToken"/> is
    /// cancelled. Each loop keeps accepting -- so it can promptly reject a connection attempt once
    /// every slot is occupied -- rather than blocking until an admitted connection finishes; every
    /// admitted connection itself still runs until it ends or the token is cancelled. A single failed
    /// accept, rejected connection, connection-factory attempt, or served-connection failure never
    /// ends the loop for the rest of the host process's life -- the next accepted connection tries
    /// again. Once both accept loops stop, waits for every currently admitted connection's own
    /// bounded teardown to finish before returning, so shutdown is deterministic rather than racing
    /// an in-flight connection's disconnect notification and socket disposal.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop accepting and serving connections.</param>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="IPublicWebSocketListener"/>
public sealed class PublicWebSocketListener : IPublicWebSocketListener
{
    /// <summary>The bound, listening IPv4 loopback socket.</summary>
    private readonly Socket ipv4Socket;

    /// <summary>The bound, listening IPv6 loopback socket.</summary>
    private readonly Socket ipv6Socket;

    /// <summary>Creates a connection over a newly accepted transport.</summary>
    private readonly Func<Stream, IPublicWebSocketConnection> connectionFactory;

    /// <summary>The maximum number of connections admitted at once. See <see cref="TryAcquireSlot"/>.</summary>
    private readonly int maxConcurrentConnections;

    /// <summary>
    /// Guards <see cref="occupiedSlots"/> and <see cref="serveTasksByConnection"/> against concurrent
    /// access from both accept loops.
    /// </summary>
    private readonly object gate = new();

    /// <summary>
    /// The number of admission slots currently reserved, including a connection whose factory call is
    /// still in flight and has not yet been added to <see cref="serveTasksByConnection"/>. See
    /// <see cref="TryAcquireSlot"/>.
    /// </summary>
    private int occupiedSlots;

    /// <summary>Every currently active connection and the task serving it.</summary>
    private readonly Dictionary<IPublicWebSocketConnection, Task> serveTasksByConnection = new();

    /// <summary>
    /// Creates a listener and eagerly binds both loopback addresses on <paramref name="port"/>.
    /// Disposes any socket already opened before throwing if a later bind fails, so a failed
    /// construction never leaks a socket handle.
    /// </summary>
    /// <param name="port">
    /// The loopback TCP port to bind on both addresses, or zero to let the operating system assign
    /// one -- the IPv6 socket is then bound to the exact port the IPv4 socket was assigned, so both
    /// addresses always share one numeric port.
    /// </param>
    /// <param name="connectionFactory">Creates a connection over a newly accepted transport.</param>
    /// <param name="maxConcurrentConnections">The maximum number of connections admitted at once.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrentConnections"/> is not positive.</exception>
    public PublicWebSocketListener(
        int port,
        Func<Stream, IPublicWebSocketConnection> connectionFactory,
        int maxConcurrentConnections = Constants.MaxActiveSessions)
    {
        if (maxConcurrentConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentConnections));
        }

        this.connectionFactory = connectionFactory;
        this.maxConcurrentConnections = maxConcurrentConnections;

        ipv4Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            ipv4Socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            ipv4Socket.Listen(Constants.PublicWebSocketAcceptBacklog);
        }
        catch
        {
            ipv4Socket.Dispose();
            throw;
        }

        int boundPort = ((IPEndPoint)ipv4Socket.LocalEndPoint!).Port;
        ipv6Socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            ipv6Socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            ipv6Socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, boundPort));
            ipv6Socket.Listen(Constants.PublicWebSocketAcceptBacklog);
        }
        catch
        {
            ipv6Socket.Dispose();
            ipv4Socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public int BoundPort => ((IPEndPoint)ipv4Socket.LocalEndPoint!).Port;

    /// <summary>
    /// The IPv4 socket's actual bound local address, for a deterministic test proving construction
    /// binds the explicit loopback address rather than a wildcard address.
    /// </summary>
    internal IPAddress BoundIPv4Address => ((IPEndPoint)ipv4Socket.LocalEndPoint!).Address;

    /// <summary>
    /// The IPv6 socket's actual bound local address, for a deterministic test proving construction
    /// binds the explicit loopback address rather than a wildcard address.
    /// </summary>
    internal IPAddress BoundIPv6Address => ((IPEndPoint)ipv6Socket.LocalEndPoint!).Address;

    /// <inheritdoc/>
    public IReadOnlyCollection<IPublicWebSocketConnection> CurrentConnections
    {
        get
        {
            lock (gate)
            {
                return serveTasksByConnection.Keys.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            AcceptLoopAsync(ipv4Socket, cancellationToken),
            AcceptLoopAsync(ipv6Socket, cancellationToken)).ConfigureAwait(false);

        // Both accept loops have stopped admitting new connections, but any currently admitted one
        // may still be tearing down. This wait is not independently bounded here; it relies on every
        // IPublicWebSocketConnection.RunAsync's own documented contract to always complete within a
        // bounded time, so this cannot hang shutdown as long as every implementation honors that.
        Task[] serveTasks;
        lock (gate)
        {
            serveTasks = serveTasksByConnection.Values.ToArray();
        }

        await Task.WhenAll(serveTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes both listening sockets, ending a pending or future accept on either address. Does not
    /// affect an already active connection; stopping one is the shared cancellation token's
    /// responsibility.
    /// </summary>
    public void Dispose()
    {
        ipv4Socket.Dispose();
        ipv6Socket.Dispose();
    }

    /// <summary>Determines whether a newly accepted socket's remote address is a loopback address.</summary>
    /// <param name="remoteEndPoint">The accepted socket's remote endpoint.</param>
    /// <returns><see langword="true"/> when <paramref name="remoteEndPoint"/> is an IPv4 or IPv6 loopback address.</returns>
    internal static bool IsLoopbackRemote(EndPoint? remoteEndPoint) =>
        remoteEndPoint is IPEndPoint ipEndPoint && IPAddress.IsLoopback(ipEndPoint.Address);

    /// <summary>Runs one loopback address's accept loop.</summary>
    /// <param name="listeningSocket">The bound, listening socket to accept from.</param>
    /// <param name="cancellationToken">The token used to stop accepting and serving connections.</param>
    private async Task AcceptLoopAsync(Socket listeningSocket, CancellationToken cancellationToken)
    {
        while (true)
        {
            Socket acceptedSocket;
            try
            {
                acceptedSocket = await listeningSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The listening socket was disposed out from under a pending accept; stop rather than
                // spin retrying an accept that can only ever fail the same way from here on.
                return;
            }
            catch (Exception)
            {
                // A failed accept must not end the loop for the rest of the host process's life, but
                // retrying instantly would busy-spin a thread if the failure is persistent rather than
                // transient.
                try
                {
                    await Task.Delay(Constants.PublicWebSocketAcceptRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            if (!IsLoopbackRemote(acceptedSocket.RemoteEndPoint))
            {
                acceptedSocket.Dispose();
                continue;
            }

            if (!TryAcquireSlot())
            {
                // Every admission slot is occupied; reject before even attempting the handshake
                // rather than queuing or replacing an active connection.
                acceptedSocket.Dispose();
                continue;
            }

            NetworkStream acceptedStream = new(acceptedSocket, ownsSocket: true);
            IPublicWebSocketConnection connection;
            try
            {
                connection = connectionFactory(acceptedStream);
            }
            catch (Exception)
            {
                // The factory failed before any connection was ever admitted, so no connection owns
                // this slot yet; only the reservation needs releasing, not a serveTasksByConnection entry.
                acceptedStream.Dispose();
                lock (gate)
                {
                    occupiedSlots--;
                }

                continue;
            }

            // Serving runs detached from this loop rather than being awaited inline: the admission
            // bound already guarantees no more than maxConcurrentConnections are ever served at once,
            // but the accept loop itself must keep accepting (so it can promptly reject an attempt
            // once every slot is occupied) instead of blocking here until an active connection ends.
            // RunAsync still awaits every serve task after both accept loops stop, so shutdown remains
            // deterministic.
            //
            // ServeConnectionAsync must not actually start running connection.RunAsync before this
            // connection's entry is stored in serveTasksByConnection below: if RunAsync happened to
            // complete synchronously, its finally block could release the slot before this loop ever
            // stored the entry, leaving a stale entry added afterward that nothing would ever remove.
            // The start barrier holds ServeConnectionAsync at its first await until that storage below
            // has happened.
            TaskCompletionSource startBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task serveTask = ServeConnectionAsync(connection, cancellationToken, startBarrier.Task);
            lock (gate)
            {
                serveTasksByConnection[connection] = serveTask;
            }

            startBarrier.SetResult();
        }
    }

    /// <summary>
    /// Runs one admitted connection to completion and releases its admission slot afterward,
    /// independently of the accept loop that admitted it. Swallows every failure, including
    /// cancellation, so a connection fault can never crash this detached task.
    /// </summary>
    /// <param name="connection">The connection to serve.</param>
    /// <param name="cancellationToken">The token used to stop the connection.</param>
    /// <param name="startBarrier">
    /// Awaited before <paramref name="connection"/> is run, so this method never reaches
    /// <see cref="ReleaseSlot"/> before the accept loop has stored <paramref name="connection"/> in
    /// <see cref="serveTasksByConnection"/>, even when <see cref="IPublicWebSocketConnection.RunAsync"/>
    /// completes synchronously.
    /// </param>
    private async Task ServeConnectionAsync(IPublicWebSocketConnection connection, CancellationToken cancellationToken, Task startBarrier)
    {
        try
        {
            await startBarrier.ConfigureAwait(false);
            await connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed or cancelled connection must not end the accept loop for the rest of the host
            // process's life; the public transport simply admits one fewer client until the next
            // attempt succeeds.
        }
        finally
        {
            ReleaseSlot(connection);
        }
    }

    /// <summary>Attempts to atomically claim one of the bounded admission slots.</summary>
    /// <returns><see langword="true"/> when a slot was free and is now claimed by the caller.</returns>
    private bool TryAcquireSlot()
    {
        lock (gate)
        {
            if (occupiedSlots >= maxConcurrentConnections)
            {
                return false;
            }

            occupiedSlots++;
            return true;
        }
    }

    /// <summary>
    /// Releases <paramref name="connection"/>'s admission slot and removes it from
    /// <see cref="serveTasksByConnection"/>. A no-op removal (the entry was never stored, or was
    /// already removed) is expected whenever this races a slot reservation whose connection-factory
    /// call has not completed yet; it is not treated as an error.
    /// </summary>
    /// <param name="connection">The connection whose slot is being released.</param>
    private void ReleaseSlot(IPublicWebSocketConnection connection)
    {
        lock (gate)
        {
            occupiedSlots--;
            serveTasksByConnection.Remove(connection);
        }
    }
}
