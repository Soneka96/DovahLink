using DovahLink.Host.Adapter.Ipc;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IAdapterContinuityRecovery"/>.</summary>
public sealed class FakeAdapterContinuityRecovery : IAdapterContinuityRecovery
{
    /// <summary>Every connection passed to <see cref="SetCurrentConnection"/>, in call order.</summary>
    public List<IAdapterIpcConnection?> CurrentConnectionCalls { get; } = [];

    /// <summary>Every generation passed to <see cref="RequestRecovery"/>, in call order.</summary>
    public List<long> RecoveryRequests { get; } = [];

    /// <inheritdoc/>
    public void SetCurrentConnection(IAdapterIpcConnection? connection) => CurrentConnectionCalls.Add(connection);

    /// <inheritdoc/>
    public void RequestRecovery(long connectionGeneration) => RecoveryRequests.Add(connectionGeneration);
}
