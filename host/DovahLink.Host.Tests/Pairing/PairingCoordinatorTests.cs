using DovahLink.Host;
using DovahLink.Host.Pairing;
using DovahLink.Host.Trust;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Pairing;

/// <summary>Tests for <see cref="PairingCoordinator"/>.</summary>
public class PairingCoordinatorTests
{
    /// <summary>Verifies that global reset cancellation removes the active challenge.</summary>
    [Fact]
    public async Task CancelAll_RemovesActiveChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        coordinator.CancelAll();

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");
        Assert.Equal(PairingState.Rejected, result.Outcome);
    }

    /// <summary>Verifies that confirming the correct, unexpired code commits a new trusted device.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_CorrectCode_CommitsNewTrustedDevice()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        Assert.Equal(PairingState.Trusted, result.Outcome);
        Assert.NotNull(result.ClientId);
        Assert.NotNull(result.Credential);
        TrustRecord record = trustStore.TryGet(result.ClientId!.Value)!;
        Assert.Equal(KnownDeviceState.Trusted, record.State);
        Assert.Equal("Living Room PC", record.DisplayName);
    }

    /// <summary>Verifies that the persisted credential verifier is a hash of the returned credential, never the credential itself.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_CorrectCode_StoresHashNotRawCredential()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        TrustRecord record = trustStore.TryGet(result.ClientId!.Value)!;
        Assert.NotEqual(result.Credential, record.CredentialVerifier);
    }

    /// <summary>Verifies that confirming with the wrong code is rejected and commits nothing.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_WrongCode_Rejects()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        coordinator.BeginPairing();

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync("000000", "Living Room PC");

        Assert.Equal(PairingState.Rejected, result.Outcome);
        Assert.Null(result.ClientId);
        Assert.Null(result.Credential);
        Assert.Empty(trustStore.List());
    }

    /// <summary>Verifies that confirming with no challenge ever having been issued is rejected rather than throwing.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_NoChallengeIssued_Rejects()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync("123456", "Living Room PC");

        Assert.Equal(PairingState.Rejected, result.Outcome);
    }

    /// <summary>Verifies that confirming after the challenge has expired reports Expired rather than Rejected.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_ExpiredChallenge_ReportsExpired()
    {
        var trustStore = new FakeTrustStore();
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(trustStore, clock);
        PairingChallenge challenge = coordinator.BeginPairing();

        clock.Advance(TimeSpan.FromMinutes(6));
        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        Assert.Equal(PairingState.Expired, result.Outcome);
        Assert.Empty(trustStore.List());
    }

    /// <summary>Verifies that a second BeginPairing call while a challenge is still active returns the same challenge rather than a new one.</summary>
    [Fact]
    public void BeginPairing_CalledAgainWhileActive_ReturnsSameChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());

        PairingChallenge first = coordinator.BeginPairing();
        PairingChallenge second = coordinator.BeginPairing();

        Assert.Equal(first, second);
    }

    /// <summary>Verifies that BeginPairing issues a fresh challenge once the previous one has expired.</summary>
    [Fact]
    public void BeginPairing_CalledAfterPriorChallengeExpired_IssuesFreshChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        PairingChallenge first = coordinator.BeginPairing();

        clock.Advance(TimeSpan.FromMinutes(6));
        PairingChallenge second = coordinator.BeginPairing();

        Assert.NotEqual(first.Code, second.Code);
    }

    /// <summary>Verifies that a successfully confirmed challenge can no longer be confirmed a second time.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_CalledTwiceWithSameCode_SecondCallRejects()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        PairingConfirmationResult first = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");
        PairingConfirmationResult second = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        Assert.Equal(PairingState.Trusted, first.Outcome);
        Assert.Equal(PairingState.Rejected, second.Outcome);
    }

    /// <summary>Verifies that after a successful pairing, BeginPairing issues a fresh challenge rather than reusing the resolved one.</summary>
    [Fact]
    public async Task BeginPairing_AfterSuccessfulPairing_IssuesFreshChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        PairingChallenge first = coordinator.BeginPairing();
        await coordinator.ConfirmCredentialAsync(first.Code, "Living Room PC");

        PairingChallenge second = coordinator.BeginPairing();

        Assert.NotEqual(first.Code, second.Code);
    }

    /// <summary>Verifies that confirming with an empty code is rejected rather than crashing or matching.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_EmptyCode_Rejects()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        coordinator.BeginPairing();

        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(string.Empty, "Living Room PC");

        Assert.Equal(PairingState.Rejected, result.Outcome);
    }

    /// <summary>Verifies that a challenge confirmed at the exact moment it expires is still accepted (expiry is exclusive).</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_AtExactExpiryMoment_StillAccepted()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        PairingChallenge challenge = coordinator.BeginPairing();

        clock.UtcNow = challenge.ExpiresAtUtc;
        PairingConfirmationResult result = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        Assert.Equal(PairingState.Trusted, result.Outcome);
    }

    /// <summary>Verifies that two successive, independent pairings produce distinct client ids, credentials, and short ids.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_TwoSuccessivePairings_ProduceDistinctIdentitiesAndCredentials()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingChallenge firstChallenge = coordinator.BeginPairing();
        PairingConfirmationResult first = await coordinator.ConfirmCredentialAsync(firstChallenge.Code, "Living Room PC");

        PairingChallenge secondChallenge = coordinator.BeginPairing();
        PairingConfirmationResult second = await coordinator.ConfirmCredentialAsync(secondChallenge.Code, "Bedroom Tablet");

        Assert.NotEqual(first.ClientId, second.ClientId);
        Assert.NotEqual(first.Credential, second.Credential);
        Assert.NotEqual(trustStore.TryGet(first.ClientId!.Value)!.ShortId, trustStore.TryGet(second.ClientId!.Value)!.ShortId);
    }

    /// <summary>
    /// Verifies that two concurrent confirmations of the same correct code cannot both succeed: the
    /// race window between reading the challenge and clearing it after the commit must not let two
    /// trusted devices be created from one challenge.
    /// </summary>
    [Fact]
    public async Task ConfirmCredentialAsync_TwoConcurrentCallsWithSameCode_OnlyOneSucceeds()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        PairingConfirmationResult[] results = await Task.WhenAll(
            coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC"),
            coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC"));

        Assert.Single(results, result => result.Outcome == PairingState.Trusted);
        Assert.Single(results, result => result.Outcome == PairingState.Rejected);
        Assert.Single(trustStore.List());
    }

    /// <summary>Verifies that a persistence failure leaves the challenge confirmable again with the same code.</summary>
    [Fact]
    public async Task ConfirmCredentialAsync_RetryAfterPersistenceFailure_SucceedsOnSecondAttempt()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingChallenge challenge = coordinator.BeginPairing();

        await Assert.ThrowsAsync<IOException>(() => coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC"));

        trustStore.ThrowOnUpsert = null;
        PairingConfirmationResult retryResult = await coordinator.ConfirmCredentialAsync(challenge.Code, "Living Room PC");

        Assert.Equal(PairingState.Trusted, retryResult.Outcome);
    }
}
