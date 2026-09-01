using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A stand-in for <see cref="IPublicWebSocketConnection"/> whose <see cref="RunAsync"/> always fails, for exercising a listener's resilience to a single failed connection.</summary>
public sealed class ThrowingPublicWebSocketConnection : IPublicWebSocketConnection
{
    /// <summary>The exception raised when the connection is run.</summary>
    private readonly Exception failure;

    /// <summary>Completes when the listener has started this connection.</summary>
    private readonly TaskCompletionSource runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases an optionally delayed connection failure.</summary>
    private readonly TaskCompletionSource failureRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether the connection waits for <see cref="ReleaseFailure"/> before failing.</summary>
    private readonly bool waitForRelease;

    /// <summary>Creates a connection double that raises the supplied failure when run.</summary>
    /// <param name="failure">The failure to raise, or a simulated operation failure by default.</param>
    /// <param name="waitForRelease">Whether to wait for <see cref="ReleaseFailure"/> before failing.</param>
    public ThrowingPublicWebSocketConnection(Exception? failure = null, bool waitForRelease = false)
    {
        this.failure = failure ?? new InvalidOperationException("Simulated connection failure.");
        this.waitForRelease = waitForRelease;
    }

    /// <summary>Gets a task that completes when <see cref="RunAsync"/> starts.</summary>
    public Task RunStarted => runStarted.Task;

    /// <summary>Allows an optionally delayed connection failure to be raised.</summary>
    public void ReleaseFailure() => failureRelease.TrySetResult();

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        runStarted.TrySetResult();
        if (waitForRelease)
        {
            await failureRelease.Task.ConfigureAwait(false);
        }

        throw failure;
    }

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload) => false;
}
