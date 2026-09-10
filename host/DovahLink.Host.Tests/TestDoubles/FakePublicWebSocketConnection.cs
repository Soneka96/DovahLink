using DovahLink.Host.Client.Transport;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IPublicWebSocketConnection"/> whose <see cref="RunAsync"/> completes only when told to, or when cancelled.</summary>
public sealed class FakePublicWebSocketConnection : IPublicWebSocketConnection
{
    /// <summary>Completes or cancels the pending <see cref="RunAsync"/> call.</summary>
    private readonly TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How long <see cref="RunAsync"/> keeps running after cancellation before it actually ends, simulating a connection's own bounded teardown.</summary>
    private readonly TimeSpan teardownDelayAfterCancellation;

    /// <summary>The transport this connection was created over.</summary>
    public Stream Stream { get; }

    /// <summary>Creates a fake connection over the given transport.</summary>
    /// <param name="stream">The transport this connection was created over.</param>
    /// <param name="teardownDelayAfterCancellation">
    /// How long <see cref="RunAsync"/> keeps running after cancellation before it actually ends,
    /// simulating a connection's own bounded teardown; defaults to ending immediately.
    /// </param>
    public FakePublicWebSocketConnection(Stream stream, TimeSpan teardownDelayAfterCancellation = default)
    {
        Stream = stream;
        this.teardownDelayAfterCancellation = teardownDelayAfterCancellation;
    }

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => _ = EndAfterTeardownDelayAsync(cancellationToken));
        await completionSource.Task.ConfigureAwait(false);
    }

    /// <summary>Ends the pending <see cref="RunAsync"/> call as if the connection ended normally.</summary>
    public void Complete() => completionSource.TrySetResult();

    /// <inheritdoc/>
    public bool TrySend(ReadOnlyMemory<byte> payload, PublicOutboundLane lane)
    {
        SentPayloads.Add(payload.ToArray());
        SentLanes.Add(lane);
        return TrySendResult;
    }

    /// <inheritdoc/>
    public bool TrySendSnapshot(StateAreaId areaId, ReadOnlyMemory<byte> payload)
    {
        SentSnapshots.Add((areaId, payload.ToArray()));
        return TrySendSnapshotResult;
    }

    /// <summary>Ends <see cref="RunAsync"/> as cancelled after <see cref="teardownDelayAfterCancellation"/>, simulating bounded teardown work.</summary>
    /// <param name="cancellationToken">The token <see cref="RunAsync"/> was cancelled with.</param>
    private async Task EndAfterTeardownDelayAsync(CancellationToken cancellationToken)
    {
        if (teardownDelayAfterCancellation > TimeSpan.Zero)
        {
            await Task.Delay(teardownDelayAfterCancellation).ConfigureAwait(false);
        }

        completionSource.TrySetCanceled(cancellationToken);
    }

    /// <summary>The value <see cref="TrySend"/> returns; defaults to <see langword="false"/>.</summary>
    public bool TrySendResult { get; set; }

    /// <summary>The value <see cref="TrySendSnapshot"/> returns; defaults to <see langword="false"/>.</summary>
    public bool TrySendSnapshotResult { get; set; }

    /// <summary>Every payload passed to <see cref="TrySend"/> so far, in call order.</summary>
    public List<byte[]> SentPayloads { get; } = [];

    /// <summary>Every area/payload pair passed to <see cref="TrySendSnapshot"/> so far, in call order.</summary>
    public List<(StateAreaId AreaId, byte[] Payload)> SentSnapshots { get; } = [];

    /// <summary>The number of times <see cref="RequestClose"/> has been called.</summary>
    public int RequestCloseCalls { get; private set; }

    /// <inheritdoc/>
    public void RequestClose() => RequestCloseCalls++;

    /// <summary>Every lane passed to <see cref="TrySend"/> so far, in call order, index-aligned with <see cref="SentPayloads"/>.</summary>
    public List<PublicOutboundLane> SentLanes { get; } = [];
}
