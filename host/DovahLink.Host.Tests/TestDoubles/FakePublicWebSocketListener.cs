using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPublicWebSocketListener"/> whose <see cref="RunAsync"/> completion is settable directly.</summary>
public sealed class FakePublicWebSocketListener : IPublicWebSocketListener
{
    /// <inheritdoc/>
    public int BoundPort { get; set; }

    /// <inheritdoc/>
    public IReadOnlyCollection<IPublicWebSocketConnection> CurrentConnections { get; set; } = [];

    /// <summary>Whether <see cref="RunAsync"/> has been called.</summary>
    public bool RunAsyncCalled { get; private set; }

    /// <summary>
    /// Optional task <see cref="RunAsync"/> awaits before returning, letting a test control exactly
    /// when this listener's run loop completes. Defaults to <see langword="null"/>, completing
    /// immediately.
    /// </summary>
    public Task? RunAsyncCompletion { get; set; }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        RunAsyncCalled = true;
        if (RunAsyncCompletion is { } completion)
        {
            await completion;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
