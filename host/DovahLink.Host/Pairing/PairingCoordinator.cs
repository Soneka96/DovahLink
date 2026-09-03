using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Pairing;

/// <summary>
/// Owns the host's client-bound pairing challenge and pending-credential lifecycle. It keeps the
/// challenge in memory, fences finalization against trust mutations, and never exposes a pending
/// credential as trusted before the client completes final confirmation.
/// </summary>
public interface IPairingCoordinator
{
    /// <summary>Begins or resumes the pairing operation for one client.</summary>
    /// <param name="clientId">The client requesting ownership of pairing.</param>
    PairingStartResult BeginPairing(ClientId clientId);

    /// <summary>Evaluates a client's submitted pairing code.</summary>
    /// <param name="clientId">The client bound to the request.</param>
    /// <param name="code">The six-digit code to evaluate.</param>
    /// <param name="displayName">The optional presentation label for the device.</param>
    PairingConfirmationResult ConfirmCode(ClientId clientId, string code, string? displayName);

    /// <summary>Finalizes a matching pending credential after the client durably saved it.</summary>
    /// <param name="clientId">The client that owns the pending credential.</param>
    /// <param name="credential">The raw credential echoed by the client.</param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    Task<PairingCommitResult> CommitPendingAsync(
        ClientId clientId,
        string credential,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a redisplay request for <paramref name="clientId"/>'s owned active challenge without
    /// committing the manual-redisplay cooldown. A caller that intends to actually redisplay the code
    /// must await its own adapter notification outside any lock this coordinator holds, then call
    /// <see cref="CommitRenotify"/> only once that notification is accepted -- this method alone never
    /// changes coordinator state.
    /// </summary>
    /// <param name="clientId">The client requesting redisplay.</param>
    /// <returns>
    /// <see cref="PairingRenotifyOutcome.Renotified"/> when <paramref name="clientId"/> owns an active
    /// challenge outside cooldown and redisplay may proceed; <see cref="PairingRenotifyOutcome.Cooldown"/>
    /// with the remaining wait while the cooldown is still active; <see cref="PairingRenotifyOutcome.AlreadyIdle"/>
    /// when <paramref name="clientId"/> owns no active challenge.
    /// </returns>
    PairingRenotifyResult TryRenotify(ClientId clientId);

    /// <summary>
    /// Commits a challenge reservation's initial display once the caller's adapter notification has
    /// been accepted, marking it publicly resumable/displayed for the first time. Re-validates that
    /// <paramref name="challengeId"/> is still the current active challenge owned by
    /// <paramref name="clientId"/> rather than trusting the caller's earlier <see cref="BeginPairing"/>
    /// result: a stale acknowledgement for a challenge already replaced, cancelled, or expired must
    /// never mark a later, unrelated challenge as displayed.
    /// </summary>
    /// <param name="clientId">The client that reserved the challenge.</param>
    /// <param name="challengeId">The exact challenge identity <see cref="BeginPairing"/> returned.</param>
    /// <returns>
    /// <see langword="true"/> once <paramref name="challengeId"/> is committed as displayed;
    /// <see langword="false"/> without any state change if it no longer matches the current challenge.
    /// </returns>
    bool CommitInitialDisplay(ClientId clientId, ChallengeId challengeId);

    /// <summary>
    /// Rolls back a challenge reservation whose initial display the adapter rejected or timed out on,
    /// clearing it so no client is ever told a code is available that was never actually shown.
    /// Identity-checked the same way as <see cref="CommitInitialDisplay"/>: a stale rejection for a
    /// challenge already replaced, cancelled, or expired must never clear a later, unrelated challenge.
    /// </summary>
    /// <param name="clientId">The client that reserved the challenge.</param>
    /// <param name="challengeId">The exact challenge identity <see cref="BeginPairing"/> returned.</param>
    void RollbackInitialDisplay(ClientId clientId, ChallengeId challengeId);

    /// <summary>Cancels the pairing operation owned by one client.</summary>
    /// <param name="clientId">The client giving up its pairing operation.</param>
    PairingCancelOutcome Cancel(ClientId clientId);

    /// <summary>Records that the owner's connection disconnected.</summary>
    /// <param name="clientId">The disconnected client.</param>
    void NotifyDisconnected(ClientId clientId);

