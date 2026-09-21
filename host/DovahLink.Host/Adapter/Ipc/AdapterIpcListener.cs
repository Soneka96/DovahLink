using System.Net;
using System.Net.Sockets;
using DovahLink.Host.PlayContext;

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

    /// <summary>Tracks the active connection for generation-checked continuity recovery.</summary>
    private readonly IAdapterContinuityRecovery? continuityRecovery;

    /// <summary>Guards <see cref="currentConnection"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently active connection, or <see langword="null"/> when no adapter is connected.</summary>
    private IAdapterIpcConnection? currentConnection;

    /// <summary>
    /// Creates a listener and eagerly binds its loopback port. Disposes the underlying socket before
    /// throwing if binding or listening fails, so a failed construction never leaks the socket handle.
    /// </summary>
    /// <param name="port">The loopback TCP port to bind, or zero to let the operating system assign one.</param>
    /// <param name="connectionFactory">Creates a connection over a newly accepted transport.</param>
    /// <param name="continuityRecovery">Tracks the active connection for generation-checked recovery, or <see langword="null"/> for standalone construction.</param>
    public AdapterIpcListener(
        int port,
        Func<Stream, IAdapterIpcConnection> connectionFactory,
        IAdapterContinuityRecovery? continuityRecovery = null)
    {
        this.connectionFactory = connectionFactory;
        this.continuityRecovery = continuityRecovery;
        listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            listenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            listenerSocket.Listen(1);
        }
        catch
        {
            listenerSocket.Dispose();
            throw;
        }
    }

    /// <summary>Creates a listener using the configured private IPC loopback port.</summary>
    /// <param name="connectionFactory">Creates a connection over a newly accepted transport.</param>
    public AdapterIpcListener(Func<Stream, IAdapterIpcConnection> connectionFactory)
        : this(Constants.AdapterIpcLoopbackPort, connectionFactory)
    {
    }

    /// <summary>Creates a listener from its own runtime configuration and the Host-lifetime connection factory.</summary>
    /// <param name="options">The private adapter-IPC listener's own runtime configuration.</param>
    /// <param name="connectionFactory">Builds a fresh connection-owned graph for each accepted transport.</param>
    /// <param name="continuityRecovery">Tracks the active connection for generation-checked recovery.</param>
    public AdapterIpcListener(
        AdapterIpcOptions options,
        IAdapterConnectionFactory connectionFactory,
        IAdapterContinuityRecovery continuityRecovery)
        : this(options.ListenerPort, connectionFactory.Create, continuityRecovery)
    {
    }

    /// <summary>Creates a listener from its own runtime configuration without a recovery observer.</summary>
    /// <param name="options">The private adapter-IPC listener's own runtime configuration.</param>
    /// <param name="connectionFactory">Builds a fresh connection-owned graph for each accepted connection.</param>
    public AdapterIpcListener(AdapterIpcOptions options, IAdapterConnectionFactory connectionFactory)
        : this(options.ListenerPort, connectionFactory.Create)
    {
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
            Socket acceptedSocket;
            try
            {
                acceptedSocket = await listenerSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
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
                // retrying instantly would busy-spin a thread if the failure is persistent (for
                // example handle exhaustion) rather than transient.
                try
                {
                    await Task.Delay(Constants.AdapterIpcAcceptRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            NetworkStream acceptedStream = new(acceptedSocket, ownsSocket: true);
            IAdapterIpcConnection connection;
            try
            {
                connection = connectionFactory(acceptedStream);
            }
            catch (Exception)
            {
                acceptedStream.Dispose();
                continue;
            }

            try
            {
                SetCurrentConnection(connection);
                try
                {
                    await connection.RunAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                finally
                {
                    SetCurrentConnection(null);
                }
            }
            catch (Exception)
            {
                // A failed connection must not end the loop for the rest of the host process's life;
                // the adapter simply remains unavailable until the next attempt succeeds.
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

        continuityRecovery?.SetCurrentConnection(connection);
    }
}

// TODO(stage4-file-extraction): Move IAdapterContinuityRecovery/AdapterContinuityRecovery and
// IPlayContextResynchronizationTrigger/PlayContextResynchronizationTrigger to their own files in
// the post-Stage-4 structural cleanup PR. Temporarily colocated with the listener they consume to
// hold this PR's changed-file count down; extraction only, no behavior change.
/// <summary>
/// Requests controlled recovery of the exact adapter connection that could not safely accept a
/// reliable capture. The normal connection lifecycle owns the resulting disconnect and reconnect.
/// </summary>
public interface IAdapterContinuityRecovery
{
    /// <summary>Records the connection whose close can be requested, or clears the current connection.</summary>
    /// <param name="connection">The currently active connection, or <see langword="null"/> after it ends.</param>
    void SetCurrentConnection(IAdapterIpcConnection? connection);

    /// <summary>
    /// Requests recovery only when the active connection still has the supplied generation. A stale
    /// capture therefore cannot close a newer connection.
    /// </summary>
    /// <param name="connectionGeneration">The generation that produced the capture requiring recovery.</param>
    void RequestRecovery(long connectionGeneration);
}

/// <inheritdoc cref="IAdapterContinuityRecovery"/>
public sealed class AdapterContinuityRecovery : IAdapterContinuityRecovery
{
    /// <summary>Guards <see cref="currentConnection"/>.</summary>
    private readonly object gate = new();

    /// <summary>The connection currently eligible for generation-checked recovery.</summary>
    private IAdapterIpcConnection? currentConnection;

    /// <inheritdoc/>
    public void SetCurrentConnection(IAdapterIpcConnection? connection)
    {
        lock (gate)
        {
            currentConnection = connection;
        }
    }

    /// <inheritdoc/>
    public void RequestRecovery(long connectionGeneration)
    {
        IAdapterIpcConnection? connection;
        lock (gate)
        {
            connection = currentConnection?.ConnectionGeneration == connectionGeneration
                ? currentConnection
                : null;
        }

        connection?.RequestClose();
    }
}

/// <summary>
/// Requests a fresh baseline whenever the play context transitions while the adapter connection
/// stays up -- a save load with no intervening reconnect -- so <c>character_level</c> and every
/// other baseline-required area is not left stale until the player happens to level up or the
/// connection happens to drop. Subscribes to <see cref="IPlayContextTracker.Transitioned"/> for the
/// host process's own lifetime at construction, matching <see cref="State.StatePublisher{TState}"/>'s
/// identical subscription discipline for the same event; never unsubscribed.
/// </summary>
public interface IPlayContextResynchronizationTrigger
{
    /// <summary>
    /// Reacts to one committed play-context transition by re-arming resynchronization and requesting
    /// a fresh baseline on the currently active adapter connection, if any. A no-op when
    /// <see cref="PlayContextTransition.NewPlayContextId"/> is <see langword="null"/>: no play
    /// context exists to resynchronize. Otherwise unconditional: every real transition (already
    /// deduplicated for a repeated context by <see cref="IPlayContextTracker.NotifyTransition"/>
    /// itself) re-arms and re-requests, superseding whatever transaction the previous request may
    /// still be in flight for. An essential resynchronize request must never silently disappear: when
    /// the send itself fails (for example a full outbound queue), this forces the connection closed
    /// instead of leaving the re-armed requirement with no request ever having gone out -- the
    /// adapter's normal reconnect then drives a fresh initial resynchronization.
    /// </summary>
    /// <param name="transition">The transition that just committed.</param>
    void HandleTransition(PlayContextTransition transition);
}

/// <inheritdoc cref="IPlayContextResynchronizationTrigger"/>
public sealed class PlayContextResynchronizationTrigger : IPlayContextResynchronizationTrigger
{
    /// <summary>The tracker this trigger re-arms for every play-context transition.</summary>
    private readonly IAdapterAvailabilityTracker adapterAvailabilityTracker;

    /// <summary>The listener whose currently active connection this trigger sends the fresh request through.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>Creates a trigger subscribed to <paramref name="playContextTracker"/> for the host process's own lifetime.</summary>
    /// <param name="playContextTracker">The tracker this trigger subscribes to.</param>
    /// <param name="adapterAvailabilityTracker">The tracker this trigger re-arms for every play-context transition.</param>
    /// <param name="listener">The listener whose currently active connection this trigger sends the fresh request through.</param>
    public PlayContextResynchronizationTrigger(
        IPlayContextTracker playContextTracker,
        IAdapterAvailabilityTracker adapterAvailabilityTracker,
        IAdapterIpcListener listener)
    {
        this.adapterAvailabilityTracker = adapterAvailabilityTracker;
        this.listener = listener;

        playContextTracker.Transitioned += HandleTransition;
    }

    /// <inheritdoc/>
    public void HandleTransition(PlayContextTransition transition)
    {
        if (transition.NewPlayContextId is null)
        {
            //  No play context exists to resynchronize; a baseline is requested only once a later
            //  transition establishes a real one.
            return;
        }

        adapterAvailabilityTracker.RearmResynchronizationForPlayContextTransition();
        IAdapterIpcConnection? connection = listener.CurrentConnection;
        if (connection is not null && !connection.TrySendResynchronizeRequest())
        {
            connection.RequestClose();
        }
    }
}
