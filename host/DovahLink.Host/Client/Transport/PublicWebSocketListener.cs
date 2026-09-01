using System.Net;
using System.Net.Sockets;

namespace DovahLink.Host.Client.Transport;

/// <summary>
/// The public client channel's listening side: binds both supported loopback addresses,
/// <see cref="IPAddress.Loopback"/> and <see cref="IPAddress.IPv6Loopback"/>, on one port and serves
/// at most one public WebSocket connection at a time, matching the first host proof's single-client
/// bound. A second connection attempt while the slot is occupied is rejected outright -- closed
/// before its handshake even begins -- never queued and never allowed to replace the active
/// connection. Accepts a fresh connection again after the previous one ends, supporting reconnect.
/// </summary>
public interface IPublicWebSocketListener : IDisposable
{
    /// <summary>The actual loopback port both listening sockets are bound to.</summary>
    int BoundPort { get; }

    /// <summary>The currently active connection, or <see langword="null"/> when no public client is connected.</summary>
    IPublicWebSocketConnection? CurrentConnection { get; }

    /// <summary>
    /// Runs both loopback addresses' accept loops until <paramref name="cancellationToken"/> is
    /// cancelled. Each loop keeps accepting -- so it can promptly reject a concurrent second
    /// connection attempt on either address -- rather than blocking until an admitted connection
    /// finishes; the admitted connection itself still runs until it ends or the token is cancelled. A
    /// single failed accept, rejected connection, connection-factory attempt, or served-connection
    /// failure never ends the loop for the rest of the host process's life -- the next accepted
    /// connection tries again. Once both accept loops stop, waits for the most recently admitted
    /// connection's own bounded teardown to finish before returning, so shutdown is deterministic
    /// rather than racing an in-flight connection's disconnect notification and socket disposal.
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

    /// <summary>
    /// Guards <see cref="currentConnection"/>, <see cref="currentServeTask"/>, and
    /// <see cref="slotOccupied"/> against concurrent access from both accept loops.
    /// </summary>
    private readonly object gate = new();

    /// <summary>Whether the single connection admission slot is currently occupied.</summary>
    private bool slotOccupied;

    /// <summary>The currently active connection, or <see langword="null"/> when no public client is connected.</summary>
    private IPublicWebSocketConnection? currentConnection;

    /// <summary>
    /// The task serving the most recently admitted connection, or an already-completed task before
    /// any connection has ever been admitted. Awaited by <see cref="RunAsync"/> after both accept
    /// loops end, so shutdown does not complete while that connection's own teardown is still
    /// running.
    /// </summary>
    private Task currentServeTask = Task.CompletedTask;

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
    public PublicWebSocketListener(int port, Func<Stream, IPublicWebSocketConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory;

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

    /// <inheritdoc/>
    public IPublicWebSocketConnection? CurrentConnection
    {
        get
        {
            lock (gate)
            {
                return currentConnection;
            }
        }
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            AcceptLoopAsync(ipv4Socket, cancellationToken),
            AcceptLoopAsync(ipv6Socket, cancellationToken)).ConfigureAwait(false);

        // Both accept loops have stopped admitting new connections, but the most recently admitted
        // one may still be tearing down. This wait is not independently bounded here; it relies on
        // IPublicWebSocketConnection.RunAsync's own documented contract to always complete within a
        // bounded time, so this cannot hang shutdown as long as every implementation honors that.
        Task serveTask;
        lock (gate)
        {
            serveTask = currentServeTask;
        }

        await serveTask.ConfigureAwait(false);
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
                // The single-client admission slot is occupied; reject before even attempting the
                // handshake rather than queuing or replacing the active connection.
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
                // this slot yet; only the slot flag needs clearing, not currentConnection.
                acceptedStream.Dispose();
                lock (gate)
                {
                    slotOccupied = false;
                }

                continue;
            }

            // Serving runs detached from this loop rather than being awaited inline: the admission
            // slot bound already guarantees at most one connection is ever served at a time, but the
            // accept loop itself must keep accepting (so it can promptly reject a concurrent second
            // attempt) instead of blocking here until the active connection ends. RunAsync still
            // awaits this task after both accept loops stop, so shutdown remains deterministic.
            //
            // ServeConnectionAsync must not actually start running connection.RunAsync before
            // currentConnection/currentServeTask are stored below: if RunAsync happened to complete
            // synchronously, its finally block could release the slot and clear currentConnection
            // before this loop ever stored it, leaving a dead connection visible as current. The
            // start barrier holds ServeConnectionAsync at its first await until that storage below
            // has happened.
            TaskCompletionSource startBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task serveTask = ServeConnectionAsync(connection, cancellationToken, startBarrier.Task);
            lock (gate)
            {
                currentConnection = connection;
                currentServeTask = serveTask;
            }

            startBarrier.SetResult();
        }
    }

    /// <summary>
    /// Runs one admitted connection to completion and releases the admission slot afterward,
    /// independently of the accept loop that admitted it. Swallows every failure, including
    /// cancellation, so a connection fault can never crash this detached task.
    /// </summary>
    /// <param name="connection">The connection to serve.</param>
    /// <param name="cancellationToken">The token used to stop the connection.</param>
    /// <param name="startBarrier">
    /// Awaited before <paramref name="connection"/> is run, so this method never reaches
    /// <see cref="ReleaseSlot"/> before the accept loop has stored <paramref name="connection"/> as
    /// <see cref="currentConnection"/>, even when <see cref="IPublicWebSocketConnection.RunAsync"/>
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
            // process's life; the public transport simply admits no client until the next attempt
            // succeeds.
        }
        finally
        {
            ReleaseSlot(connection);
        }
    }

    /// <summary>Attempts to atomically claim the single connection admission slot.</summary>
    /// <returns><see langword="true"/> when the slot was free and is now claimed by the caller.</returns>
    private bool TryAcquireSlot()
    {
        lock (gate)
        {
            if (slotOccupied)
            {
                return false;
            }

            slotOccupied = true;
            return true;
        }
    }

    /// <summary>
    /// Releases the single connection admission slot and clears <see cref="currentConnection"/>, but
    /// only when it still refers to <paramref name="connection"/>. A later connection's own admission
    /// always replaces <see cref="currentConnection"/> before this connection's teardown can reach
    /// here, so this check is a defensive guard against ever clearing a newer connection's state
    /// rather than a condition expected to trigger in practice.
    /// </summary>
    /// <param name="connection">The connection whose slot is being released.</param>
    private void ReleaseSlot(IPublicWebSocketConnection connection)
    {
        lock (gate)
        {
            slotOccupied = false;
            if (ReferenceEquals(currentConnection, connection))
            {
                currentConnection = null;
            }
        }
    }
}
