using DovahLink.Host.Pairing;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A pairing coordinator fake that records global cancellation for reset tests.</summary>
public sealed class FakePairingCoordinator : IPairingCoordinator
{
    /// <summary>The number of times <see cref="CancelAll"/> has been called.</summary>
    public int CancelAllCallCount { get; private set; }

    /// <inheritdoc/>
    public PairingChallenge BeginPairing() => new("000000", DateTimeOffset.UtcNow);

    /// <inheritdoc/>
    public Task<PairingConfirmationResult> ConfirmCredentialAsync(
        string code, string displayName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PairingConfirmationResult(PairingState.Rejected, null, null));

    /// <inheritdoc/>
    public void CancelAll() => CancelAllCallCount++;
}
