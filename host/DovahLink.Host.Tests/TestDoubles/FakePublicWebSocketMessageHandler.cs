using System.Collections.Concurrent;
using DovahLink.Host.Client.Transport;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPublicWebSocketMessageHandler"/> that records every call.</summary>
public sealed class FakePublicWebSocketMessageHandler : IPublicWebSocketMessageHandler
{
    /// <summary>Completes once <see cref="HandleDisconnectedAsync"/> is called.</summary>
    private readonly TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The payloads handled so far, in receipt order.</summary>
    public ConcurrentQueue<byte[]> ReceivedMessages { get; } = new();

    /// <summary>The number of times <see cref="HandleDisconnectedAsync"/> has been called.</summary>
    public int DisconnectedCalls { get; private set; }

    /// <summary>Gets a task that completes once <see cref="HandleDisconnectedAsync"/> is called.</summary>
    public Task Disconnected => disconnected.Task;

    /// <inheritdoc/>
    public Task HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ReceivedMessages.Enqueue(payload.ToArray());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task HandleDisconnectedAsync(CancellationToken cancellationToken)
    {
        DisconnectedCalls++;
        disconnected.TrySetResult();
        return Task.CompletedTask;
    }
}
