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
    public long? ConnectionGeneration { get; set; }

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
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        await completionSource.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool TrySendListenEvent(uint eventKey, out ulong correlationId)
    {
        correlationId = 0;
        return false;
    }

    /// <summary>The result <see cref="TrySendReadSample"/> returns.</summary>
    public bool TrySendReadSampleResult { get; set; }

    /// <summary>The correlation id <see cref="TrySendReadSample"/> reports when <see cref="TrySendReadSampleResult"/> is <see langword="true"/>.</summary>
    public ulong TrySendReadSampleCorrelationId { get; set; }

    /// <summary>The sample tokens whose direct or prepared send method was called, in call order.</summary>
    public List<uint> ReadSampleCalls { get; } = [];

    /// <summary>
    /// Invoked synchronously by every <see cref="TrySendReadSample"/> or
    /// <see cref="TrySendPreparedReadSample"/> call, once this call's own sample token is already
    /// recorded in <see cref="ReadSampleCalls"/> but before it resolves --
    /// lets a test inject work (for example applying the matching capture result immediately, before
    /// the caller has had a chance to record its own request as outstanding) to exercise a race the
    /// caller's own locking is meant to close. Mirrors
    /// <see cref="FakePublicConnectionContext.OnTrySend"/>'s identical purpose for the same kind of race.
    /// </summary>
    public Action? OnTrySendReadSample { get; set; }

    /// <inheritdoc/>
    public bool TrySendReadSample(uint sampleToken, out ulong correlationId)
    {
        ReadSampleCalls.Add(sampleToken);
        OnTrySendReadSample?.Invoke();
        correlationId = TrySendReadSampleResult ? TrySendReadSampleCorrelationId : 0;
        return TrySendReadSampleResult;
    }

    /// <summary>Invoked when a scheduler prepares a sample request, before its final availability check.</summary>
    public Action<uint>? OnPrepareReadSample { get; set; }

    /// <inheritdoc/>
    public IpcReadSampleMessage? PrepareReadSample(uint sampleToken)
    {
        OnPrepareReadSample?.Invoke(sampleToken);
        return ConnectionGeneration is null ? null : new IpcReadSampleMessage(TrySendReadSampleCorrelationId, sampleToken);
    }

    /// <inheritdoc/>
    public bool TrySendPreparedReadSample(IpcReadSampleMessage message, long expectedConnectionGeneration, out ulong correlationId)
    {
        ReadSampleCalls.Add(message.SampleToken);
        OnTrySendReadSample?.Invoke();
        if (ConnectionGeneration != expectedConnectionGeneration || !TrySendReadSampleResult)
        {
            correlationId = 0;
            return false;
        }

        correlationId = message.CorrelationId;
        return true;
    }

    /// <summary>The result <see cref="TrySendResynchronizeRequest"/> returns.</summary>
    public bool TrySendResynchronizeRequestResult { get; set; }

    /// <summary>The number of times <see cref="TrySendResynchronizeRequest"/> was called.</summary>
    public int ResynchronizeRequestCalls { get; private set; }

    /// <inheritdoc/>
    public bool TrySendResynchronizeRequest()
    {
        ResynchronizeRequestCalls++;
        return TrySendResynchronizeRequestResult;
    }

    /// <summary>The correlation ids passed to <see cref="TryCancel"/>, in call order.</summary>
    public List<ulong> CancelCalls { get; } = [];

    /// <inheritdoc/>
    public bool TryCancel(ulong correlationId)
    {
        CancelCalls.Add(correlationId);
        return false;
    }

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

    /// <summary>The number of times <see cref="RequestClose"/> was called.</summary>
    public int RequestCloseCalls { get; private set; }

    /// <inheritdoc/>
    public void RequestClose() => RequestCloseCalls++;

    /// <summary>Ends the pending <see cref="RunAsync"/> call as if the connection ended normally.</summary>
    public void Complete() => completionSource.TrySetResult();
}
