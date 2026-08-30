using System.Net;
using System.Net.Sockets;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The private adapter channel's listening side: binds a loopback-only TCP port and serves at most
/// one adapter connection at a time, matching the "exactly one adapter and one host" ownership model
/// for the current Skyrim lifetime. Accepts a fresh connection again after the previous one ends,
/// supporting reconnect.
/// </summary>
public interface IAdapterIpcListener : IDisposable
{
    /// <summary>The actual loopback port this listener is bound to.</summary>
    int BoundPort { get; }

    /// <summary>The currently active connection, or <see langword="null"/> when no adapter is connected.</summary>
    IAdapterIpcConnection? CurrentConnection { get; }

    /// <summary>
    /// Runs the accept loop until <paramref name="cancellationToken"/> is cancelled: accepts one
    /// connection, serves it to completion, then accepts the next. Returns normally on cancellation
    /// rather than propagating it. A single failed accept, connection, or connection-factory attempt
    /// never ends the loop for the rest of the host process's life -- the next iteration tries again.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop accepting and serving connections.</param>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAdapterIpcListener"/>
public sealed class AdapterIpcListener : IAdapterIpcListener
{
    /// <summary>The bound, listening loopback socket.</summary>
    private readonly Socket listenerSocket;

    /// <summary>Creates a connection over a newly accepted transport.</summary>
    private readonly Func<Stream, IAdapterIpcConnection> connectionFactory;

    /// <summary>Guards <see cref="currentConnection"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently active connection, or <see langword="null"/> when no adapter is connected.</summary>
    private IAdapterIpcConnection? currentConnection;

    /// <summary>Creates a listener and eagerly binds its loopback port.</summary>
    /// <param name="port">The loopback TCP port to bind, or zero to let the operating system assign one.</param>
    /// <param name="connectionFactory">Creates a connection over a newly accepted transport.</param>
    public AdapterIpcListener(int port, Func<Stream, IAdapterIpcConnection> connectionFactory)
    {
        this.connectionFactory = connectionFactory;
        listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        listenerSocket.Listen(1);
    }

    /// <inheritdoc/>
    public int BoundPort => ((IPEndPoint)listenerSocket.LocalEndPoint!).Port;

    /// <inheritdoc/>
    public IAdapterIpcConnection? CurrentConnection
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
        while (true)
        {
            try
            {
                Socket acceptedSocket = await listenerSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                IAdapterIpcConnection connection = connectionFactory(new NetworkStream(acceptedSocket, ownsSocket: true));
                SetCurrentConnection(connection);
                try
                {
                    await connection.RunAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    SetCurrentConnection(null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The listening socket was disposed out from under a running loop; stop rather than
                // spin retrying an accept that can only ever fail the same way from here on.
                return;
            }
            catch (Exception)
            {
                // A failed accept, connection, or connection-factory attempt must not end the loop
                // for the rest of the host process's life; the adapter simply remains unavailable
                // until the next attempt succeeds.
            }
        }
    }

    /// <summary>
    /// Closes the listening socket, ending a pending or future accept. Does not affect an already
    /// active connection; stopping one is the shared cancellation token's responsibility.
    /// </summary>
    public void Dispose() => listenerSocket.Dispose();

    /// <summary>Sets the currently active connection under <see cref="gate"/>.</summary>
    /// <param name="connection">The connection to record as active, or <see langword="null"/>.</param>
    private void SetCurrentConnection(IAdapterIpcConnection? connection)
    {
        lock (gate)
        {
            currentConnection = connection;
        }
    }
}
