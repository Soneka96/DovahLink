using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPublicWebSocketConnection"/> whose <see cref="RunAsync"/> completes only when told to, or when cancelled.</summary>
public sealed class FakePublicWebSocketConnection : IPublicWebSocketConnection
{
    /// <summary>Completes or cancels the pending <see cref="RunAsync"/> call.</summary>
    private readonly TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The transport this connection was created over.</summary>
    public Stream Stream { get; }

    /// <summary>Creates a fake connection over the given transport.</summary>
    /// <param name="stream">The transport this connection was created over.</param>
    public FakePublicWebSocketConnection(Stream stream)
    {
        Stream = stream;
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        await completionSource.Task.ConfigureAwait(false);
    }

    /// <summary>Ends the pending <see cref="RunAsync"/> call as if the connection ended normally.</summary>
    public void Complete() => completionSource.TrySetResult();

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload) => false;
}
