using DovahLink.Host.Pairing;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A pairing coordinator fake that records global cancellation for reset tests.</summary>
public sealed class FakePairingCoordinator : IPairingCoordinator
{
    /// <summary>The number of times <see cref="CancelAll"/> has been called.</summary>
    public int CancelAllCallCount { get; private set; }

    /// <summary>The clients whose pairing operations were cancelled.</summary>
    public List<DovahLink.Host.Identity.ClientId> CancelledClientIds { get; } = [];

    /// <inheritdoc/>
    public PairingStartResult BeginPairing(DovahLink.Host.Identity.ClientId clientId) =>
        new(PairingStartOutcome.OtherDeviceActive, null);

    /// <inheritdoc/>
    public PairingConfirmationResult ConfirmCode(
        DovahLink.Host.Identity.ClientId clientId, string code, string? displayName) =>
        new(PairingConfirmOutcome.Invalid, null, null);

    /// <inheritdoc/>
    public Task<PairingCommitResult> CommitPendingAsync(
        DovahLink.Host.Identity.ClientId clientId,
        string credential,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PairingCommitResult(PairingCommitOutcome.PendingNotFound));

    /// <inheritdoc/>
    public PairingRenotifyResult TryRenotify(DovahLink.Host.Identity.ClientId clientId) =>
        new(PairingRenotifyOutcome.AlreadyIdle);

    /// <inheritdoc/>
    public PairingCancelOutcome Cancel(DovahLink.Host.Identity.ClientId clientId) =>
        CancelAndRecord(clientId);

    /// <inheritdoc/>
    public void NotifyDisconnected(DovahLink.Host.Identity.ClientId clientId) { }

    /// <inheritdoc/>
    public void NotifyReconnected(DovahLink.Host.Identity.ClientId clientId) { }

    /// <inheritdoc/>
    public void CancelAll() => CancelAllCallCount++;

    /// <summary>Records one client-specific cancellation and returns the fake outcome.</summary>
    private PairingCancelOutcome CancelAndRecord(DovahLink.Host.Identity.ClientId clientId)
    {
        CancelledClientIds.Add(clientId);
        return PairingCancelOutcome.Cancelled;
    }
}
