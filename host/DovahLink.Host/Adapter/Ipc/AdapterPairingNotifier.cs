using DovahLink.Host.Client.Dispatch;

namespace DovahLink.Host.Adapter.Ipc;

/// <summary>
/// The real <see cref="IPairingAdapterNotifier"/> implementation over the private adapter IPC
/// channel: requests display, redisplay, and attempts-exhausted notifications on whichever adapter
/// connection is currently active, per <see cref="IAdapterIpcListener.CurrentConnection"/>. A missing
/// or disconnected adapter is a controlled false or no-op outcome, never an exception.
/// </summary>
public sealed class AdapterPairingNotifier : IPairingAdapterNotifier
{
    /// <summary>The listener whose currently active connection this notifier sends requests through.</summary>
    private readonly IAdapterIpcListener listener;

    /// <summary>Creates a pairing-adapter notifier over the given adapter IPC listener.</summary>
    /// <param name="listener">The listener whose currently active connection this notifier sends requests through.</param>
    public AdapterPairingNotifier(IAdapterIpcListener listener)
    {
        this.listener = listener;
    }

    /// <inheritdoc/>
    public Task<bool> TryNotifyCodeAvailableAsync(string code, CancellationToken cancellationToken) =>
        TrySendDisplayAndAwaitAckAsync(code, PairingDisplayMode.Initial, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> TryNotifyRedisplayAsync(string code, CancellationToken cancellationToken) =>
        TrySendDisplayAndAwaitAckAsync(code, PairingDisplayMode.ManualRedisplay, cancellationToken);

    /// <inheritdoc/>
    public async Task NotifyCodeIncorrectAsync(string code, CancellationToken cancellationToken) =>
        await TrySendDisplayAndAwaitAckAsync(code, PairingDisplayMode.WrongCodeRedisplay, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task NotifyAttemptsExhaustedAsync(CancellationToken cancellationToken)
    {
        listener.CurrentConnection?.TrySendPairingAttemptsExhausted();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a pairing-display request on the currently active adapter connection and awaits its
    /// bounded acknowledgement.
    /// </summary>
    /// <param name="code">The code to display.</param>
    /// <param name="mode">Which display intent this request carries.</param>
    /// <param name="cancellationToken">The token used to stop waiting early.</param>
    /// <returns>
    /// <see langword="false"/> when no adapter is connected, the request could not be enqueued, or
    /// the wait timed out, was cancelled, or was declined.
    /// </returns>
    private async Task<bool> TrySendDisplayAndAwaitAckAsync(string code, PairingDisplayMode mode, CancellationToken cancellationToken)
    {
        IAdapterIpcConnection? connection = listener.CurrentConnection;
        if (connection is null || !connection.TrySendPairingDisplay(code, mode, out ulong correlationId))
        {
            return false;
        }

        return await connection.AwaitPairingDisplayAckAsync(correlationId, Constants.PairingDisplayAckTimeout, cancellationToken)
            .ConfigureAwait(false);
    }
}