    /// <summary>Records that the owner's connection reconnected.</summary>
    /// <param name="clientId">The reconnected client.</param>
    void NotifyReconnected(ClientId clientId);

    /// <summary>Cancels every active challenge and pending credential.</summary>
    void CancelAll();

    /// <summary>
    /// Commits a redisplay <see cref="TryRenotify"/> found eligible, applying the manual-redisplay
    /// cooldown only now that the caller's adapter notification succeeded. Re-validates both ownership
    /// and the exact challenge identity from current state rather than trusting the earlier
    /// <see cref="TryRenotify"/> result: if the challenge was cancelled, replaced, expired, or its
    /// cooldown was otherwise already consumed between the two calls, this reports that fresh outcome
    /// instead of committing a cooldown against state that no longer matches it.
    /// </summary>
    /// <param name="clientId">The client committing its previously evaluated redisplay.</param>
    /// <param name="challengeId">
    /// The exact challenge identity <see cref="TryRenotify"/> returned alongside
    /// <see cref="PairingRenotifyOutcome.Renotified"/>.
    /// </param>
    /// <returns>
    /// <see cref="PairingRenotifyOutcome.Renotified"/> once the cooldown is committed against this
    /// exact challenge; <see cref="PairingRenotifyOutcome.AlreadyIdle"/> without any state change if
    /// <paramref name="challengeId"/> no longer matches the current active challenge -- including when
    /// a replacement challenge for the same <paramref name="clientId"/> is now active; otherwise the
    /// same outcome shape <see cref="TryRenotify"/> would report for the current state, uncommitted.
    /// </returns>
    PairingRenotifyResult CommitRenotify(ClientId clientId, ChallengeId challengeId);

    /// <summary>
    /// Returns <paramref name="clientId"/>'s still-active, already display-committed challenge,
    /// without generating, displaying, or otherwise mutating anything. Used to obtain the code for an
    /// adapter notification -- manual or automatic redisplay -- without exposing it through any public
    /// outcome.
    /// </summary>
    /// <param name="clientId">The client whose ownership to check.</param>
    /// <returns>
    /// The active challenge if <paramref name="clientId"/> owns it, it has not expired, and
    /// <see cref="CommitInitialDisplay"/> has already marked it displayed; otherwise
    /// <see langword="null"/> -- <paramref name="clientId"/> may still separately own a pending
    /// credential, which has no code left to redisplay, or an as-yet-uncommitted reservation, which
    /// must not be treated as publicly resumable/displayed until its initial display commits.
    /// </returns>
    PairingChallenge? TryGetOwnedChallenge(ClientId clientId);
}

/// <inheritdoc cref="IPairingCoordinator"/>
public sealed class PairingCoordinator : IPairingCoordinator
{
    /// <summary>The trust store used to fence and persist pairing mutations.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>The time source used for challenge, pacing, and pending expiry.</summary>
    private readonly IClock clock;

    /// <summary>Guards all in-memory pairing state.</summary>
    private readonly object gate = new();

    /// <summary>Serializes every pairing operation, including cancellation and finalization.</summary>
    private readonly SemaphoreSlim operationSemaphore = new(1, 1);

    /// <summary>Produces a new pairing code, or null when secure generation fails.</summary>
    private readonly Func<string?> pairingCodeGenerator;

    /// <summary>Produces a new credential, or null when secure generation fails.</summary>
    private readonly Func<string?> credentialGenerator;

    /// <summary>Produces one short-id candidate, or null when secure generation fails.</summary>
    private readonly Func<string?> shortIdGenerator;

    /// <summary>The active client-bound challenge, or <see langword="null"/>.</summary>
    private PairingChallenge? activeChallenge;

    /// <summary>
    /// The identity of <see cref="activeChallenge"/> once <see cref="CommitInitialDisplay"/> has
    /// accepted it, or <see langword="null"/> while it is still an unacknowledged reservation. Gates
    /// <see cref="TryGetOwnedChallenge"/>: a reservation must not be publicly resumable or displayed
    /// before its initial display commits.
    /// </summary>
    private ChallengeId? committedDisplayChallengeId;

    /// <summary>The disconnect time for the active challenge owner, when grace is running.</summary>
    private DateTimeOffset? disconnectedAtUtc;

    /// <summary>The number of wrong evaluated codes for the active challenge.</summary>
    private int wrongAttempts;

