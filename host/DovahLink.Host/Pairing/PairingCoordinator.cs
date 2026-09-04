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

    /// <summary>
    /// Evaluates a client's submitted pairing code. A code cannot be confirmed before this exact
    /// challenge's initial display has been accepted by the adapter through
    /// <see cref="CommitInitialDisplay"/>: an attempt made before that commit is simply invalid,
    /// without consuming pacing, wrong-attempt, or cooldown state.
    /// </summary>
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
    /// Evaluates a redisplay request for <paramref name="clientId"/>'s owned active challenge and, when
    /// eligible, grants an exclusive redisplay reservation rather than merely peeking -- it does not
    /// commit the manual-redisplay cooldown. A caller that intends to actually redisplay the code must
    /// await its own adapter notification outside any lock this coordinator holds, then call
    /// <see cref="CommitRenotify"/> once that notification is accepted, or
    /// <see cref="RollbackRenotify"/> if it is rejected, faults, or the caller's own request is
    /// cancelled -- every exit from that adapter I/O must resolve the granted reservation one way or
    /// the other. While a reservation is already outstanding for the active challenge, a second
    /// concurrent call reports <see cref="PairingRenotifyOutcome.AlreadyIdle"/> rather than also being
    /// told it may redisplay: at most one caller may ever be mid-adapter-notification for one challenge
    /// at a time, so two adapter notifications can never land against one cooldown commit.
    /// </summary>
    /// <param name="clientId">The client requesting redisplay.</param>
    /// <returns>
    /// <see cref="PairingRenotifyOutcome.Renotified"/>, with a reservation the caller must resolve via
    /// <see cref="CommitRenotify"/> or <see cref="RollbackRenotify"/>, when <paramref name="clientId"/>
    /// owns a display-committed active challenge outside cooldown with no reservation already
    /// outstanding for it; <see cref="PairingRenotifyOutcome.Cooldown"/> with the remaining wait while
    /// the cooldown is still active; <see cref="PairingRenotifyOutcome.AlreadyIdle"/> when
    /// <paramref name="clientId"/> owns no active challenge, owns one whose initial display has not yet
    /// committed, or a reservation for it is already outstanding.
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

    /// <summary>
    /// Cancels the pairing operation owned by one client. Linearized against a concurrent
    /// <see cref="CommitPendingAsync"/> racing the same pending credential: if this call wins first,
    /// the credential is cleared and that commit subsequently reports
    /// <see cref="PairingCommitOutcome.PendingNotFound"/> without ever attempting persistence; if a
    /// commit has already irrevocably claimed the credential first, this call can no longer clear it
    /// and reports <see cref="PairingCancelOutcome.AlreadyIdle"/> rather than falsely claiming
    /// <see cref="PairingCancelOutcome.Cancelled"/> for a credential that may already be persisting.
    /// </summary>
    /// <param name="clientId">The client giving up its pairing operation.</param>
    PairingCancelOutcome Cancel(ClientId clientId);

    /// <summary>Records that the owner's connection disconnected.</summary>
    /// <param name="clientId">The disconnected client.</param>
    void NotifyDisconnected(ClientId clientId);

    /// <summary>Records that the owner's connection reconnected.</summary>
    /// <param name="clientId">The reconnected client.</param>
    void NotifyReconnected(ClientId clientId);

    /// <summary>
    /// Cancels every active challenge and pending credential, with the same linearization against a
    /// concurrent <see cref="CommitPendingAsync"/> that <see cref="Cancel"/>'s own doc describes.
    /// </summary>
    void CancelAll();

    /// <summary>
    /// Commits a redisplay reservation <see cref="TryRenotify"/> granted, applying the
    /// manual-redisplay cooldown only now that the caller's adapter notification succeeded.
    /// Re-validates ownership, the exact challenge identity, and the exact reservation identity from
    /// current state rather than trusting the earlier <see cref="TryRenotify"/> result: if the
    /// challenge was cancelled, replaced, or expired, or this exact reservation was already resolved
    /// (by a prior commit, rollback, or the challenge's own clear) between the two calls, this reports
    /// <see cref="PairingRenotifyOutcome.AlreadyIdle"/> instead of committing a cooldown against state
    /// that no longer matches it.
    /// </summary>
    /// <param name="clientId">The client committing its previously granted reservation.</param>
    /// <param name="challengeId">
    /// The exact challenge identity <see cref="TryRenotify"/> returned alongside
    /// <see cref="PairingRenotifyOutcome.Renotified"/>.
    /// </param>
    /// <param name="claimId">
    /// The exact reservation identity <see cref="TryRenotify"/> granted alongside
    /// <paramref name="challengeId"/>.
    /// </param>
    /// <returns>
    /// <see cref="PairingRenotifyOutcome.Renotified"/> once the cooldown is committed against this
    /// exact reservation; <see cref="PairingRenotifyOutcome.AlreadyIdle"/> without any state change if
    /// <paramref name="challengeId"/> or <paramref name="claimId"/> no longer matches the current
    /// outstanding reservation -- including when a replacement challenge for the same
    /// <paramref name="clientId"/> is now active.
    /// </returns>
    PairingRenotifyResult CommitRenotify(ClientId clientId, ChallengeId challengeId, RenotifyClaimId claimId);

    /// <summary>
    /// Releases a redisplay reservation <see cref="TryRenotify"/> granted, without applying the
    /// manual-redisplay cooldown, for a caller whose adapter notification was rejected, faulted, or
    /// was itself cancelled: the redisplay never actually happened, so the reservation must be freed
    /// for an immediate retry rather than left stuck or falsely rate-limited. Identity-checked the
    /// same way as <see cref="CommitRenotify"/>: a stale release for a reservation already resolved,
    /// or for a challenge already replaced, cancelled, or expired, is simply a no-op.
    /// </summary>
    /// <param name="clientId">The client releasing its previously granted reservation.</param>
    /// <param name="challengeId">
    /// The exact challenge identity <see cref="TryRenotify"/> returned alongside
    /// <see cref="PairingRenotifyOutcome.Renotified"/>.
    /// </param>
    /// <param name="claimId">
    /// The exact reservation identity <see cref="TryRenotify"/> granted alongside
    /// <paramref name="challengeId"/>.
    /// </param>
    void RollbackRenotify(ClientId clientId, ChallengeId challengeId, RenotifyClaimId claimId);

    /// <summary>
    /// Returns one coherent snapshot of <paramref name="clientId"/>'s pairing status, read once under
    /// this coordinator's own lock so a caller never combines separately-synchronized reads into a
    /// state that never coherently existed. Distinguishes an unacknowledged display reservation, a
    /// displayed challenge, an owned pending credential, another client's active operation, and no
    /// operation at all -- a caller must never infer any of these from a nullable challenge alone.
    /// </summary>
    /// <param name="clientId">The client whose pairing status to check.</param>
    PairingStatusSnapshot GetStatusSnapshot(ClientId clientId);
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
    /// <see cref="GetStatusSnapshot"/> and <see cref="EvaluateRenotify"/>: a reservation must not be
    /// publicly resumable, displayed, or renotify-eligible before its initial display commits.
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

    /// <summary>
    /// The identity of the single outstanding redisplay reservation <see cref="TryRenotify"/> granted,
    /// or <see langword="null"/> while none is outstanding. Bound to
    /// <see cref="renotifyClaimChallengeId"/>: while set, a second concurrent <see cref="TryRenotify"/>
    /// call must never also be granted a reservation for the same challenge, and only
    /// <see cref="CommitRenotify"/> or <see cref="RollbackRenotify"/> presenting this exact identity
    /// together with <see cref="renotifyClaimChallengeId"/> may resolve it. Cleared alongside the
    /// challenge itself by both <see cref="ClearChallenge"/> and <see cref="CancelAll"/>'s own inline
    /// reset, so a stale commit or rollback for a since-cancelled, expired, or replaced challenge can
    /// never resolve a different, current reservation, and an abandoned reservation can never
    /// permanently block every later challenge's own redisplay.
    /// </summary>
    private RenotifyClaimId? renotifyClaim;

    /// <summary>The exact challenge <see cref="renotifyClaim"/> was granted for.</summary>
    private ChallengeId? renotifyClaimChallengeId;

    /// <summary>The pending credential waiting for client final confirmation.</summary>
    private PendingCredential? pendingCredential;

    /// <summary>
    /// The identity of the single <see cref="CommitPendingAsync"/> invocation that has irrevocably
    /// claimed <see cref="pendingCredential"/> for persistence, or <see langword="null"/> while it is
    /// unclaimed. While set, <see cref="Cancel"/>, <see cref="CancelAll"/>, and
    /// <see cref="ExpirePendingIfNeeded"/> must never clear or replace <see cref="pendingCredential"/>:
    /// the claimant already won the race to finalize it, so cancellation can no longer truthfully
    /// claim it cancelled that exact credential. Also gates <see cref="CommitPendingAsync"/> itself: a
    /// concurrent call arriving while this is already set is not the claimant and must never re-claim
    /// or proceed toward persistence, closing the residual a bare claimed/unclaimed flag left open --
    /// two concurrent calls both observing "unclaimed" and both winning would otherwise both reach
    /// persistence for the same reservation. Reset back to <see langword="null"/> whenever the
    /// claiming call's own attempt resolves -- through <see cref="ClearPending"/> on a durable outcome,
    /// or <see cref="ReleaseCommitClaim"/> when the attempt never reached persistence or the
    /// persistence attempt itself faulted or was cancelled -- and only ever by the exact invocation
    /// that owns the current claim identity, so one call's release can never release a different
    /// concurrent call's claim.
    /// </summary>
    private CommitClaimId? pendingCommitClaim;

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
                if (committedDisplayChallengeId != challenge.Id)
                {
                    // The adapter has not yet positively accepted this exact challenge's initial
                    // display. The client cannot legitimately know this challenge's code before the
                    // host has confirmed it was actually shown, so a confirm attempt here is simply
                    // invalid -- without consuming pacing, wrong-attempt, or cooldown state the way a
                    // genuinely wrong code would, since nothing about this attempt reflects the
                    // client's actual knowledge of the real code.
                    return new PairingConfirmationResult(PairingConfirmOutcome.Invalid, null, null);
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
                    trustStore.SecurityFenceGeneration,
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
    /// <see cref="operationSemaphore"/> while awaiting <see cref="trustStore"/>'s persistence: under
    /// one <see cref="gate"/>-scoped block it re-validates the reservation, checks whether another
    /// invocation already owns <see cref="pendingCommitClaim"/>, and checks it against the trust
    /// store's current <see cref="ITrustStore.SecurityFenceGeneration"/> -- only if all three hold --
    /// irrevocably claims it with a fresh <see cref="CommitClaimId"/> before releasing the lock,
    /// awaiting persistence unguarded, then finalizing through <see cref="ClearPending"/>. A slow or
    /// blocked write can therefore never synchronously block unrelated pairing lifecycle work --
    /// <see cref="Cancel"/>, <see cref="NotifyDisconnected"/>, <see cref="CancelAll"/>, or another
    /// coordinator operation racing it during connection teardown. This claim is what linearizes ACK
    /// against both Cancel/CancelAll and a second concurrent ACK for the exact same credential: once
    /// claimed, <see cref="pendingCommitClaim"/> stops <see cref="Cancel"/>/<see cref="CancelAll"/>
    /// from ever clearing or replacing this reservation, and stops a concurrent
    /// <see cref="CommitPendingAsync"/> call from ever claiming it too, so persistence is the sole
    /// remaining authority over whether it becomes Trusted, and no concurrent cancellation can report
    /// <see cref="PairingCancelOutcome.Cancelled"/> for a credential that goes on to persist durably --
    /// nor can a losing concurrent ACK ever reach persistence for it. A cancellation that instead lands
    /// before the claim -- while <see cref="pendingCommitClaim"/> is still <see langword="null"/> --
    /// clears the reservation outright, so this call finds nothing to claim and reports
    /// <see cref="PairingCommitOutcome.PendingNotFound"/> without ever attempting persistence; a losing
    /// concurrent ACK that arrives after another call already claimed the reservation reports the same
    /// <see cref="PairingCommitOutcome.PendingNotFound"/> without persisting anything. If the winning
    /// call's own attempt does not reach a durable outcome -- a short-id generator exhaustion before
    /// persistence is even attempted, a thrown persistence exception, or the caller's own token being
    /// cancelled -- it releases its own claim through <see cref="ReleaseCommitClaim"/> without clearing
    /// the pending credential, leaving it exactly as retryable and cancellable as it was before this
    /// call claimed it.
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
        CommitClaimId claim = default;
        bool generationInvalidated = false;
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
                else if (pendingCommitClaim is not null)
                {
                    // Another concurrent CommitPendingAsync invocation already exclusively claimed
                    // this exact reservation for finalization. This call is not the claimant and must
                    // never enter short-id generation or persistence for it -- see
                    // pendingCommitClaim's own remarks for what this closes.
                    pending = null;
                }
                else if (trustStore.SecurityFenceGeneration != current.SecurityFenceGeneration)
                {
                    // The security fence advanced since this credential was issued: drop the
                    // reservation now, under this same lock, so nothing can race to claim it after
                    // this check.
                    pendingCredential = null;
                    pendingCommitClaim = null;
                    pending = null;
                    generationInvalidated = true;
                }
                else
                {
                    // Claim the reservation irrevocably before releasing the lock -- see
                    // pendingCommitClaim's own remarks for what this closes.
                    claim = CommitClaimId.New();
                    pendingCommitClaim = claim;
                    pending = current;
                }
            }

            if (pending is null)
            {
                if (generationInvalidated)
                {
                    return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
                }

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

            existingRecord = trustStore.TryGet(clientId);
            shortId = existingRecord?.ShortId ?? GenerateUniqueShortId();
            if (shortId is null)
            {
                ReleaseCommitClaim(pending, claim);
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
                reserved.SecurityFenceGeneration,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReleaseCommitClaim(reserved, claim);
            throw;
        }
        catch
        {
            ReleaseCommitClaim(reserved, claim);
            return new PairingCommitResult(PairingCommitOutcome.PersistenceFailed);
        }

        if (!committed)
        {
            // The fence moved between the claim and this write through some other trust mutation --
            // never a Cancel/CancelAll or a concurrent ACK, which this claim already made impossible --
            // so the reservation is genuinely invalidated rather than merely un-claimed.
            ClearPending(reserved, claim);
            return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
        }

        if (!ClearPending(reserved, claim))
        {
            // Unreachable under the claim above: once pendingCommitClaim is set to this call's own
            // identity, Cancel/CancelAll/expiry/a concurrent ACK can never clear or replace this exact
            // reservation, so ClearPending always finds it still live here. Kept as a defensive
            // invariant check rather than assumed.
            return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
        }

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
                if (evaluation.Outcome != PairingRenotifyOutcome.Renotified)
                {
                    return evaluation;
                }

                if (renotifyClaim is not null)
                {
                    // Another redisplay reservation is already outstanding for the active challenge --
                    // its own adapter notification has not yet resolved. A second concurrent caller
                    // must never also be told it may redisplay, or two adapter notifications could
                    // land against one cooldown commit; reuse the same closed-vocabulary "nothing to do
                    // right now" outcome an unclaimed reservation reports, rather than inventing new
                    // wire vocabulary for an internal exclusivity race.
                    return new PairingRenotifyResult(PairingRenotifyOutcome.AlreadyIdle);
                }

                RenotifyClaimId claim = RenotifyClaimId.New();
                renotifyClaim = claim;
                renotifyClaimChallengeId = activeChallenge!.Id;
                return evaluation with { ChallengeId = activeChallenge.Id, Code = activeChallenge.Code, ClaimId = claim };
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
                    if (pendingCommitClaim is not null)
                    {
                        // An in-flight commit has already irrevocably claimed this exact reservation
                        // for persistence: cancellation can no longer truthfully undo it, so this
                        // reports nothing left to cancel rather than falsely claiming success while a
                        // durable Trusted record may already be landing.
                        return PairingCancelOutcome.AlreadyIdle;
                    }

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
                if (pendingCommitClaim is null)
                {
                    // An in-flight commit has already irrevocably claimed the pending credential for
                    // persistence: this administrative cancellation can no longer undo it and must
                    // leave it to the commit's own finalization rather than clear a reservation that
                    // may already be landing durably.
                    pendingCredential = null;
                }
                disconnectedAtUtc = null;
                wrongAttempts = 0;
                lastConfirmAttemptUtc = null;
                renotifyCooldownUntilUtc = null;
                autoRenotifyCooldownUntilUtc = null;
                renotifyClaim = null;
                renotifyClaimChallengeId = null;
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingRenotifyResult CommitRenotify(ClientId clientId, ChallengeId challengeId, RenotifyClaimId claimId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                if (activeChallenge is not { } current || current.OwnerClientId != clientId || current.Id != challengeId ||
                    renotifyClaim != claimId || renotifyClaimChallengeId != challengeId)
                {
                    // The challenge this commit was evaluated for is no longer current -- cancelled,
                    // expired, or replaced by a fresh challenge for the same owner -- or this exact
                    // reservation was already resolved by a prior commit or rollback. Report AlreadyIdle
                    // for the caller's stale request without evaluating or mutating whatever challenge
                    // (if any) is actually active now; a replacement challenge's own cooldown must
                    // never be consumed by a commit that was never actually displayed for it.
                    return new PairingRenotifyResult(PairingRenotifyOutcome.AlreadyIdle);
                }

                renotifyClaim = null;
                renotifyClaimChallengeId = null;
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
    public void RollbackRenotify(ClientId clientId, ChallengeId challengeId, RenotifyClaimId claimId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                if (renotifyClaim == claimId && renotifyClaimChallengeId == challengeId &&
                    activeChallenge is { } current && current.OwnerClientId == clientId && current.Id == challengeId)
                {
                    renotifyClaim = null;
                    renotifyClaimChallengeId = null;
                }
            }
        }
        finally
        {
            operationSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public PairingStatusSnapshot GetStatusSnapshot(ClientId clientId)
    {
        operationSemaphore.Wait();
        try
        {
            lock (gate)
            {
                DateTimeOffset now = clock.UtcNow;
                ExpireChallengeIfNeeded(now);
                ExpirePendingIfNeeded(now);

                if (pendingCredential is { } pending)
                {
                    return new PairingStatusSnapshot(
                        pending.ClientId == clientId ? PairingStatusKind.PendingCredential : PairingStatusKind.OtherDeviceActive,
                        null);
                }

                if (activeChallenge is { } challenge)
                {
                    if (challenge.OwnerClientId != clientId)
                    {
                        return new PairingStatusSnapshot(PairingStatusKind.OtherDeviceActive, null);
                    }

                    return committedDisplayChallengeId == challenge.Id
                        ? new PairingStatusSnapshot(PairingStatusKind.DisplayedChallenge, challenge)
                        : new PairingStatusSnapshot(PairingStatusKind.UncommittedDisplayReservation, null);
                }

                return new PairingStatusSnapshot(PairingStatusKind.Idle, null);
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
    /// committing. Requires the active challenge to be owned by <paramref name="clientId"/> and to
    /// have already committed its initial display: an unacknowledged reservation has never been shown
    /// to the client, so there is nothing yet to redisplay. Callers must already hold <see cref="gate"/>
    /// and have called <see cref="ExpireChallengeIfNeeded"/> for <paramref name="now"/>.
    /// </summary>
    private PairingRenotifyResult EvaluateRenotify(ClientId clientId, DateTimeOffset now)
    {
        if (activeChallenge is not { } challenge ||
            challenge.OwnerClientId != clientId ||
            committedDisplayChallengeId != challenge.Id)
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
        renotifyClaim = null;
        renotifyClaimChallengeId = null;
    }

    /// <summary>
    /// Clears the matching pending credential after finalization or invalidation, reporting whether
    /// <paramref name="pending"/> was still the live reservation under <paramref name="claim"/>'s
    /// exact identity, and resets <see cref="pendingCommitClaim"/> alongside it. Once
    /// <see cref="CommitPendingAsync"/> has claimed <paramref name="pending"/> under
    /// <paramref name="claim"/>, nothing else can clear or replace it before this call does, so
    /// <see langword="false"/> is expected only from a caller that never actually held this exact
    /// claim.
    /// </summary>
    /// <param name="pending">The exact reservation to clear.</param>
    /// <param name="claim">The exact claim identity this invocation was granted.</param>
    /// <returns><see langword="true"/> if <paramref name="pending"/> was still live under <paramref name="claim"/> and is now cleared.</returns>
    private bool ClearPending(PendingCredential pending, CommitClaimId claim)
    {
        lock (gate)
        {
            if (pendingCredential != pending || pendingCommitClaim != claim)
            {
                return false;
            }

            pendingCredential = null;
            pendingCommitClaim = null;
            return true;
        }
    }

    /// <summary>
    /// Releases <see cref="CommitPendingAsync"/>'s irrevocable claim on <paramref name="pending"/>
    /// under <paramref name="claim"/>'s exact identity without clearing it, for a commit attempt that
    /// never reached persistence or whose persistence attempt itself faulted or was cancelled: the
    /// pending credential remains exactly as retryable and cancellable as it was before this call
    /// claimed it. Comparing <paramref name="claim"/> rather than only <paramref name="pending"/>
    /// ensures this call can only ever release its own claim, never one a different concurrent
    /// invocation currently holds for the same reservation.
    /// </summary>
    /// <param name="pending">The exact reservation this call had claimed.</param>
    /// <param name="claim">The exact claim identity this invocation was granted.</param>
    private void ReleaseCommitClaim(PendingCredential pending, CommitClaimId claim)
    {
        lock (gate)
        {
            if (pendingCredential == pending && pendingCommitClaim == claim)
            {
                pendingCommitClaim = null;
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

    /// <summary>
    /// Expires a pending credential after its five-minute finalization lifetime, unless
    /// <see cref="pendingCommitClaim"/> means an in-flight commit has already irrevocably claimed
    /// it -- expiry must never clear a reservation a commit may currently be persisting.
    /// </summary>
    private void ExpirePendingIfNeeded(DateTimeOffset now)
    {
        if (pendingCommitClaim is null && pendingCredential is { } pending &&
            now - pending.CreatedAtUtc >= Constants.PairingPendingCredentialLifetime)
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
        long SecurityFenceGeneration,
        DateTimeOffset CreatedAtUtc);
}
