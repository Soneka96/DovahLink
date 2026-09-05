using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterIpcConnection"/> whose <see cref="RunAsync"/> completes only when told to, or when cancelled.</summary>
public sealed class FakeAdapterIpcConnection : IAdapterIpcConnection
{
    /// <summary>Completes or cancels the pending <see cref="RunAsync"/> call.</summary>
    private readonly TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The transport this connection was created over.</summary>
    public Stream Stream { get; }

    /// <summary>Creates a fake connection over the given transport.</summary>
    /// <param name="stream">The transport this connection was created over.</param>
    public FakeAdapterIpcConnection(Stream stream)
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
    public bool TrySendListenEvent(uint eventKey, out ulong correlationId)
    {
        correlationId = 0;
        return false;
    }

    /// <inheritdoc/>
    public bool TrySendReadSample(uint sampleToken, out ulong correlationId)
    {
        correlationId = 0;
        return false;
    }

    /// <inheritdoc/>
    public bool TryCancel(ulong correlationId) => false;

    /// <summary>The result <see cref="TrySendPairingDisplay"/> returns.</summary>
    public bool TrySendPairingDisplayResult { get; set; }

    /// <summary>The correlation id <see cref="TrySendPairingDisplay"/> reports when <see cref="TrySendPairingDisplayResult"/> is <see langword="true"/>.</summary>
    public ulong TrySendPairingDisplayCorrelationId { get; set; }

    /// <summary>The code and mode passed to <see cref="TrySendPairingDisplay"/>, in call order.</summary>
    public List<(string Code, PairingDisplayMode Mode)> PairingDisplayCalls { get; } = [];

    /// <summary>The result <see cref="TrySendPairingAttemptsExhausted"/> returns.</summary>
    public bool TrySendPairingAttemptsExhaustedResult { get; set; }

    /// <summary>The number of times <see cref="TrySendPairingAttemptsExhausted"/> was called.</summary>
    public int PairingAttemptsExhaustedCalls { get; private set; }

    /// <summary>The function used to produce <see cref="AwaitPairingDisplayAckAsync"/>'s result. Defaults to always returning <see langword="false"/>.</summary>
    public Func<ulong, TimeSpan, CancellationToken, Task<bool>> AwaitPairingDisplayAckAsyncResult { get; set; } = (_, _, _) => Task.FromResult(false);

    /// <summary>The arguments passed to <see cref="AwaitPairingDisplayAckAsync"/>, in call order.</summary>
    public List<(ulong CorrelationId, TimeSpan Timeout, CancellationToken CancellationToken)> AwaitPairingDisplayAckAsyncCalls { get; } = [];

    /// <inheritdoc/>
    public bool TrySendPairingDisplay(string code, PairingDisplayMode mode, out ulong correlationId)
    {
        PairingDisplayCalls.Add((code, mode));
        correlationId = TrySendPairingDisplayResult ? TrySendPairingDisplayCorrelationId : 0;
        return TrySendPairingDisplayResult;
    }

    /// <inheritdoc/>
    public bool TrySendPairingAttemptsExhausted()
    {
        PairingAttemptsExhaustedCalls++;
        return TrySendPairingAttemptsExhaustedResult;
    }

    /// <inheritdoc/>
    public Task<bool> AwaitPairingDisplayAckAsync(ulong correlationId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        AwaitPairingDisplayAckAsyncCalls.Add((correlationId, timeout, cancellationToken));
        return AwaitPairingDisplayAckAsyncResult(correlationId, timeout, cancellationToken);
    }
}
