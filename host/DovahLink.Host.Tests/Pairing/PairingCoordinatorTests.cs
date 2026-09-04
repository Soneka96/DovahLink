using System.Text;
using DovahLink.Host;
using DovahLink.Host.Pairing;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;
using DovahLink.Host.Identity;

namespace DovahLink.Host.Tests.Pairing;

/// <summary>Tests for the host's owner-bound pairing state machine.</summary>
public class PairingCoordinatorTests
{
    /// <summary>Verifies that only the challenge owner can resume an active pairing operation.</summary>
    [Fact]
    public void BeginPairing_OnlyOwnerCanResume()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();

        PairingStartResult started = coordinator.BeginPairing(owner);
        PairingStartResult resumed = coordinator.BeginPairing(owner);
        PairingStartResult rejected = coordinator.BeginPairing(other);

        Assert.Equal(PairingStartOutcome.Started, started.Outcome);
        Assert.NotNull(started.Challenge);
        Assert.Equal(PairingStartOutcome.Resumed, resumed.Outcome);
        Assert.Null(resumed.Challenge);
        Assert.Equal(PairingStartOutcome.OtherDeviceActive, rejected.Outcome);
    }

    /// <summary>Verifies that pairing-code generation failure is reported without creating a challenge.</summary>
    [Fact]
    public void BeginPairing_CodeGenerationFails_ReturnsGeneratorFailure()
    {
        var coordinator = new PairingCoordinator(
            new FakeTrustStore(),
            new FakeClock(),
            pairingCodeGenerator: () => null);

        PairingStartResult result = coordinator.BeginPairing(ClientId.NewId());

        Assert.Equal(PairingStartOutcome.GeneratorFailed, result.Outcome);
        Assert.Null(result.Challenge);
    }

    /// <summary>Verifies that a correct code issues a pending credential without trusting it yet.</summary>
    [Fact]
    public async Task ConfirmCode_CorrectCode_IssuesPendingCredentialUntilCommit()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);

        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.CredentialIssued, issued.Outcome);
        Assert.Equal(32, issued.Credential!.Length);
        Assert.All(issued.Credential, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Empty(trustStore.List());
        PairingCommitResult committed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.Trusted, committed.Outcome);
        Assert.Equal(5, committed.ShortId!.Length);
        Assert.All(committed.ShortId, character => Assert.InRange(character, '0', '9'));
        TrustRecord record = trustStore.TryGet(clientId)!;
        Assert.Equal(KnownDeviceState.Trusted, record.State);
        Assert.Equal("Living Room PC", record.DisplayName);
        Assert.Equal(issued.DisplayName, committed.DisplayName);
        Assert.NotEqual(issued.Credential, record.CredentialVerifier);
    }

    /// <summary>
    /// Verifies that the correct code cannot be confirmed before its exact challenge's initial display
    /// has been committed, and that such an attempt leaves pacing, wrong-attempt, and cooldown state
    /// completely untouched -- unlike a genuinely wrong code, which does consume that state.
    /// </summary>
    [Fact]
    public void ConfirmCode_BeforeDisplayCommit_CorrectCode_IsInvalidAndDoesNotConsumeAttemptState()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(clientId);

        PairingConfirmationResult attempt = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.Invalid, attempt.Outcome);
        Assert.False(attempt.ShouldAutoRenotify);

        coordinator.CommitInitialDisplay(clientId, start.Challenge.Id);
        PairingConfirmationResult afterCommit = coordinator.ConfirmCode(clientId, start.Challenge.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.CredentialIssued, afterCommit.Outcome);
    }

    /// <summary>
    /// Verifies the same untouched-attempt-state guarantee as
    /// <see cref="ConfirmCode_BeforeDisplayCommit_CorrectCode_IsInvalidAndDoesNotConsumeAttemptState"/>
    /// for a wrong code submitted before display commit: five wrong attempts made before the commit
    /// leave the full wrong-attempt budget untouched, so the hard limit is still reached only after
    /// five genuine wrong attempts made afterward, not zero.
    /// </summary>
    [Fact]
    public void ConfirmCode_BeforeDisplayCommit_WrongCode_DoesNotConsumeWrongAttemptBudget()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(clientId);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            PairingConfirmationResult beforeCommit = coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
            Assert.Equal(PairingConfirmOutcome.Invalid, beforeCommit.Outcome);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PairingConfirmationResult result = default!;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            result = coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PairingConfirmOutcome.HardLimitReached, result.Outcome);
    }

    /// <summary>
    /// Verifies that a display commit for a stale, cancelled challenge can never protect a later
    /// challenge's confirm attempt: each challenge's display must be committed on its own exact
    /// identity, never inherited from a predecessor for the same client.
    /// </summary>
    [Fact]
    public void ConfirmCode_StaleDisplayCommitFromCancelledChallenge_DoesNotProtectReplacementChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult first = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, first.Challenge!.Id);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);

        PairingConfirmationResult attempt = coordinator.ConfirmCode(clientId, replacement.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.Invalid, attempt.Outcome);
    }

    /// <summary>Verifies that an invalid code is indistinguishable from a non-owner attempt and auto-renotify is bounded.</summary>
    [Fact]
    public void ConfirmCode_WrongCode_TracksAttemptsAndAutoRenotifyCooldown()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        BeginAndDisplayPairing(coordinator, clientId);

        PairingConfirmationResult first = coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
        clock.Advance(TimeSpan.FromSeconds(1));
        PairingConfirmationResult second = coordinator.ConfirmCode(clientId, "000000", "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.Invalid, first.Outcome);
        Assert.True(first.ShouldAutoRenotify);
        Assert.False(second.ShouldAutoRenotify);
    }

    /// <summary>Verifies that a correct code from another client neither consumes nor paces the owner's challenge.</summary>
    [Fact]
    public void ConfirmCode_CorrectCodeFromOtherClient_DoesNotConsumeChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);

        PairingConfirmationResult rejected = coordinator.ConfirmCode(other, start.Challenge!.Code, "Other");
        PairingConfirmationResult accepted = coordinator.ConfirmCode(owner, start.Challenge.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.Invalid, rejected.Outcome);
        Assert.Equal(PairingConfirmOutcome.CredentialIssued, accepted.Outcome);
    }

    /// <summary>Verifies that concurrent correct submissions issue only one pending credential.</summary>
    [Fact]
    public async Task ConfirmCode_ConcurrentCorrectSubmissions_OnlyOneSucceeds()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        string code = start.Challenge!.Code;

        PairingConfirmationResult[] results = await Task.WhenAll(
            Task.Run(() => coordinator.ConfirmCode(clientId, code, "Living Room PC")),
            Task.Run(() => coordinator.ConfirmCode(clientId, code, "Living Room PC")));

        Assert.Single(results, result => result.Outcome == PairingConfirmOutcome.CredentialIssued);
        Assert.Single(results, result => result.Outcome == PairingConfirmOutcome.Invalid);
    }

    /// <summary>Verifies that the fifth wrong evaluated code cancels the active challenge.</summary>
    [Fact]
    public void ConfirmCode_FifthWrongAttempt_ReachesHardLimitAndCancelsChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        BeginAndDisplayPairing(coordinator, clientId);

        PairingConfirmationResult result = default!;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            result = coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PairingConfirmOutcome.HardLimitReached, result.Outcome);
        Assert.Equal(PairingStartOutcome.Started, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that a confirmation submitted during pacing is not evaluated or counted.</summary>
    [Fact]
    public void ConfirmCode_TooSoon_ReturnsPacingLimitedWithoutConsumingChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);

        coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
        PairingConfirmationResult paced = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.PacingLimited, paced.Outcome);
        Assert.NotNull(paced.RetryAfter);
    }

    /// <summary>Verifies that a pending credential remains owned by its client and blocks other clients.</summary>
    [Fact]
    public void BeginPairing_WhileCredentialPending_IsOwnerBound()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);
        coordinator.ConfirmCode(owner, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(owner).Outcome);
        Assert.Equal(PairingStartOutcome.OtherDeviceActive, coordinator.BeginPairing(other).Outcome);
    }

    /// <summary>Verifies that a blocked device cannot start a new pairing challenge.</summary>
    [Fact]
    public void BeginPairing_BlockedDevice_IsRejected()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Blocked PC", KnownDeviceState.Blocked, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());

        PairingStartResult result = coordinator.BeginPairing(clientId);

        Assert.Equal(PairingStartOutcome.Blocked, result.Outcome);
        Assert.Null(result.Challenge);
    }

    /// <summary>Verifies that re-pairing preserves a known device's identity metadata.</summary>
    [Fact]
    public async Task CommitPending_ExistingRecord_PreservesShortIdAndPairedAt()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        DateTimeOffset pairedAt = DateTimeOffset.UtcNow.AddDays(-1);
        trustStore.Seed(new TrustRecord(clientId, "12345", "Old Name", KnownDeviceState.Revoked, "oldhash", pairedAt));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        TrustRecord updated = trustStore.TryGet(clientId)!;

        Assert.Equal(PairingCommitOutcome.Trusted, result.Outcome);
        Assert.Equal("12345", updated.ShortId);
        Assert.Equal(pairedAt, updated.PairedAtUtc);
        Assert.Equal("Old Name", updated.DisplayName);
    }

    /// <summary>Verifies that an expired challenge reports expiry and can be replaced by a fresh one.</summary>
    [Fact]
    public void BeginAndConfirm_AfterChallengeExpiry_StartsFreshAndReportsExpired()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult first = coordinator.BeginPairing(clientId);

        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        PairingConfirmationResult expired = coordinator.ConfirmCode(clientId, first.Challenge!.Code, "Living Room PC");
        PairingStartResult replacement = coordinator.BeginPairing(clientId);

        Assert.Equal(PairingConfirmOutcome.Expired, expired.Outcome);
        Assert.Equal(PairingStartOutcome.Started, replacement.Outcome);
    }

    /// <summary>Verifies that a challenge is still valid at its exact expiry instant.</summary>
    [Fact]
    public void ConfirmCode_AtExactChallengeExpiry_IsAccepted()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        clock.UtcNow = start.Challenge!.ExpiresAtUtc;

        PairingConfirmationResult result = coordinator.ConfirmCode(clientId, start.Challenge.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.CredentialIssued, result.Outcome);
    }

    /// <summary>Verifies that the pacing interval becomes available exactly at one second.</summary>
    [Fact]
    public void ConfirmCode_AtExactPacingBoundary_IsEvaluated()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        coordinator.ConfirmCode(clientId, "000000", "Living Room PC");
        clock.Advance(TimeSpan.FromSeconds(1));

        PairingConfirmationResult result = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingConfirmOutcome.CredentialIssued, result.Outcome);
    }

    /// <summary>Verifies that an owner can reconnect within ten seconds but loses the challenge afterward.</summary>
    [Fact]
    public void NotifyDisconnected_UsesReconnectGracePeriod()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        coordinator.BeginPairing(clientId);
        coordinator.NotifyDisconnected(clientId);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PairingStartOutcome.Started, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that reconnecting within grace keeps the owner's active challenge alive.</summary>
    [Fact]
    public void NotifyReconnected_WithinGrace_PreservesChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        coordinator.BeginPairing(clientId);
        coordinator.NotifyDisconnected(clientId);
        clock.Advance(TimeSpan.FromSeconds(5));

        coordinator.NotifyReconnected(clientId);
        clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that pending credentials expire and cannot be finalized after five minutes.</summary>
    [Fact]
    public async Task CommitPending_AfterPendingExpiry_ReturnsNotFound()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        clock.Advance(TimeSpan.FromMinutes(5));
        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, result.Outcome);
    }

    /// <summary>Verifies that persistence failure preserves the pending credential for retry.</summary>
    [Fact]
    public async Task CommitPending_PersistenceFailure_IsRetryable()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult failed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        trustStore.ThrowOnUpsert = null;
        PairingCommitResult retried = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PersistenceFailed, failed.Outcome);
        Assert.Equal(PairingCommitOutcome.Trusted, retried.Outcome);
    }

    /// <summary>
    /// Verifies that a thrown persistence exception releases the commit's claim rather than leaving
    /// the pending credential stuck uncancellable: unlike
    /// <see cref="CommitPending_PersistenceFailure_IsRetryable"/>'s retry path, this proves the client
    /// can instead choose to give up and cancel it normally afterward.
    /// </summary>
    [Fact]
    public async Task CommitPending_PersistenceFailure_ReleasesClaimSoCancelSucceeds()
    {
        var trustStore = new FakeTrustStore { ThrowOnUpsert = new IOException("disk full") };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult failed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PersistenceFailed, failed.Outcome);
        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>Verifies that credential-generation failure leaves the active challenge retryable.</summary>
    [Fact]
    public void ConfirmCode_CredentialGenerationFails_PreservesChallenge()
    {
        ClientId clientId = ClientId.NewId();
        var coordinator = new PairingCoordinator(
            new FakeTrustStore(),
            new FakeClock(),
            credentialGenerator: () => null);
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);

        PairingConfirmationResult failed = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        PairingStartResult resumed = coordinator.BeginPairing(clientId);

        Assert.Equal(PairingConfirmOutcome.GeneratorFailed, failed.Outcome);
        Assert.Equal(PairingStartOutcome.Resumed, resumed.Outcome);
    }

    /// <summary>Verifies that short-id generation failure leaves the pending credential retryable.</summary>
    [Fact]
    public async Task CommitPending_ShortIdGenerationFails_PreservesPendingCredential()
    {
        ClientId clientId = ClientId.NewId();
        var coordinator = new PairingCoordinator(
            new FakeTrustStore(),
            new FakeClock(),
            shortIdGenerator: () => null);
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult failed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.GeneratorFailed, failed.Outcome);
        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that an unexpected exception from pre-persistence work performed after the commit
    /// claim is acquired -- here, the short-id generator itself faulting rather than returning
    /// <see langword="null"/> -- still releases the claim, rather than only the already-handled
    /// generator-exhaustion branch doing so: the exception propagates to the caller, and the pending
    /// credential remains exactly as retryable and cancellable as before the claim was acquired.
    /// </summary>
    [Fact]
    public async Task CommitPending_ShortIdGeneratorThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential()
    {
        ClientId clientId = ClientId.NewId();
        var coordinator = new PairingCoordinator(
            new FakeTrustStore(),
            new FakeClock(),
            shortIdGenerator: () => throw new InvalidOperationException("Simulated generator fault."));
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitPendingAsync(clientId, issued.Credential!));

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
    }

    /// <summary>
    /// Verifies the same exception-safety guarantee as
    /// <see cref="CommitPending_ShortIdGeneratorThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential"/>
    /// for the other pre-persistence dependency this section performs after acquiring the claim: an
    /// unexpected exception from <see cref="ITrustStore.TryGet"/> itself.
    /// </summary>
    [Fact]
    public async Task CommitPending_TrustStoreTryGetThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential()
    {
        ClientId clientId = ClientId.NewId();
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        trustStore.ThrowOnTryGet = new InvalidOperationException("Simulated trust store fault.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitPendingAsync(clientId, issued.Credential!));

        trustStore.ThrowOnTryGet = null;
        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
    }

    /// <summary>
    /// Verifies the exception-safety guarantee for the section this fix newly protects: an unexpected
    /// fault in the post-claim record construction and verifier hashing that runs after the short-id is
    /// already resolved -- not a trust-store lookup or a short-id generator fault, which
    /// <see cref="CommitPending_TrustStoreTryGetThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential"/>
    /// and <see cref="CommitPending_ShortIdGeneratorThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential"/>
    /// already cover -- still releases the claim through the single ownership-tracking <c>finally</c>
    /// rather than a dedicated catch for this specific fault, proving the cleanup rule itself rather
    /// than only the previously-known fault sites.
    /// </summary>
    [Fact]
    public async Task CommitPending_VerifierHasherThrowsUnexpectedly_ReleasesClaimAndPreservesPendingCredential()
    {
        ClientId clientId = ClientId.NewId();
        var coordinator = new PairingCoordinator(
            new FakeTrustStore(),
            new FakeClock(),
            credentialVerifierHasher: _ => throw new InvalidOperationException("Simulated verifier-hashing fault."));
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CommitPendingAsync(clientId, issued.Credential!));

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
    }

    /// <summary>Verifies that a wrong client or credential cannot consume the pending credential.</summary>
    [Fact]
    public async Task CommitPending_WrongClientOrCredential_PreservesPendingCredential()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);
        PairingConfirmationResult issued = coordinator.ConfirmCode(owner, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult wrongClient = await coordinator.CommitPendingAsync(ClientId.NewId(), issued.Credential!);
        PairingCommitResult wrongCredential = await coordinator.CommitPendingAsync(owner, "wrong-credential");
        PairingCommitResult correct = await coordinator.CommitPendingAsync(owner, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, wrongClient.Outcome);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, wrongCredential.Outcome);
        Assert.Equal(PairingCommitOutcome.Trusted, correct.Outcome);
    }

    /// <summary>Verifies that repeating a successful finalization is an idempotent trusted result.</summary>
    [Fact]
    public async Task CommitPending_RepeatedCredential_IsIdempotentlyTrusted()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult first = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        PairingCommitResult second = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.Trusted, first.Outcome);
        Assert.Equal(PairingCommitOutcome.AlreadyTrusted, second.Outcome);
        Assert.Single(trustStore.List());
    }

    /// <summary>Verifies that exhausting short-id candidates fails closed without consuming pending pairing.</summary>
    [Fact]
    public async Task CommitPending_ShortIdCollisions_ReachBoundedGeneratorFailure()
    {
        var trustStore = new FakeTrustStore();
        trustStore.Seed(new TrustRecord(ClientId.NewId(), "12345", "Existing", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(
            trustStore,
            new FakeClock(),
            shortIdGenerator: () => "12345");
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.GeneratorFailed, result.Outcome);
        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
        // A generator failure never reaches persistence, so it must release its claim rather than
        // leave the pending credential stuck uncancellable: Cancel succeeds normally afterward.
        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
    }

    /// <summary>Verifies that cancellation before finalization leaves the pending credential retryable.</summary>
    [Fact]
    public async Task CommitPending_CancelledWait_DoesNotConsumePendingCredential()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.CommitPendingAsync(clientId, issued.Credential!, cancellation.Token));

        Assert.Equal(PairingCommitOutcome.Trusted,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies that cancellation requested while persistence is genuinely in flight -- after the
    /// pairing-operation lock has already been released, per the lock-boundary fix -- still propagates
    /// <see cref="OperationCanceledException"/> and leaves the pending credential retryable, the same
    /// contract <see cref="CommitPending_CancelledWait_DoesNotConsumePendingCredential"/> proves for
    /// cancellation requested before the call even starts.
    /// </summary>
    [Fact]
    public async Task CommitPending_CancelledDuringPersistenceAwait_PropagatesAndPreservesPendingCredential()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task.WaitAsync(cancellation.Token);
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!, cancellation.Token);
        await enteredUpsert.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commit);
        trustStore.BeforeConditionalUpsert = null;
        Assert.Equal(PairingCommitOutcome.Trusted,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies that a cancellation propagated during persistence releases the commit's claim rather
    /// than leaving the pending credential stuck uncancellable: unlike
    /// <see cref="CommitPending_CancelledDuringPersistenceAwait_PropagatesAndPreservesPendingCredential"/>'s
    /// retry path, this proves the client can instead choose to give up and cancel it normally
    /// afterward.
    /// </summary>
    [Fact]
    public async Task CommitPending_CancelledDuringPersistenceAwait_ReleasesClaimSoCancelSucceeds()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task.WaitAsync(cancellation.Token);
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!, cancellation.Token);
        await enteredUpsert.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commit);

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.CommitPendingAsync"/> does not hold the
    /// pairing-operation lock across its persistence await: <see cref="PairingCoordinator.CancelAll"/>
    /// completes immediately even while a commit's trust-store write is deliberately blocked, rather
    /// than being forced to wait behind it the way holding the lock across the await would require. The
    /// commit has already irrevocably claimed this exact reservation by the time the write is observed
    /// entered, so <see cref="PairingCoordinator.CancelAll"/> racing in cannot invalidate it: the commit
    /// still reports <see cref="PairingCommitOutcome.Trusted"/> once the write completes, closing the
    /// orphaned-Trusted residual a plain identity compare-and-swap could not.
    /// </summary>
    [Fact]
    public async Task CancelAll_DuringCommit_DoesNotInvalidateTheIrrevocablyCommittingReservation()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;
        Task cancel = Task.Run(coordinator.CancelAll);
        Task observationWindow = Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Same(cancel, await Task.WhenAny(cancel, observationWindow));

        releaseUpsert.SetResult();
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.Trusted, result.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies the same non-blocking, non-invalidating guarantee for the single-client
    /// <see cref="PairingCoordinator.Cancel"/> path -- the ordinary <c>pairing_cancel</c> route --
    /// symmetric with <see cref="CancelAll_DuringCommit_DoesNotInvalidateTheIrrevocablyCommittingReservation"/>.
    /// <see cref="PairingCoordinator.Cancel"/> itself reports <see cref="PairingCancelOutcome.AlreadyIdle"/>
    /// rather than falsely claiming it cancelled a credential that goes on to persist.
    /// </summary>
    [Fact]
    public async Task Cancel_DuringCommit_ReportsAlreadyIdleAndDoesNotInvalidateTheCommittingReservation()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;
        Task<PairingCancelOutcome> cancel = Task.Run(() => coordinator.Cancel(clientId));
        Task observationWindow = Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Same(cancel, await Task.WhenAny(cancel, observationWindow));

        releaseUpsert.SetResult();
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCancelOutcome.AlreadyIdle, await cancel);
        Assert.Equal(PairingCommitOutcome.Trusted, result.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies the other linearization order from
    /// <see cref="Cancel_DuringCommit_ReportsAlreadyIdleAndDoesNotInvalidateTheCommittingReservation"/>:
    /// when <see cref="PairingCoordinator.Cancel"/> clears the pending credential before a commit ever
    /// claims it, that commit must never reach persistence at all -- it reports
    /// <see cref="PairingCommitOutcome.PendingNotFound"/>, and no trust record is written.
    /// </summary>
    [Fact]
    public async Task Cancel_BeforeCommitClaims_PreventsCommitFromPersisting()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, result.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same before-the-claim linearization as
    /// <see cref="Cancel_BeforeCommitClaims_PreventsCommitFromPersisting"/> for the administrative
    /// <see cref="PairingCoordinator.CancelAll"/> path.
    /// </summary>
    [Fact]
    public async Task CancelAll_BeforeCommitClaims_PreventsCommitFromPersisting()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        coordinator.CancelAll();
        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, result.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the exact lock-boundary contract required of
    /// <see cref="PairingCoordinator.CommitPendingAsync"/>: <see cref="PairingCoordinator.NotifyDisconnected"/>
    /// for an entirely unrelated client completes immediately while a commit's persistence write is
    /// deliberately blocked -- proving the pairing-operation semaphore is never held across the
    /// persistence await, so a slow or blocked trust-store write can never synchronously stall
    /// unrelated pairing lifecycle work during connection teardown.
    /// </summary>
    [Fact]
    public async Task CommitPending_PersistenceAwait_DoesNotHoldPairingLifecycleLock()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId committingClient = ClientId.NewId();
        ClientId otherClient = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, committingClient);
        PairingConfirmationResult issued = coordinator.ConfirmCode(committingClient, start.Challenge!.Code, "Living Room PC");
        coordinator.BeginPairing(otherClient);

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(committingClient, issued.Credential!);
        await enteredUpsert.Task;
        Task disconnect = Task.Run(() => coordinator.NotifyDisconnected(otherClient));
        Task observationWindow = Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Same(disconnect, await Task.WhenAny(disconnect, observationWindow));

        releaseUpsert.SetResult();
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.Trusted, result.Outcome);
    }

    /// <summary>Verifies that global cancellation removes a pending credential before finalization.</summary>
    [Fact]
    public async Task CancelAll_PendingCredential_PreventsCommit()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        coordinator.CancelAll();

        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>Verifies that a trust mutation fences an already-issued pending credential.</summary>
    [Fact]
    public async Task CommitPending_AfterTrustMutation_ReturnsInvalidated()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        await trustStore.UpsertAsync(new TrustRecord(ClientId.NewId(), "12345", "Other", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
    }

    /// <summary>
    /// Verifies that a peek is owner-bound and, once it grants a reservation, never grants a second
    /// concurrent one: a repeated peek by the same owner while its own reservation is still
    /// outstanding reports <see cref="PairingRenotifyOutcome.AlreadyIdle"/> rather than also being
    /// granted a reservation -- the exclusivity guarantee that closes the race where two concurrent
    /// redisplay attempts could otherwise both reach the adapter before either commits its cooldown.
    /// </summary>
    [Fact]
    public void TryRenotify_IsOwnerBoundAndDoesNotGrantASecondConcurrentReservation()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);

        PairingRenotifyResult first = coordinator.TryRenotify(owner);
        PairingRenotifyResult second = coordinator.TryRenotify(owner);
        PairingRenotifyResult other = coordinator.TryRenotify(ClientId.NewId());

        Assert.Equal(PairingRenotifyOutcome.Renotified, first.Outcome);
        Assert.NotNull(first.ClaimId);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, second.Outcome);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, other.Outcome);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.RollbackRenotify"/> releases an outstanding
    /// reservation without touching the cooldown, so an immediate retry peek is granted a fresh
    /// reservation for the same still-active challenge rather than being told nothing is eligible.
    /// </summary>
    [Fact]
    public void RollbackRenotify_ReleasesReservationWithoutCooldown_ImmediateRetrySucceeds()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);
        PairingRenotifyResult first = coordinator.TryRenotify(owner);

        coordinator.RollbackRenotify(owner, first.ChallengeId!.Value, first.ClaimId!.Value);

        PairingRenotifyResult retry = coordinator.TryRenotify(owner);
        Assert.Equal(PairingRenotifyOutcome.Renotified, retry.Outcome);
        Assert.NotNull(retry.ClaimId);
        Assert.NotEqual(first.ClaimId, retry.ClaimId);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.RollbackRenotify"/> is a no-op for a stale
    /// reservation -- one already resolved, or belonging to a challenge since cancelled, expired, or
    /// replaced -- rather than disturbing whatever reservation (if any) actually exists now.
    /// </summary>
    [Fact]
    public void RollbackRenotify_StaleReservationAfterReplacement_DoesNotDisturbReplacementReservation()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingRenotifyResult stalePeek = coordinator.TryRenotify(clientId);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);
        PairingRenotifyResult replacementPeek = coordinator.TryRenotify(clientId);

        coordinator.RollbackRenotify(clientId, stalePeek.ChallengeId!.Value, stalePeek.ClaimId!.Value);

        Assert.Equal(
            PairingRenotifyOutcome.Renotified,
            coordinator.CommitRenotify(clientId, replacement.Challenge!.Id, replacementPeek.ClaimId!.Value).Outcome);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.RollbackRenotify"/> is owner-bound: a non-owner
    /// presenting another client's exact reservation identity cannot release it, symmetric with
    /// <see cref="CommitRenotify_NonOwner_ReturnsAlreadyIdle"/>.
    /// </summary>
    [Fact]
    public void RollbackRenotify_NonOwner_DoesNotReleaseReservation()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);
        PairingRenotifyResult peek = coordinator.TryRenotify(owner);

        coordinator.RollbackRenotify(ClientId.NewId(), peek.ChallengeId!.Value, peek.ClaimId!.Value);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, coordinator.TryRenotify(owner).Outcome);
    }

    /// <summary>
    /// Verifies that committing an already-resolved reservation a second time is a harmless
    /// <see cref="PairingRenotifyOutcome.AlreadyIdle"/> rather than re-applying the cooldown or
    /// disturbing a subsequently granted reservation.
    /// </summary>
    [Fact]
    public void CommitRenotify_CalledTwiceForTheSameClaim_SecondCallReportsAlreadyIdle()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);

        PairingRenotifyResult first = coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);
        PairingRenotifyResult second = coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);

        Assert.Equal(PairingRenotifyOutcome.Renotified, first.Outcome);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, second.Outcome);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that releasing an already-resolved reservation a second time is a harmless no-op
    /// rather than clearing a subsequently granted reservation out from under it.
    /// </summary>
    [Fact]
    public void RollbackRenotify_CalledTwiceForTheSameClaim_SecondCallDoesNotDisturbNewReservation()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingRenotifyResult firstPeek = coordinator.TryRenotify(clientId);
        coordinator.RollbackRenotify(clientId, firstPeek.ChallengeId!.Value, firstPeek.ClaimId!.Value);
        PairingRenotifyResult secondPeek = coordinator.TryRenotify(clientId);

        coordinator.RollbackRenotify(clientId, firstPeek.ChallengeId!.Value, firstPeek.ClaimId!.Value);

        Assert.Equal(
            PairingRenotifyOutcome.Renotified,
            coordinator.CommitRenotify(clientId, secondPeek.ChallengeId!.Value, secondPeek.ClaimId!.Value).Outcome);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.Cancel"/> clears an outstanding redisplay
    /// reservation the same way it clears the challenge itself: a stale commit or rollback for the
    /// cancelled reservation is a no-op, and a fresh challenge started afterward is fully
    /// renotify-eligible rather than permanently blocked by the abandoned reservation.
    /// </summary>
    [Fact]
    public void Cancel_WhileRenotifyReservationOutstanding_ClearsReservationAndLeavesFutureRenotifyUsable()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        coordinator.RollbackRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);

        PairingStartResult replacement = BeginAndDisplayPairing(coordinator, clientId);
        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.CancelAll"/> clears an outstanding redisplay
    /// reservation the same way <see cref="Cancel_WhileRenotifyReservationOutstanding_ClearsReservationAndLeavesFutureRenotifyUsable"/>
    /// proves for a targeted <see cref="PairingCoordinator.Cancel"/>: without this, the abandoned
    /// reservation would permanently block every future client's redisplay, since nothing else ever
    /// clears a reservation bound to a challenge <see cref="PairingCoordinator.CancelAll"/> already
    /// discarded.
    /// </summary>
    [Fact]
    public void CancelAll_WhileRenotifyReservationOutstanding_ClearsReservationAndLeavesFutureRenotifyUsable()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        BeginAndDisplayPairing(coordinator, clientId);
        coordinator.TryRenotify(clientId);

        coordinator.CancelAll();

        PairingStartResult replacement = BeginAndDisplayPairing(coordinator, clientId);
        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that a challenge expiring while its redisplay reservation is still outstanding clears
    /// the reservation the same way <see cref="Cancel_WhileRenotifyReservationOutstanding_ClearsReservationAndLeavesFutureRenotifyUsable"/>
    /// proves for an explicit cancel: a subsequent commit for the expired reservation reports
    /// <see cref="PairingRenotifyOutcome.AlreadyIdle"/> without consuming any cooldown.
    /// </summary>
    [Fact]
    public void TryRenotify_ChallengeExpiresWhileReservationOutstanding_CommitReportsAlreadyIdle()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);

        clock.Advance(Constants.PairingChallengeLifetime + TimeSpan.FromSeconds(1));

        PairingRenotifyResult commit = coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, commit.Outcome);
    }

    /// <summary>
    /// Verifies that a challenge reservation whose initial display has not yet committed is never
    /// renotify-eligible: it has never actually been shown to the client, so there is nothing yet to
    /// redisplay -- <see cref="PairingCoordinator.TryRenotify"/> must check display commitment, not
    /// just ownership.
    /// </summary>
    [Fact]
    public void TryRenotify_UncommittedReservation_ReportsAlreadyIdle()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        coordinator.BeginPairing(owner);

        PairingRenotifyResult result = coordinator.TryRenotify(owner);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, result.Outcome);
    }

    /// <summary>Verifies that committing a redisplay is allowed again exactly at the cooldown boundary.</summary>
    [Fact]
    public void CommitRenotify_AtExactCooldownBoundary_IsAllowed()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PairingRenotifyResult firstPeek = coordinator.TryRenotify(clientId);
        coordinator.CommitRenotify(clientId, firstPeek.ChallengeId!.Value, firstPeek.ClaimId!.Value);
        clock.Advance(TimeSpan.FromSeconds(5));

        PairingRenotifyResult secondPeek = coordinator.TryRenotify(clientId);
        Assert.Equal(
            PairingRenotifyOutcome.Renotified,
            coordinator.CommitRenotify(clientId, secondPeek.ChallengeId!.Value, secondPeek.ClaimId!.Value).Outcome);
    }

    /// <summary>Verifies that cancellation only clears the requesting client's pairing operation.</summary>
    [Fact]
    public void Cancel_IsOwnerBound()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        coordinator.BeginPairing(owner);

        Assert.Equal(PairingCancelOutcome.AlreadyIdle, coordinator.Cancel(ClientId.NewId()));
        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(owner));
        Assert.Equal(PairingStartOutcome.Started, coordinator.BeginPairing(owner).Outcome);
    }

    /// <summary>Verifies that cancelling a pending credential prevents finalization.</summary>
    [Fact]
    public async Task Cancel_PendingCredential_ClearsOnlyOwnedState()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);
        PairingConfirmationResult issued = coordinator.ConfirmCode(owner, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(owner));
        Assert.Equal(PairingCommitOutcome.PendingNotFound, (await coordinator.CommitPendingAsync(owner, issued.Credential!)).Outcome);
    }

    /// <summary>Verifies that oversized UTF-8 display names are rejected before a credential is issued.</summary>
    [Fact]
    public void ConfirmCode_DisplayNameOver64Utf8Bytes_Throws()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        string displayName = new('é', 33);

        Assert.Equal(66, Encoding.UTF8.GetByteCount(displayName));
        Assert.Throws<ArgumentException>(() => coordinator.ConfirmCode(clientId, start.Challenge!.Code, displayName));
    }

    /// <summary>Verifies that a display name exactly at the UTF-8 limit is accepted.</summary>
    [Fact]
    public void ConfirmCode_DisplayNameExactly64Utf8Bytes_IsAccepted()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        string displayName = new('é', 32);

        Assert.Equal(64, Encoding.UTF8.GetByteCount(displayName));
        Assert.Equal(PairingConfirmOutcome.CredentialIssued,
            coordinator.ConfirmCode(clientId, start.Challenge!.Code, displayName).Outcome);
    }

    /// <summary>Verifies that a present empty display name clears a prior name during re-pairing.</summary>
    [Fact]
    public async Task CommitPending_EmptyDisplayName_ClearsExistingName()
    {
        var trustStore = new FakeTrustStore();
        ClientId clientId = ClientId.NewId();
        trustStore.Seed(new TrustRecord(clientId, "12345", "Old Name", KnownDeviceState.Revoked, "oldhash", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, string.Empty);

        await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(string.Empty, trustStore.TryGet(clientId)!.DisplayName);
    }

    /// <summary>Verifies that control characters are rejected from presentation names.</summary>
    [Fact]
    public void ConfirmCode_DisplayNameWithControlCharacter_Throws()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);

        Assert.Throws<ArgumentException>(() => coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living\nRoom"));
    }

    /// <summary>Verifies that a missing pairing code is rejected explicitly.</summary>
    [Fact]
    public void ConfirmCode_NullCode_ThrowsArgumentNullException()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());

        Assert.Throws<ArgumentNullException>(() => coordinator.ConfirmCode(ClientId.NewId(), null!, null));
    }

    /// <summary>Verifies that global cancellation removes both active and pending pairing state.</summary>
    [Fact]
    public void CancelAll_RemovesActiveChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);

        coordinator.CancelAll();

        Assert.Equal(PairingStartOutcome.Started, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that committing a redisplay applies the cooldown, rate-limiting a further peek.</summary>
    [Fact]
    public void CommitRenotify_AppliesCooldownAndRateLimitsFurtherCommits()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);

        PairingRenotifyResult peek = coordinator.TryRenotify(owner);
        PairingRenotifyResult first = coordinator.CommitRenotify(owner, peek.ChallengeId!.Value, peek.ClaimId!.Value);
        PairingRenotifyResult cooldown = coordinator.TryRenotify(owner);

        Assert.Equal(PairingRenotifyOutcome.Renotified, first.Outcome);
        Assert.Equal(PairingRenotifyOutcome.Cooldown, cooldown.Outcome);
        Assert.NotNull(cooldown.RetryAfter);
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.CommitRenotify"/> re-validates current state rather
    /// than trusting an earlier peek: a challenge cancelled between the peek and the commit reports
    /// the fresh outcome instead of committing a cooldown against state that no longer exists.
    /// </summary>
    [Fact]
    public void CommitRenotify_ChallengeCancelledAfterPeek_ReportsFreshOutcome()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);
        PairingRenotifyResult peek = coordinator.TryRenotify(owner);
        coordinator.Cancel(owner);

        PairingRenotifyResult commit = coordinator.CommitRenotify(owner, peek.ChallengeId!.Value, peek.ClaimId!.Value);

        Assert.Equal(PairingRenotifyOutcome.Renotified, peek.Outcome);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, commit.Outcome);
    }

    /// <summary>Verifies that a client owning nothing at all reports Idle.</summary>
    [Fact]
    public void GetStatusSnapshot_NoOperation_ReportsIdle()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());

        PairingStatusSnapshot snapshot = coordinator.GetStatusSnapshot(ClientId.NewId());

        Assert.Equal(PairingStatusKind.Idle, snapshot.Kind);
        Assert.Null(snapshot.Challenge);
    }

    /// <summary>
    /// Verifies that a freshly reserved challenge whose initial display has not yet committed reports
    /// as an uncommitted reservation, never as a displayed challenge or a pending credential -- the
    /// exact ambiguity a nullable challenge lookup used to collapse.
    /// </summary>
    [Fact]
    public void GetStatusSnapshot_UncommittedReservation_ReportsUncommittedDisplayReservation()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        coordinator.BeginPairing(owner);

        PairingStatusSnapshot snapshot = coordinator.GetStatusSnapshot(owner);

        Assert.Equal(PairingStatusKind.UncommittedDisplayReservation, snapshot.Kind);
        Assert.Null(snapshot.Challenge);
    }

    /// <summary>Verifies that the owned-challenge snapshot returns the displayed challenge only for its actual owner, and OtherDeviceActive for anyone else.</summary>
    [Fact]
    public void GetStatusSnapshot_DisplayedChallenge_ReturnsChallengeOnlyForOwner()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);

        PairingStatusSnapshot ownerSnapshot = coordinator.GetStatusSnapshot(owner);
        PairingStatusSnapshot otherSnapshot = coordinator.GetStatusSnapshot(other);

        Assert.Equal(PairingStatusKind.DisplayedChallenge, ownerSnapshot.Kind);
        Assert.Equal(start.Challenge, ownerSnapshot.Challenge);
        Assert.Equal(PairingStatusKind.OtherDeviceActive, otherSnapshot.Kind);
        Assert.Null(otherSnapshot.Challenge);
    }

    /// <summary>
    /// Verifies that a client holding only a pending credential -- its code already consumed by a
    /// correct <see cref="PairingCoordinator.ConfirmCode"/> -- reports PendingCredential, not a
    /// displayed challenge.
    /// </summary>
    [Fact]
    public void GetStatusSnapshot_PendingCredentialOnly_ReportsPendingCredential()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, owner);
        coordinator.ConfirmCode(owner, start.Challenge!.Code, "Living Room PC");

        PairingStatusSnapshot ownerSnapshot = coordinator.GetStatusSnapshot(owner);
        PairingStatusSnapshot otherSnapshot = coordinator.GetStatusSnapshot(other);

        Assert.Equal(PairingStatusKind.PendingCredential, ownerSnapshot.Kind);
        Assert.Null(ownerSnapshot.Challenge);
        Assert.Equal(PairingStatusKind.OtherDeviceActive, otherSnapshot.Kind);
    }

    /// <summary>Verifies that an expired challenge is never reported as still owned.</summary>
    [Fact]
    public void GetStatusSnapshot_ExpiredChallenge_ReportsIdle()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId owner = ClientId.NewId();
        coordinator.BeginPairing(owner);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        PairingStatusSnapshot snapshot = coordinator.GetStatusSnapshot(owner);

        Assert.Equal(PairingStatusKind.Idle, snapshot.Kind);
    }

    /// <summary>Verifies that a disconnected owner still within reconnect grace keeps its displayed challenge reportable.</summary>
    [Fact]
    public void GetStatusSnapshot_DuringReconnectGrace_StillReturnsDisplayedChallenge()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);
        coordinator.NotifyDisconnected(owner);
        clock.Advance(TimeSpan.FromSeconds(9));

        PairingStatusSnapshot snapshot = coordinator.GetStatusSnapshot(owner);

        Assert.Equal(PairingStatusKind.DisplayedChallenge, snapshot.Kind);
        Assert.Equal(start.Challenge, snapshot.Challenge);
    }

    /// <summary>Verifies that a non-owner committing a redisplay it never peeked reports AlreadyIdle, symmetric with <see cref="TryRenotify_IsOwnerBoundAndDoesNotGrantASecondConcurrentReservation"/>.</summary>
    [Fact]
    public void CommitRenotify_NonOwner_ReturnsAlreadyIdle()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, coordinator.CommitRenotify(ClientId.NewId(), start.Challenge!.Id, default).Outcome);
    }

    /// <summary>Verifies that a peek honors the exact cooldown boundary the same way <see cref="CommitRenotify_AtExactCooldownBoundary_IsAllowed"/> proves for a commit.</summary>
    [Fact]
    public void TryRenotify_AtExactCooldownBoundary_ReportsRenotified()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);
        coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.TryRenotify(clientId).Outcome);
    }

    /// <summary>
    /// Verifies that a stale acknowledgement for a challenge already replaced can never commit the
    /// replacement challenge's initial display -- <see cref="PairingCoordinator.CommitInitialDisplay"/>
    /// must compare the exact challenge identity, not just <c>clientId</c> ownership.
    /// </summary>
    [Fact]
    public void CommitInitialDisplay_StaleChallengeId_DoesNotCommitReplacement()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult first = coordinator.BeginPairing(clientId);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);

        bool committed = coordinator.CommitInitialDisplay(clientId, first.Challenge!.Id);

        Assert.False(committed);
        Assert.Null(coordinator.GetStatusSnapshot(clientId).Challenge);
        Assert.True(coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id));
        Assert.Equal(replacement.Challenge, coordinator.GetStatusSnapshot(clientId).Challenge);
    }

    /// <summary>
    /// Verifies that a challenge's initial display cannot be committed by a client that does not own
    /// it, even when it presents that challenge's exact, correctly-guessed <see cref="ChallengeId"/> --
    /// symmetric with <see cref="CommitInitialDisplay_StaleChallengeId_DoesNotCommitReplacement"/>,
    /// which proves the opposite mismatch (right client, wrong challenge).
    /// </summary>
    [Fact]
    public void CommitInitialDisplay_WrongClientId_DoesNotCommitAnotherClientsChallenge()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        ClientId other = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);

        bool committed = coordinator.CommitInitialDisplay(other, start.Challenge!.Id);

        Assert.False(committed);
        Assert.Null(coordinator.GetStatusSnapshot(owner).Challenge);
        Assert.True(coordinator.CommitInitialDisplay(owner, start.Challenge.Id));
    }

    /// <summary>
    /// Verifies that a stale acknowledgement for a challenge that expired naturally -- not explicitly
    /// cancelled -- can never commit a fresh replacement challenge for the same client, the same way
    /// <see cref="CommitInitialDisplay_StaleChallengeId_DoesNotCommitReplacement"/> proves for an
    /// explicit cancellation.
    /// </summary>
    [Fact]
    public void CommitInitialDisplay_StaleChallengeIdAfterExpiry_DoesNotCommitReplacement()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult first = coordinator.BeginPairing(clientId);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        PairingStartResult replacement = coordinator.BeginPairing(clientId);

        bool committed = coordinator.CommitInitialDisplay(clientId, first.Challenge!.Id);

        Assert.False(committed);
        Assert.Null(coordinator.GetStatusSnapshot(clientId).Challenge);
        Assert.True(coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id));
        Assert.Equal(replacement.Challenge, coordinator.GetStatusSnapshot(clientId).Challenge);
    }

    /// <summary>
    /// Verifies that a stale rejection for a challenge already replaced can never roll back the
    /// replacement challenge -- <see cref="PairingCoordinator.RollbackInitialDisplay"/> must compare
    /// the exact challenge identity.
    /// </summary>
    [Fact]
    public void RollbackInitialDisplay_StaleChallengeId_LeavesReplacementUntouched()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult first = coordinator.BeginPairing(clientId);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);

        coordinator.RollbackInitialDisplay(clientId, first.Challenge!.Id);

        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
        Assert.True(coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id));
    }

    /// <summary>
    /// Verifies that a commit evaluated for a challenge already replaced can never consume the
    /// replacement challenge's cooldown -- <see cref="PairingCoordinator.CommitRenotify"/>'s identity
    /// check must run before any cooldown-eligibility evaluation.
    /// </summary>
    [Fact]
    public void CommitRenotify_IdentityMismatchAfterReplacement_DoesNotConsumeReplacementCooldown()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);

        PairingRenotifyResult stale = coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value, peek.ClaimId!.Value);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, stale.Outcome);
        PairingRenotifyResult replacementPeek = coordinator.TryRenotify(clientId);
        Assert.Equal(PairingRenotifyOutcome.Renotified, replacementPeek.Outcome);
        Assert.Equal(
            PairingRenotifyOutcome.Renotified,
            coordinator.CommitRenotify(clientId, replacement.Challenge!.Id, replacementPeek.ClaimId!.Value).Outcome);
    }

    /// <summary>
    /// Verifies that an administrative Revoke racing <see cref="PairingCoordinator.CommitPendingAsync"/>'s
    /// own trust-store persistence becomes authoritative. Revoke's underlying persistence write is held
    /// open with a gate, deterministically forcing the concurrently started commit to block
    /// on the real <see cref="TrustStore"/>'s own mutation lock rather than racing it via timing: releasing
    /// the gate lets Revoke's generation bump land first, so the commit's own conditional upsert observes a
    /// stale generation and reports <see cref="PairingCommitOutcome.PairingInvalidated"/> instead of
    /// <see cref="PairingCommitOutcome.Trusted"/>, and the credential the client already held can never
    /// later commit the record back to trusted.
    /// </summary>
    [Fact]
    public async Task CommitPending_RevokeDuringPersistence_CannotRestoreTrustedCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        await trustStore.UpsertAsync(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> revoke = trustStore.RevokeAsync(clientId);
        await enteredSave.Task;
        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        releaseSave.SetResult();

        Assert.Equal(TrustMutationOutcome.Changed, await revoke);
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        TrustRecord? final = trustStore.TryGet(clientId);
        Assert.NotNull(final);
        Assert.Equal(KnownDeviceState.Revoked, final!.State);
        Assert.Equal(string.Empty, final.CredentialVerifier);
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies the same authoritative-admin-mutation guarantee as
    /// <see cref="CommitPending_RevokeDuringPersistence_CannotRestoreTrustedCredential"/> for an
    /// administrative Block racing an in-flight commit.
    /// </summary>
    [Fact]
    public async Task CommitPending_BlockDuringPersistence_CannotRestoreTrustedCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        await trustStore.UpsertAsync(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<TrustMutationOutcome> block = trustStore.BlockAsync(clientId);
        await enteredSave.Task;
        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        releaseSave.SetResult();

        Assert.Equal(TrustMutationOutcome.Changed, await block);
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        TrustRecord? final = trustStore.TryGet(clientId);
        Assert.NotNull(final);
        Assert.Equal(KnownDeviceState.Blocked, final!.State);
        Assert.Equal(string.Empty, final.CredentialVerifier);
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies the same authoritative-admin-mutation guarantee as
    /// <see cref="CommitPending_RevokeDuringPersistence_CannotRestoreTrustedCredential"/> for an
    /// administrative Reset Trust racing an in-flight commit.
    /// </summary>
    [Fact]
    public async Task CommitPending_ResetTrustDuringPersistence_CannotRestoreTrustedCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        await trustStore.UpsertAsync(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<IReadOnlyList<ClientId>> resetTrust = trustStore.ResetTrustAsync();
        await enteredSave.Task;
        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        releaseSave.SetResult();

        Assert.Contains(clientId, await resetTrust);
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        TrustRecord? final = trustStore.TryGet(clientId);
        Assert.NotNull(final);
        Assert.Equal(KnownDeviceState.Revoked, final!.State);
        Assert.Equal(string.Empty, final.CredentialVerifier);
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Verifies the same authoritative-admin-mutation guarantee as
    /// <see cref="CommitPending_ResetTrustDuringPersistence_CannotRestoreTrustedCredential"/> for a
    /// first-time pairing, where Reset Trust has no currently trusted record to revoke at all -- the
    /// exact race <see cref="TrustStore.ResetTrustAsync"/>'s zero-affected fence advance exists to
    /// close. Before that fix, this pending credential's captured fence generation would have observed
    /// no movement and persisted regardless of the concurrent Reset Trust.
    /// </summary>
    [Fact]
    public async Task CommitPending_ResetTrustWithNoTrustedRecordsDuringPersistence_CannotRestoreTrustedCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        Assert.Empty(await trustStore.ResetTrustAsync());
        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same authoritative-admin-mutation guarantee as
    /// <see cref="CommitPending_RevokeDuringPersistence_CannotRestoreTrustedCredential"/> for the global
    /// Factory Reset racing an in-flight commit: no trust record survives or reappears afterward.
    /// </summary>
    [Fact]
    public async Task CommitPending_FactoryResetDuringPersistence_CannotRestoreTrustedCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        await trustStore.UpsertAsync(new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, null);

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task factoryReset = trustStore.ClearAsync();
        await enteredSave.Task;
        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        releaseSave.SetResult();

        await factoryReset;
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
        Assert.Equal(PairingCommitOutcome.PendingNotFound,
            (await coordinator.CommitPendingAsync(clientId, issued.Credential!)).Outcome);
    }

    /// <summary>
    /// Reproduces the duplicate-ACK race against the real <see cref="TrustStore"/> rather than
    /// <see cref="FakeTrustStore"/>: with ACK A's own conditional-upsert persistence write blocked,
    /// <see cref="TrustStore.TryGet"/> must still report no record for this client -- proving
    /// <see cref="TrustStore.TryUpsertIfGenerationAsync"/> never publishes the proposed Trusted record
    /// before its write durably succeeds -- so ACK B's own fallback lookup falls through to
    /// <see cref="PairingCommitOutcome.PendingNotFound"/> rather than incorrectly reporting
    /// <see cref="PairingCommitOutcome.AlreadyTrusted"/> for a credential that has not actually
    /// persisted yet. <see cref="FakeTrustStore"/>'s own gate already publishes only after its gate
    /// opens, so this class of race could only be exercised against the real store.
    /// </summary>
    [Fact]
    public async Task CommitPending_ConcurrentSecondAckAgainstRealTrustStore_NeverObservesTransientTrustedBeforePersistenceSucceeds()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredSave.Task;

        Assert.Null(trustStore.TryGet(clientId));
        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        releaseSave.SetResult();
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.Trusted, resultA.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies the same real-<see cref="TrustStore"/> visibility guarantee as
    /// <see cref="CommitPending_ConcurrentSecondAckAgainstRealTrustStore_NeverObservesTransientTrustedBeforePersistenceSucceeds"/>
    /// when ACK A's persistence ultimately fails: ACK B still never observes
    /// <see cref="PairingCommitOutcome.AlreadyTrusted"/>, and no Trusted record survives A's failure.
    /// </summary>
    [Fact]
    public async Task CommitPending_ConcurrentSecondAckAgainstRealTrustStore_PersistenceFails_NoTrustSurvivesAndSecondAckNeverPersisted()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        persistence.BeforeSave = async () =>
        {
            enteredSave.SetResult();
            await releaseSave.Task;
        };

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredSave.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        releaseSave.SetException(new IOException("disk full"));
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.PersistenceFailed, resultA.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies that an administrative Block attempt against an Unpaired known device -- not eligible
    /// for Block per the restored Stage 3.2 contract -- reports <see cref="TrustMutationOutcome.NotEligible"/>
    /// without advancing <see cref="ITrustStore.SecurityFenceGeneration"/>, so it can never invalidate a
    /// concurrently pending pairing credential the way a genuinely eligible Block (Trusted or Revoked)
    /// correctly does. Combines the Stage 3.2 Block-eligibility fix with the real <see cref="TrustStore"/>
    /// used elsewhere in this class, unlike <see cref="CommitPending_BlockDuringPersistence_CannotRestoreTrustedCredential"/>
    /// which exercises the eligible-and-invalidating case.
    /// </summary>
    [Fact]
    public async Task CommitPending_BlockAttemptAgainstUnpairedDeviceDuringPersistence_NeverInvalidatesPendingCredential()
    {
        ClientId clientId = ClientId.NewId();
        var persistence = new FakeTrustStorePersistence();
        TrustStore trustStore = await TrustStore.CreateAsync(persistence, new FakeClock());
        await trustStore.UpsertAsync(new TrustRecord(clientId, "12345", null, KnownDeviceState.Unpaired, string.Empty, DateTimeOffset.UtcNow));
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        long generationBeforeBlockAttempt = trustStore.SecurityFenceGeneration;

        Assert.Equal(TrustMutationOutcome.NotEligible, await trustStore.BlockAsync(clientId));
        Assert.Equal(generationBeforeBlockAttempt, trustStore.SecurityFenceGeneration);

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.Trusted, result.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies that a second concurrent <see cref="PairingCoordinator.CommitPendingAsync"/> call for
    /// the exact same pending credential can never also claim it while a first call's exclusive claim
    /// is still live: it observes <see cref="PairingCommitOutcome.PendingNotFound"/> without ever
    /// reaching persistence, and exactly one persistence attempt -- the first call's own -- ever
    /// succeeds. Closes the residual a bare claimed/unclaimed flag left open, where both calls could
    /// observe "unclaimed" and both proceed toward persistence for the same reservation.
    /// </summary>
    [Fact]
    public async Task CommitPending_ConcurrentSecondAck_NeverReachesPersistenceAndExactlyOnePersistenceAttemptSucceeds()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);
        Assert.Equal(0, trustStore.UpsertCallCount);

        releaseUpsert.SetResult();
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.Trusted, resultA.Outcome);
        Assert.Equal(1, trustStore.UpsertCallCount);
        Assert.Single(trustStore.List());
    }

    /// <summary>
    /// Reproduces the previously exploitable ordering the exclusive claim identity now closes: a
    /// second concurrent ACK arrives while the first already holds the exclusive claim, the first
    /// ACK's own persistence then fails, and <see cref="PairingCoordinator.Cancel"/> races in
    /// immediately afterward. Before this fix, releasing the first ACK's claim reset a shared boolean
    /// flag with no memory of which call had reclaimed it, so a second ACK could go on to persist a
    /// credential Cancel had already reported cancelled. With the claim identity, the second ACK never
    /// claimed anything in the first place -- it already observed <see cref="PairingCommitOutcome.PendingNotFound"/>
    /// before the first ACK even failed -- so Cancel's report and the trust store's actual state stay
    /// coherent, and no further attempt with this credential can ever persist it.
    /// </summary>
    [Fact]
    public async Task CommitPending_FirstAckFailsThenCancelRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        releaseUpsert.SetException(new InvalidOperationException("Simulated persistence failure."));
        PairingCommitResult resultA = await ackA;
        Assert.Equal(PairingCommitOutcome.PersistenceFailed, resultA.Outcome);

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        Assert.Null(trustStore.TryGet(clientId));

        PairingCommitResult retry = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, retry.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same previously exploitable ordering as
    /// <see cref="CommitPending_FirstAckFailsThenCancelRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist"/>
    /// for the first ACK's own <see cref="CancellationToken"/> being cancelled during persistence
    /// instead of persistence throwing.
    /// </summary>
    [Fact]
    public async Task CommitPending_FirstAckCancelledThenCancelRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task.WaitAsync(cancellation.Token);
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!, cancellation.Token);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ackA);

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));
        Assert.Null(trustStore.TryGet(clientId));

        PairingCommitResult retry = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, retry.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies that <see cref="PairingCoordinator.Cancel"/> landing before either of two concurrent
    /// <see cref="PairingCoordinator.CommitPendingAsync"/> calls claims the reservation prevents both
    /// from ever reaching persistence.
    /// </summary>
    [Fact]
    public async Task Cancel_BeforeEitherAckClaims_PreventsBothAcksFromPersisting()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Assert.Equal(PairingCancelOutcome.Cancelled, coordinator.Cancel(clientId));

        PairingCommitResult ackA = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackA.Outcome);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);
        Assert.Equal(0, trustStore.UpsertCallCount);
    }

    /// <summary>
    /// Verifies the same non-blocking, non-invalidating guarantee as
    /// <see cref="Cancel_DuringCommit_ReportsAlreadyIdleAndDoesNotInvalidateTheCommittingReservation"/>
    /// with a losing concurrent second ACK also present: <see cref="PairingCoordinator.Cancel"/> still
    /// reports <see cref="PairingCancelOutcome.AlreadyIdle"/> rather than falsely claiming it cancelled
    /// a credential the real claimant goes on to persist.
    /// </summary>
    [Fact]
    public async Task Cancel_AfterExclusiveClaimWithConcurrentSecondAck_ReportsAlreadyIdleAndClaimantStillReachesTrusted()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        Assert.Equal(PairingCancelOutcome.AlreadyIdle, coordinator.Cancel(clientId));

        releaseUpsert.SetResult();
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.Trusted, resultA.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies the same previously exploitable ordering as
    /// <see cref="CommitPending_FirstAckFailsThenCancelRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist"/>
    /// for the multi-client <see cref="PairingCoordinator.CancelAll"/> path.
    /// </summary>
    [Fact]
    public async Task CommitPending_FirstAckFailsThenCancelAllRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        releaseUpsert.SetException(new InvalidOperationException("Simulated persistence failure."));
        PairingCommitResult resultA = await ackA;
        Assert.Equal(PairingCommitOutcome.PersistenceFailed, resultA.Outcome);

        coordinator.CancelAll();
        Assert.Null(trustStore.TryGet(clientId));

        PairingCommitResult retry = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, retry.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same previously exploitable ordering as
    /// <see cref="CommitPending_FirstAckFailsThenCancelAllRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist"/>
    /// for the first ACK's own <see cref="CancellationToken"/> being cancelled during persistence
    /// instead of persistence throwing.
    /// </summary>
    [Fact]
    public async Task CommitPending_FirstAckCancelledThenCancelAllRaces_TrustStoreNeverBecomesTrustedAndSecondAckCannotPersist()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task.WaitAsync(cancellation.Token);
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!, cancellation.Token);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ackA);

        coordinator.CancelAll();
        Assert.Null(trustStore.TryGet(clientId));

        PairingCommitResult retry = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, retry.Outcome);
        Assert.Null(trustStore.TryGet(clientId));
    }

    /// <summary>
    /// Verifies the same guarantee as
    /// <see cref="Cancel_BeforeEitherAckClaims_PreventsBothAcksFromPersisting"/> for
    /// <see cref="PairingCoordinator.CancelAll"/>.
    /// </summary>
    [Fact]
    public async Task CancelAll_BeforeEitherAckClaims_PreventsBothAcksFromPersisting()
    {
        var trustStore = new FakeTrustStore();
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        coordinator.CancelAll();

        PairingCommitResult ackA = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackA.Outcome);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);
        Assert.Equal(0, trustStore.UpsertCallCount);
    }

    /// <summary>
    /// Verifies the same guarantee as
    /// <see cref="Cancel_AfterExclusiveClaimWithConcurrentSecondAck_ReportsAlreadyIdleAndClaimantStillReachesTrusted"/>
    /// for <see cref="PairingCoordinator.CancelAll"/>: it cannot undo an already-claimed reservation
    /// even with a losing concurrent second ACK also present, and the real claimant still reaches
    /// <see cref="PairingCommitOutcome.Trusted"/>.
    /// </summary>
    [Fact]
    public async Task CancelAll_AfterExclusiveClaimWithConcurrentSecondAck_ClaimantStillReachesTrusted()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var coordinator = new PairingCoordinator(trustStore, new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        PairingCommitResult ackB = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        Assert.Equal(PairingCommitOutcome.PendingNotFound, ackB.Outcome);

        coordinator.CancelAll();

        releaseUpsert.SetResult();
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.Trusted, resultA.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies that a claimed reservation's five-minute finalization lifetime expiring while its
    /// commit is still in flight never clears it out from under the claimant: pending expiry must
    /// defer to a live commit claim the same way <see cref="PairingCoordinator.Cancel"/> and
    /// <see cref="PairingCoordinator.CancelAll"/> already do, so a slow persistence write outliving the
    /// pending lifetime still finalizes to <see cref="PairingCommitOutcome.Trusted"/> rather than
    /// racing an expiry that silently discards the reservation mid-commit.
    /// </summary>
    [Fact]
    public async Task CommitPending_PendingExpiresWhileClaimIsLive_DoesNotClearTheCommittingReservationAndClaimantStillReachesTrusted()
    {
        var enteredUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustStore = new FakeTrustStore
        {
            BeforeConditionalUpsert = async () =>
            {
                enteredUpsert.SetResult();
                await releaseUpsert.Task;
            },
        };
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(trustStore, clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = BeginAndDisplayPairing(coordinator, clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> ackA = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;

        clock.Advance(Constants.PairingPendingCredentialLifetime + TimeSpan.FromSeconds(1));

        // GetStatusSnapshot runs the same pending-expiry check every other entry point does; it must
        // still report the reservation as owned by this client rather than silently discarding it.
        PairingStatusSnapshot snapshotWhileClaimed = coordinator.GetStatusSnapshot(clientId);
        Assert.Equal(PairingStatusKind.PendingCredential, snapshotWhileClaimed.Kind);

        releaseUpsert.SetResult();
        PairingCommitResult resultA = await ackA;

        Assert.Equal(PairingCommitOutcome.Trusted, resultA.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Begins pairing and immediately commits its initial display, for the common case where a test
    /// needs a challenge <see cref="PairingCoordinator.ConfirmCode"/> will actually accept: a code
    /// cannot be confirmed before its exact challenge's display has been committed.
    /// </summary>
    /// <param name="coordinator">The coordinator to begin pairing on.</param>
    /// <param name="clientId">The client beginning pairing.</param>
    /// <returns>The started-pairing result, with its challenge's display already committed.</returns>
    private static PairingStartResult BeginAndDisplayPairing(PairingCoordinator coordinator, ClientId clientId)
    {
        PairingStartResult start = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        return start;
    }
}
