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

    /// <summary>Requests a rate-limited redisplay of the owner's active code.</summary>
    /// <param name="clientId">The client requesting redisplay.</param>
    PairingRenotifyResult TryRenotify(ClientId clientId);

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
                var challenge = new PairingChallenge(clientId, code, now + Constants.PairingChallengeLifetime);
                activeChallenge = challenge;
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
                if (!FixedTimeEquals(challenge.Code, code))
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

    /// <inheritdoc/>
    public async Task<PairingCommitResult> CommitPendingAsync(
        ClientId clientId,
        string credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        await operationSemaphore.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = clock.UtcNow;
            PendingCredential? pending;
            lock (gate)
            {
                ExpirePendingIfNeeded(now);
                if (pendingCredential is not { } current || current.ClientId != clientId ||
                    !FixedTimeEquals(current.Credential, credential))
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
                    FixedTimeEquals(existing.CredentialVerifier, HashCredential(credential)))
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

            TrustRecord? existingRecord = trustStore.TryGet(clientId);
            string? shortId = existingRecord?.ShortId ?? GenerateUniqueShortId();
            if (shortId is null)
            {
                return new PairingCommitResult(PairingCommitOutcome.GeneratorFailed);
            }
            string? effectiveDisplayName = pending.DisplayName ?? existingRecord?.DisplayName;
            var record = new TrustRecord(
                clientId,
                shortId,
                effectiveDisplayName,
                KnownDeviceState.Trusted,
                HashCredential(pending.Credential),
                existingRecord?.PairedAtUtc ?? pending.CreatedAtUtc);

            bool committed;
            try
            {
                committed = await trustStore.TryUpsertIfGenerationAsync(
                    record,
                    pending.MutationGeneration,
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
                ClearPending(pending);
                return new PairingCommitResult(PairingCommitOutcome.PairingInvalidated);
            }

            ClearPending(pending);
            return new PairingCommitResult(
                PairingCommitOutcome.Trusted,
                clientId,
                pending.Credential,
                record.ShortId,
                record.DisplayName);
        }
        finally
        {
            operationSemaphore.Release();
        }
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
                if (activeChallenge is null || activeChallenge.OwnerClientId != clientId)
                {
                    return new PairingRenotifyResult(PairingRenotifyOutcome.AlreadyIdle);
                }
                if (renotifyCooldownUntilUtc is { } cooldown && now < cooldown)
                {
                    return new PairingRenotifyResult(PairingRenotifyOutcome.Cooldown, cooldown - now);
                }

                renotifyCooldownUntilUtc = now + Constants.PairingRenotifyCooldown;
                return new PairingRenotifyResult(PairingRenotifyOutcome.Renotified);
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

    /// <summary>Clears only the active challenge and its attempt/cooldown state.</summary>
    private void ClearChallenge()
    {
        activeChallenge = null;
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

    /// <summary>Hashes a credential before it enters durable trust state.</summary>
    private static string HashCredential(string credential) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    /// <summary>Compares two credential values without an early-exit equality check.</summary>
    private static bool FixedTimeEquals(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));

    /// <summary>Tracks values held between code confirmation and final trust commit.</summary>
    private sealed record PendingCredential(
        ClientId ClientId,
        string Credential,
        string? DisplayName,
        long MutationGeneration,
        DateTimeOffset CreatedAtUtc);
}
