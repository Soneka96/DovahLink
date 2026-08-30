using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A stand-in for <see cref="IAdapterIpcConnection"/> whose <see cref="RunAsync"/> always fails, for exercising a listener's resilience to a single failed connection.</summary>
public sealed class ThrowingAdapterIpcConnection : IAdapterIpcConnection
{
    /// <inheritdoc/>
    public Task RunAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated connection failure.");

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
}
