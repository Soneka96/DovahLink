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

    /// <summary>When set, <see cref="HandleDisconnectedAsync"/> throws this exception after recording the call.</summary>
    public Exception? DisconnectedFailure { get; set; }

    /// <summary>When <see langword="true"/>, <see cref="HandleDisconnectedAsync"/> waits on its received token instead of completing immediately, simulating a hung-but-cooperative handler.</summary>
    public bool HangOnDisconnected { get; set; }

    /// <summary>
    /// Whether the token passed to <see cref="HandleDisconnectedAsync"/> was actually cancelled while
    /// <see cref="HangOnDisconnected"/> was waiting on it -- proving the caller supplied a real,
    /// usable token rather than <see cref="CancellationToken.None"/>.
    /// </summary>
    public bool ReceivedTokenWasCancelled { get; private set; }

    /// <inheritdoc/>
    public Task HandleMessageAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ReceivedMessages.Enqueue(payload.ToArray());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task HandleDisconnectedAsync(CancellationToken cancellationToken)
    {
        DisconnectedCalls++;
        disconnected.TrySetResult();

        if (HangOnDisconnected)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReceivedTokenWasCancelled = true;
                throw;
            }
        }

        if (DisconnectedFailure is not null)
        {
            throw DisconnectedFailure;
        }
    }
}
