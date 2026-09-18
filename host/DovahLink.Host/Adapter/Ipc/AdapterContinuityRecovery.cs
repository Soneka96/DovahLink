namespace DovahLink.Host.Adapter.Ipc;

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
