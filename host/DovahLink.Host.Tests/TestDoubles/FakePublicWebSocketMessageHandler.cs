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

    /// <summary>The number of times <see cref="HandleConnectionEnded"/> has been called.</summary>
    public int ConnectionEndedCalls { get; private set; }

    /// <summary>
    /// The order in which <see cref="HandleConnectionEnded"/> and <see cref="HandleDisconnectedAsync"/>
    /// were called, recording <c>"ConnectionEnded"</c> and <c>"Disconnected"</c> respectively, so a
    /// test can assert the mandatory-before-best-effort ordering the transport promises.
    /// </summary>
    public ConcurrentQueue<string> CallOrder { get; } = new();

    /// <summary>Gets a task that completes once <see cref="HandleDisconnectedAsync"/> is called.</summary>
    public Task Disconnected => disconnected.Task;

    /// <summary>When set, <see cref="HandleDisconnectedAsync"/> throws this exception after recording the call.</summary>
    public Exception? DisconnectedFailure { get; set; }

    /// <summary>When set, <see cref="HandleConnectionEnded"/> throws this exception after recording the call.</summary>
    public Exception? ConnectionEndedFailure { get; set; }

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
    public void HandleConnectionEnded()
    {
        ConnectionEndedCalls++;
        CallOrder.Enqueue("ConnectionEnded");

        if (ConnectionEndedFailure is not null)
        {
            throw ConnectionEndedFailure;
        }
    }

    /// <inheritdoc/>
    public async Task HandleDisconnectedAsync(CancellationToken cancellationToken)
    {
        DisconnectedCalls++;
        CallOrder.Enqueue("Disconnected");
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
