using System.Text;
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
        PairingStartResult start = coordinator.BeginPairing(clientId);

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

    /// <summary>Verifies that an invalid code is indistinguishable from a non-owner attempt and auto-renotify is bounded.</summary>
    [Fact]
    public void ConfirmCode_WrongCode_TracksAttemptsAndAutoRenotifyCooldown()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        coordinator.BeginPairing(clientId);

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
        PairingStartResult start = coordinator.BeginPairing(owner);

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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        coordinator.BeginPairing(clientId);

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
        PairingStartResult start = coordinator.BeginPairing(clientId);

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
        PairingStartResult start = coordinator.BeginPairing(owner);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult failed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);
        trustStore.ThrowOnUpsert = null;
        PairingCommitResult retried = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PersistenceFailed, failed.Outcome);
        Assert.Equal(PairingCommitOutcome.Trusted, retried.Outcome);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);

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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult failed = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.GeneratorFailed, failed.Outcome);
        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that a wrong client or credential cannot consume the pending credential.</summary>
    [Fact]
    public async Task CommitPending_WrongClientOrCredential_PreservesPendingCredential()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.GeneratorFailed, result.Outcome);
        Assert.Equal(PairingStartOutcome.Resumed, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that cancellation before finalization leaves the pending credential retryable.</summary>
    [Fact]
    public async Task CommitPending_CancelledWait_DoesNotConsumePendingCredential()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
    /// Verifies that <see cref="PairingCoordinator.CommitPendingAsync"/> does not hold the
    /// pairing-operation lock across its persistence await: <see cref="PairingCoordinator.CancelAll"/>
    /// completes immediately even while a commit's trust-store write is deliberately blocked, rather
    /// than being forced to wait behind it the way holding the lock across the await would require.
    /// Cancellation still wins what is reported, per <see cref="PairingCoordinator.CommitPendingAsync"/>'s
    /// own remarks: the commit reports <see cref="PairingCommitOutcome.PairingInvalidated"/> rather than
    /// <see cref="PairingCommitOutcome.Trusted"/>, even though the blocked write still lands durably once
    /// released -- the one documented residual case a non-blocking cancellation cannot close.
    /// </summary>
    [Fact]
    public async Task CancelAll_DuringCommit_ProceedsWithoutWaitingForPersistenceAndCommitReportsInvalidated()
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;
        Task cancel = Task.Run(coordinator.CancelAll);
        Task observationWindow = Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Same(cancel, await Task.WhenAny(cancel, observationWindow));

        releaseUpsert.SetResult();
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        // Documented residual: the write already landed durably before CancelAll's clear was observed.
        // It is orphaned, not a security exposure -- nothing else can present the discarded credential,
        // and the client's own next pairing attempt overwrites this exact record.
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies the same cancellation-wins guarantee for the single-client <see cref="PairingCoordinator.Cancel"/>
    /// path -- the ordinary <c>pairing_cancel</c> route, which unlike administrative invalidation
    /// carries no trust-store mutation-generation fence of its own -- symmetric with
    /// <see cref="CancelAll_DuringCommit_ProceedsWithoutWaitingForPersistenceAndCommitReportsInvalidated"/>.
    /// </summary>
    [Fact]
    public async Task Cancel_DuringCommit_ProceedsWithoutWaitingForPersistenceAndCommitReportsInvalidated()
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");

        Task<PairingCommitResult> commit = coordinator.CommitPendingAsync(clientId, issued.Credential!);
        await enteredUpsert.Task;
        Task cancel = Task.Run(() => coordinator.Cancel(clientId));
        Task observationWindow = Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Same(cancel, await Task.WhenAny(cancel, observationWindow));

        releaseUpsert.SetResult();
        PairingCommitResult result = await commit;

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
        Assert.Equal(KnownDeviceState.Trusted, trustStore.TryGet(clientId)!.State);
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
        PairingStartResult start = coordinator.BeginPairing(committingClient);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        PairingConfirmationResult issued = coordinator.ConfirmCode(clientId, start.Challenge!.Code, "Living Room PC");
        await trustStore.UpsertAsync(new TrustRecord(ClientId.NewId(), "12345", "Other", KnownDeviceState.Trusted, "deadbeef", DateTimeOffset.UtcNow));

        PairingCommitResult result = await coordinator.CommitPendingAsync(clientId, issued.Credential!);

        Assert.Equal(PairingCommitOutcome.PairingInvalidated, result.Outcome);
    }

    /// <summary>
    /// Verifies that a peek is owner-bound and never itself commits anything: repeating it while
    /// still eligible keeps reporting <see cref="PairingRenotifyOutcome.Renotified"/> rather than
    /// falling into a cooldown it never actually applied.
    /// </summary>
    [Fact]
    public void TryRenotify_IsOwnerBoundAndDoesNotCommitCooldown()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);

        PairingRenotifyResult first = coordinator.TryRenotify(owner);
        PairingRenotifyResult second = coordinator.TryRenotify(owner);
        PairingRenotifyResult other = coordinator.TryRenotify(ClientId.NewId());

        Assert.Equal(PairingRenotifyOutcome.Renotified, first.Outcome);
        Assert.Equal(PairingRenotifyOutcome.Renotified, second.Outcome);
        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, other.Outcome);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        coordinator.CommitRenotify(clientId, start.Challenge!.Id);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.CommitRenotify(clientId, start.Challenge.Id).Outcome);
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
        PairingStartResult start = coordinator.BeginPairing(owner);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);

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
        PairingStartResult start = coordinator.BeginPairing(clientId);

        coordinator.CancelAll();

        Assert.Equal(PairingStartOutcome.Started, coordinator.BeginPairing(clientId).Outcome);
    }

    /// <summary>Verifies that committing a redisplay applies the cooldown, rate-limiting a further commit.</summary>
    [Fact]
    public void CommitRenotify_AppliesCooldownAndRateLimitsFurtherCommits()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);
        coordinator.CommitInitialDisplay(owner, start.Challenge!.Id);

        PairingRenotifyResult first = coordinator.CommitRenotify(owner, start.Challenge!.Id);
        PairingRenotifyResult cooldown = coordinator.CommitRenotify(owner, start.Challenge.Id);
        PairingRenotifyResult peekDuringCooldown = coordinator.TryRenotify(owner);

        Assert.Equal(PairingRenotifyOutcome.Renotified, first.Outcome);
        Assert.Equal(PairingRenotifyOutcome.Cooldown, cooldown.Outcome);
        Assert.NotNull(cooldown.RetryAfter);
        Assert.Equal(PairingRenotifyOutcome.Cooldown, peekDuringCooldown.Outcome);
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

        PairingRenotifyResult commit = coordinator.CommitRenotify(owner, peek.ChallengeId!.Value);

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
        PairingStartResult start = coordinator.BeginPairing(owner);
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

    /// <summary>Verifies that a non-owner committing a redisplay it never peeked reports AlreadyIdle, symmetric with <see cref="TryRenotify_IsOwnerBoundAndDoesNotCommitCooldown"/>.</summary>
    [Fact]
    public void CommitRenotify_NonOwner_ReturnsAlreadyIdle()
    {
        var coordinator = new PairingCoordinator(new FakeTrustStore(), new FakeClock());
        ClientId owner = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(owner);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, coordinator.CommitRenotify(ClientId.NewId(), start.Challenge!.Id).Outcome);
    }

    /// <summary>Verifies that a peek honors the exact cooldown boundary the same way <see cref="CommitRenotify_AtExactCooldownBoundary_IsAllowed"/> proves for a commit.</summary>
    [Fact]
    public void TryRenotify_AtExactCooldownBoundary_ReportsRenotified()
    {
        var clock = new FakeClock();
        var coordinator = new PairingCoordinator(new FakeTrustStore(), clock);
        ClientId clientId = ClientId.NewId();
        PairingStartResult start = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        coordinator.CommitRenotify(clientId, start.Challenge!.Id);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, start.Challenge!.Id);
        PairingRenotifyResult peek = coordinator.TryRenotify(clientId);
        coordinator.Cancel(clientId);
        PairingStartResult replacement = coordinator.BeginPairing(clientId);
        coordinator.CommitInitialDisplay(clientId, replacement.Challenge!.Id);

        PairingRenotifyResult stale = coordinator.CommitRenotify(clientId, peek.ChallengeId!.Value);

        Assert.Equal(PairingRenotifyOutcome.AlreadyIdle, stale.Outcome);
        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.TryRenotify(clientId).Outcome);
        Assert.Equal(PairingRenotifyOutcome.Renotified, coordinator.CommitRenotify(clientId, replacement.Challenge!.Id).Outcome);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
        PairingStartResult start = coordinator.BeginPairing(clientId);
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
}