    /// <summary>The instant of the most recent evaluated code.</summary>
    private DateTimeOffset? lastConfirmAttemptUtc;

    /// <summary>The next instant at which manual code redisplay is allowed.</summary>
    private DateTimeOffset? renotifyCooldownUntilUtc;

    /// <summary>The next instant at which automatic wrong-code redisplay is allowed.</summary>
    private DateTimeOffset? autoRenotifyCooldownUntilUtc;

    /// <summary>The pending credential waiting for client final confirmation.</summary>
    private PendingCredential? pendingCredential;

    /// <summary>Creates a pairing coordinator.</summary>
    /// <param name="trustStore">The store used for mutation fencing and final persistence.</param>
    /// <param name="clock">The time source used by pairing expiry rules.</param>
    /// <param name="pairingCodeGenerator">Optional deterministic pairing-code generator.</param>
    /// <param name="credentialGenerator">Optional deterministic credential generator.</param>
    /// <param name="shortIdGenerator">Optional deterministic short-id candidate generator.</param>
    public PairingCoordinator(
        ITrustStore trustStore,
        IClock clock,
        Func<string?>? pairingCodeGenerator = null,
        Func<string?>? credentialGenerator = null,
        Func<string?>? shortIdGenerator = null)
    {
        this.trustStore = trustStore;
        this.clock = clock;
        this.pairingCodeGenerator = pairingCodeGenerator ?? GeneratePairingCode;
        this.credentialGenerator = credentialGenerator ?? GenerateCredential;
        this.shortIdGenerator = shortIdGenerator ?? GenerateShortIdCandidate;
    }

