namespace DovahLink.Host.Process;

/// <summary>
/// Waits for the adapter's named shutdown-request signal, so an orderly Skyrim close can ask this
/// host to begin its own deterministic teardown. The signal is scoped per Skyrim lifetime (see
/// <see cref="Constants.ShutdownEventName"/>), so a signal from one lifetime's adapter can never
/// reach a different lifetime's host.
/// </summary>
public interface IHostShutdownSignal : IDisposable
{
    /// <summary>Waits until the shutdown signal is set, or until <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <param name="cancellationToken">The token used to stop waiting without the signal ever being set.</param>
    Task WaitAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IHostShutdownSignal"/>
public sealed class NamedEventHostShutdownSignal : IHostShutdownSignal
{
    /// <summary>The owned named manual-reset event this instance waits on.</summary>
    private readonly EventWaitHandle handle;

    /// <summary>Creates a signal waiting on an explicit named event, creating it if it does not already exist.</summary>
    /// <param name="eventName">
    /// The named event to wait on, typically <see cref="Constants.ShutdownEventName"/> for the current
    /// Skyrim lifetime.
    /// </param>
    public NamedEventHostShutdownSignal(string eventName)
    {
        handle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, eventName);
    }

    /// <inheritdoc/>
    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        //  Deliberately does not pass cancellationToken to Task.Run itself: that would let Task.Run
        //  cancel *scheduling* the task if the token fires before it starts, throwing
        //  TaskCanceledException instead of returning normally. Cancellation is instead observed by
        //  WaitAny itself, via the token's own wait handle, so this method always completes normally
        //  regardless of which handle woke it.
        Task.Run(() => WaitHandle.WaitAny([handle, cancellationToken.WaitHandle]));

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();
}