    /// <inheritdoc/>
    public PairingStartResult BeginPairing(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                ExpirePendingIfNeeded(now);

                if (trustStore.TryGet(clientId)?.State == KnownDeviceState.Blocked)
                {
                    return new PairingStartResult(PairingStartOutcome.Blocked, null);
                }

                if (pendingCredential is { } pending)
                {
                    return new PairingStartResult(
                        pending.ClientId == clientId ? PairingStartOutcome.Resumed : PairingStartOutcome.OtherDeviceActive,
                        null);
                }

                if (activeChallenge is { } existing)
                {
                    return new PairingStartResult(
                        existing.OwnerClientId == clientId ? PairingStartOutcome.Resumed : PairingStartOutcome.OtherDeviceActive,
                        null);
                }

                string? code = pairingCodeGenerator();
                if (code is null)
                {
                    return new PairingStartResult(PairingStartOutcome.GeneratorFailed, null);
                }
                var challenge = new PairingChallenge(ChallengeId.NewId(), clientId, code, now + Constants.PairingChallengeLifetime);
                activeChallenge = challenge;
                committedDisplayChallengeId = null;
                wrongAttempts = 0;
                lastConfirmAttemptUtc = null;
                renotifyCooldownUntilUtc = null;
                autoRenotifyCooldownUntilUtc = null;
                disconnectedAtUtc = null;
                return new PairingStartResult(PairingStartOutcome.Started, challenge);
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public bool CommitInitialDisplay(ClientId clientId, ChallengeId challengeId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                if (activeChallenge is not { } challenge || challenge.OwnerClientId != clientId || challenge.Id != challengeId)
                {
                    return false;
                }

                committedDisplayChallengeId = challengeId;
                return true;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void RollbackInitialDisplay(ClientId clientId, ChallengeId challengeId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                if (activeChallenge is { } challenge && challenge.OwnerClientId == clientId && challenge.Id == challengeId)
                {
                    ClearChallenge();
                }
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingConfirmationResult ConfirmCode(ClientId clientId, string code, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (displayName is not null && Encoding.UTF8.GetByteCount(displayName) > Constants.MaxDisplayNameLengthBytes)
        {
            throw new ArgumentException("The display name exceeds the 64-byte limit.", nameof(displayName));
        }
        if (displayName is not null && displayName.Any(char.IsControl))
        {
            throw new ArgumentException("The display name contains a control character.", nameof(displayName));
        }

        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireDisconnectedChallengeIfNeeded(now);
                if (activeChallenge is not { } challenge)
                {
                    return new PairingConfirmationResult(PairingConfirmOutcome.Invalid, null, null);
                }
                if (challenge.OwnerClientId != clientId)
                {
                    return new PairingConfirmationResult(PairingConfirmOutcome.Invalid, null, null);
                }
                if (now > challenge.ExpiresAtUtc)
                {
                    ClearChallenge();
                    return new PairingConfirmationResult(PairingConfirmOutcome.Expired, null, null);
                }
                if (lastConfirmAttemptUtc is { } lastAttempt && now - lastAttempt < Constants.PairingConfirmPacingInterval)
                {
                    return new PairingConfirmationResult(
                        PairingConfirmOutcome.PacingLimited,
                        null,
                        null,
                        RetryAfter: Constants.PairingConfirmPacingInterval - (now - lastAttempt));
                }

                lastConfirmAttemptUtc = now;
                if (!CredentialHasher.FixedTimeEquals(challenge.Code, code))
                {
                    wrongAttempts++;
                    if (wrongAttempts >= Constants.PairingMaxWrongAttempts)
                    {
                        ClearChallenge();
                        return new PairingConfirmationResult(PairingConfirmOutcome.HardLimitReached, null, null);
                    }

                    bool shouldAutoRenotify = autoRenotifyCooldownUntilUtc is null || now >= autoRenotifyCooldownUntilUtc;
                    if (shouldAutoRenotify)
                    {
                        autoRenotifyCooldownUntilUtc = now + Constants.PairingRenotifyCooldown;
                    }
                    return new PairingConfirmationResult(
                        PairingConfirmOutcome.Invalid,
                        null,
                        null,
                        ShouldAutoRenotify: shouldAutoRenotify);
                }

                string? credential = credentialGenerator();
                if (credential is null)
                {
                    return new PairingConfirmationResult(PairingConfirmOutcome.GeneratorFailed, null, null);
                }
                pendingCredential = new PendingCredential(
                    clientId,
                    credential,
                    displayName,
                    trustStore.MutationGeneration,
                    now);
                ClearChallenge();
                return new PairingConfirmationResult(
                    PairingConfirmOutcome.CredentialIssued,
                    clientId,
                    credential,
                    DisplayName: displayName);
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <summary>
    /// Finalizes a matching pending credential after the client durably saved it. Never holds
    /// <see cref="operationSemaphore"/> while awaiting <see cref="trustStore"/>'s persistence: it
    /// reserves the exact pending credential to commit under the lock, releases it, awaits persistence
    /// unguarded, then finalizes through <see cref="ClearPending"/>'s own <see cref="gate"/>-scoped
    /// compare-and-swap against that captured reservation. A slow or blocked write can therefore never
    /// synchronously block unrelated pairing lifecycle work -- <see cref="Cancel"/>,
    /// <see cref="NotifyDisconnected"/>, <see cref="CancelAll"/>, or another coordinator operation
    /// racing it during connection teardown. The trust store's own mutation-generation fencing, checked
    /// both before and during <see cref="ITrustStore.TryUpsertIfGenerationAsync"/>, remains the sole
    /// authority for whether this exact pending credential is still valid to persist; a concurrent
    /// <see cref="Cancel"/>/<see cref="CancelAll"/> that clears <see cref="pendingCredential"/> in the
    /// meantime cannot resurrect it, since <see cref="ClearPending"/> only clears a reference still
    /// equal to the one this call reserved.
    /// </summary>
    /// <param name="clientId">The client that owns the pending credential.</param>
    /// <param name="credential">The raw credential echoed by the client.</param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    public async Task<PairingCommitResult> CommitPendingAsync(
        ClientId clientId,
        string credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        PendingCredential? pending;
        TrustRecord? existingRecord;
        string? shortId;
        await operationSemaphore.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = clock.UtcNow;
            lock (gate)
            {
                ExpirePendingIfNeeded(now);
                if (pendingCredential is not { } current || current.ClientId != clientId ||
                    !CredentialHasher.FixedTimeEquals(current.Credential, credential))
                {
                    pending = null;
                }
                else
                {
                    pending = current;
                }
            }

            if (pending is null)
            {
                TrustRecord? existing = trustStore.TryGet(clientId);
                if (existing is not null && existing.State == KnownDeviceState.Trusted &&
                    CredentialHasher.FixedTimeEquals(existing.CredentialVerifier, CredentialHasher.Hash(credential)))
                {
                    return new PairingCommitResult(
                        PairingCommitOutcome.AlreadyTrusted,
                        clientId,
                        credential,
                        existing.ShortId,
                        existing.DisplayName);
                }
                return new PairingCommitResult(PairingCommitOutcome.PendingNotFound);
            }

            if (trustStore.MutationGeneration != pending.MutationGeneration)
            {
                ClearPending(pending);
                return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
            }

            existingRecord = trustStore.TryGet(clientId);
            shortId = existingRecord?.ShortId ?? GenerateUniqueShortId();
            if (shortId is null)
            {
                return new PairingCommitResult(PairingCommitOutcome.GeneratorFailed);
            }
        }
        finally
        {
            operationSemaphore.Release();
        }

        // Every branch above either returned already or left `pending`/`shortId` non-null -- the
        // reservation this call now commits without holding operationSemaphore across the await below.
        PendingCredential reserved = pending!;
        string? effectiveDisplayName = reserved.DisplayName ?? existingRecord?.DisplayName;
        var record = new TrustRecord(
            clientId,
            shortId!,
            effectiveDisplayName,
            KnownDeviceState.Trusted,
            CredentialHasher.Hash(reserved.Credential),
            existingRecord?.PairedAtUtc ?? reserved.CreatedAtUtc);

        bool committed;
        try
        {
            committed = await trustStore.TryUpsertIfGenerationAsync(
                record,
                reserved.MutationGeneration,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new PairingCommitResult(PairingCommitOutcome.PersistenceFailed);
        }

        if (!committed)
        {
            ClearPending(reserved);
            return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
        }

        ClearPending(reserved);
        return new PairingCommitResult(
            PairingCommitOutcome.Trusted,
            clientId,
            reserved.Credential,
            record.ShortId,
            record.DisplayName);
    }

    /// <inheritdoc/>
    public PairingRenotifyResult TryRenotify(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                PairingRenotifyResult evaluation = EvaluateRenotify(clientId, now);
                return evaluation.Outcome == PairingRenotifyOutcome.Renotified
                    ? evaluation with { ChallengeId = activeChallenge!.Id, Code = activeChallenge.Code }
                    : evaluation;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingCancelOutcome Cancel(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                ExpirePendingIfNeeded(now);
                if (pendingCredential is { ClientId: var pendingClient } && pendingClient == clientId)
                {
                    pendingCredential = null;
                    return PairingCancelOutcome.Cancelled;
                }
                if (activeChallenge is { OwnerClientId: var activeClient } && activeClient == clientId)
                {
                    ClearChallenge();
                    return PairingCancelOutcome.Cancelled;
                }
                return PairingCancelOutcome.AlreadyIdle;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void NotifyDisconnected(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                if (activeChallenge?.OwnerClientId == clientId)
                {
                    disconnectedAtUtc = clock.UtcNow;
                }
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void NotifyReconnected(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                ExpireChallengeIfNeeded(clock.UtcNow);
                if (activeChallenge?.OwnerClientId == clientId)
                {
                    disconnectedAtUtc = null;
                }
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void CancelAll()
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                activeChallenge = null;
                committedDisplayChallengeId = null;
                pendingCredential = null;
                disconnectedAtUtc = null;
                wrongAttempts = 0;
                lastConfirmAttemptUtc = null;
                renotifyCooldownUntilUtc = null;
                autoRenotifyCooldownUntilUtc = null;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingRenotifyResult CommitRenotify(ClientId clientId, ChallengeId challengeId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                if (activeChallenge is not { } current || current.OwnerClientId != clientId || current.Id != challengeId)
                {
                    // The challenge this commit was evaluated for is no longer current -- cancelled,
                    // expired, or replaced by a fresh challenge for the same owner. Report AlreadyIdle
                    // for the caller's stale request without evaluating or mutating whatever challenge
                    // (if any) is actually active now; a replacement challenge's own cooldown must
                    // never be consumed by a commit that was never actually displayed for it.
                    return new PairingRenotifyResult(PairingRenotifyOutcome.AlreadyIdle);
                }

                PairingRenotifyResult evaluation = EvaluateRenotify(clientId, now);
                if (evaluation.Outcome == PairingRenotifyOutcome.Renotified)
                {
                    renotifyCooldownUntilUtc = now + Constants.PairingRenotifyCooldown;
                }

                return evaluation;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingChallenge? TryGetOwnedChallenge(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                return activeChallenge is { } challenge &&
                    challenge.OwnerClientId == clientId &&
                    committedDisplayChallengeId == challenge.Id
                    ? challenge
                    : null;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <summary>
    /// Evaluates a redisplay request against current state without committing anything, shared by
    /// <see cref="TryRenotify"/>'s pure peek and <see cref="CommitRenotify"/>'s re-validation before
    /// committing. Callers must already hold <see cref="gate"/> and have called
    /// <see cref="ExpireChallengeIfNeeded"/> for <paramref name="now"/>.
    /// </summary>
    private PairingRenotifyResult EvaluateRenotify(ClientId clientId, DateTimeOffset now)
    {
        if (activeChallenge is null || activeChallenge.OwnerClientId != clientId)
        {
            return new PairingRenotifyResult(PairingRenotifyOutcome.AlreadyIdle);
        }
        if (renotifyCooldownUntilUtc is { } cooldown && now < cooldown)
        {
            return new PairingRenotifyResult(PairingRenotifyOutcome.Cooldown, cooldown - now);
        }

        return new PairingRenotifyResult(PairingRenotifyOutcome.Renotified);
    }

    /// <summary>Clears only the active challenge and its attempt/cooldown/display-commit state.</summary>
    private void ClearChallenge()
    {
        activeChallenge = null;
        committedDisplayChallengeId = null;
        disconnectedAtUtc = null;
        wrongAttempts = 0;
        lastConfirmAttemptUtc = null;
        renotifyCooldownUntilUtc = null;
        autoRenotifyCooldownUntilUtc = null;
    }

    /// <summary>Clears the matching pending credential after finalization or invalidation.</summary>
    private void ClearPending(PendingCredential pending)
    {
        lock (gate)
        {
            if (pendingCredential == pending)
            {
                pendingCredential = null;
            }
        }
    }

    /// <summary>Expires an active challenge after its code or reconnect-grace lifetime.</summary>
    private void ExpireChallengeIfNeeded(DateTimeOffset now)
    {
        ExpireDisconnectedChallengeIfNeeded(now);
        if (activeChallenge is { } challenge && now > challenge.ExpiresAtUtc)
        {
            ClearChallenge();
        }
    }

    /// <summary>Expires only the reconnect grace period without hiding code expiry from confirmation.</summary>
    private void ExpireDisconnectedChallengeIfNeeded(DateTimeOffset now)
    {
        if (disconnectedAtUtc is { } disconnected && now - disconnected >= Constants.PairingReconnectGracePeriod)
        {
            ClearChallenge();
        }
    }

    /// <summary>Expires a pending credential after its five-minute finalization lifetime.</summary>
    private void ExpirePendingIfNeeded(DateTimeOffset now)
    {
        if (pendingCredential is { } pending && now - pending.CreatedAtUtc >= Constants.PairingPendingCredentialLifetime)
        {
            pendingCredential = null;
        }
    }

    /// <summary>Generates a unique five-digit administration identifier with a bounded retry count.</summary>
    private string? GenerateUniqueShortId()
    {
        for (int attempt = 0; attempt < Constants.PairingMaxShortIdGenerationAttempts; attempt++)
        {
            string? candidate = shortIdGenerator();
            if (candidate is null)
            {
                return null;
            }
            if (trustStore.List().All(record => record.ShortId != candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Generates a six-digit pairing code, returning no value if secure generation fails.</summary>
    private static string? GeneratePairingCode()
    {
        try
        {
            return RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D" + Constants.PairingChallengeCodeDigits, CultureInfo.InvariantCulture);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Generates a 128-bit hex credential, returning no value if secure generation fails.</summary>
    private static string? GenerateCredential()
    {
        try
        {
            return RandomNumberGenerator.GetHexString(Constants.PairingCredentialLength, lowercase: true);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Generates a five-digit short-id candidate, returning no value if secure generation fails.</summary>
    private static string? GenerateShortIdCandidate()
    {
        try
        {
            return RandomNumberGenerator.GetInt32(0, 100_000)
                .ToString("D" + Constants.PairingShortIdDigits, CultureInfo.InvariantCulture);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Tracks values held between code confirmation and final trust commit.</summary>
    private sealed record PendingCredential(
        ClientId ClientId,
        string Credential,
        string? DisplayName,
        long MutationGeneration,
        DateTimeOffset CreatedAtUtc);
}
