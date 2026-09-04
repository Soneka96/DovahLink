using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Pairing;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;

namespace DovahLink.Host.Trust;

/// <summary>
/// The global factory reset: a challenge-confirmed operation that deletes every known device and
/// unconditionally invalidates every active session, per
/// <c>ai/context/protocol/security.md</c>'s "Factory Reset's unconditional session invalidation".
/// Distinct from <see cref="ITrustAdminService.ResetTrustAsync"/>, which resets trusted devices.
/// </summary>
public interface ITrustResetService
{
    /// <summary>
    /// Issues a new factory-reset challenge, replacing any previously issued one, unless a
    /// <see cref="ConfirmResetAsync"/> invocation already in flight has irrevocably claimed the
    /// currently active challenge for execution: that claimant already won the race to execute it, so
    /// this call cannot retroactively supersede it and the freshly issued challenge is returned to the
    /// caller without ever becoming the active one. This mirrors how a claimed pairing credential
    /// cannot be cancelled out from under an in-flight commit.
    /// </summary>
    /// <returns>The challenge whose code must be confirmed to complete the reset.</returns>
    FactoryResetChallenge BeginReset();

    /// <summary>Confirms a factory-reset challenge and, if valid, performs the reset.</summary>
    /// <param name="code">The code to check against the currently active challenge.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence writes.</param>
    /// <returns><see langword="true"/> if the code matched an unexpired challenge and the reset was performed.</returns>
    Task<bool> ConfirmResetAsync(string code, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITrustResetService"/>
public sealed class TrustResetService : ITrustResetService
{
    /// <summary>The trust records cleared back to unpaired by a confirmed reset.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>The seam through which every session is unconditionally invalidated by a confirmed reset.</summary>
    private readonly IClientSessionInvalidator sessionInvalidator;

    /// <summary>The pairing state cancelled by a confirmed reset.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>The time source used to issue and check challenge expiry.</summary>
    private readonly IClock clock;

    /// <summary>Guards <see cref="activeChallenge"/> and <see cref="activeConfirmClaim"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently issued, not-yet-confirmed challenge, or <see langword="null"/> if none is active.</summary>
    private FactoryResetChallenge? activeChallenge;

    /// <summary>
    /// The identity of the single <see cref="ConfirmResetAsync"/> invocation that has irrevocably
    /// claimed <see cref="activeChallenge"/> for execution, or <see langword="null"/> while it is
    /// unclaimed. While set, <see cref="BeginReset"/> must never replace <see cref="activeChallenge"/>:
    /// the claimant already won the race to execute it, so a fresh challenge issued while a claim is
    /// held never becomes the active one. Also gates <see cref="ConfirmResetAsync"/> itself: a
    /// concurrent call arriving while this is already set is not the claimant and must never re-claim
    /// or proceed toward the destructive reset, closing the residual a bare claimed/unclaimed flag
    /// would otherwise leave open -- two concurrent calls both observing "unclaimed" and both winning
    /// would otherwise both reach the destructive operation for the same challenge. Reset back to
    /// <see langword="null"/> whenever the claiming call's own attempt resolves, and only ever by the
    /// exact invocation that owns the current claim identity, so one call's release can never release a
    /// different concurrent call's claim.
    /// </summary>
    private ResetClaimId? activeConfirmClaim;

    /// <summary>Creates a factory-reset service.</summary>
    /// <param name="trustStore">The trust records to reset.</param>
    /// <param name="sessionInvalidator">The seam used to unconditionally invalidate every session on a confirmed reset.</param>
    /// <param name="pairingCoordinator">The pairing state to cancel after a confirmed reset.</param>
    /// <param name="clock">The time source used for challenge issuance and expiry.</param>
    public TrustResetService(ITrustStore trustStore, IClientSessionInvalidator sessionInvalidator, IPairingCoordinator pairingCoordinator, IClock clock)
    {
        this.trustStore = trustStore;
        this.sessionInvalidator = sessionInvalidator;
        this.pairingCoordinator = pairingCoordinator;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public FactoryResetChallenge BeginReset()
    {
        var challenge = new FactoryResetChallenge(
            RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D" + Constants.FactoryResetChallengeCodeDigits, CultureInfo.InvariantCulture),
            clock.UtcNow + Constants.FactoryResetChallengeLifetime);

        lock (gate)
        {
            if (activeConfirmClaim is null)
            {
                activeChallenge = challenge;
            }
            // else: an in-flight ConfirmResetAsync has already irrevocably claimed the active
            // challenge for execution -- see activeConfirmClaim's own remarks for what this closes.
        }

        return challenge;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Validates the active challenge and irrevocably claims it for execution in one atomic,
    /// synchronous block under <see cref="gate"/> -- re-validated ownership, expiry, code match, and
    /// that no other invocation already holds <see cref="activeConfirmClaim"/> -- before ever awaiting
    /// the destructive reset. From that instant, <see cref="BeginReset"/> can no longer replace this
    /// exact challenge, and a second concurrent <see cref="ConfirmResetAsync"/> call for it finds the
    /// claim already held and returns <see langword="false"/> immediately without ever attempting the
    /// destructive reset, so at most one call ever executes it. The challenge is only cleared once the
    /// trust store has been cleared and every session and pairing operation has been invalidated. If
    /// persistence fails, the claim is released and the challenge stays valid and unclaimed so the
    /// caller can retry the same all-or-nothing store mutation.
    /// </remarks>
    public async Task<bool> ConfirmResetAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        FactoryResetChallenge challenge;
        ResetClaimId claim;
        lock (gate)
        {
            FactoryResetChallenge? current = activeChallenge;
            if (current is null || clock.UtcNow > current.ExpiresAtUtc)
            {
                if (ReferenceEquals(activeChallenge, current))
                {
                    activeChallenge = null;
                }

                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current.Code), Encoding.UTF8.GetBytes(code)))
            {
                if (ReferenceEquals(activeChallenge, current))
                {
                    activeChallenge = null;
                }

                return false;
            }

            if (activeConfirmClaim is not null)
            {
                // Another concurrent ConfirmResetAsync invocation already exclusively claimed the
                // active challenge for execution. This call is not the claimant and must never enter
                // the destructive reset for it -- see activeConfirmClaim's own remarks for what this
                // closes.
                return false;
            }

            challenge = current;
            claim = ResetClaimId.New();
            activeConfirmClaim = claim;
        }

        // The claim above is held from this point on: every exit below that does not resolve it
        // through a successful reset releases it in the finally, mechanically rather than by
        // enumerating each failure branch -- see activeConfirmClaim's own remarks for what that closes.
        bool claimResolved = false;
        try
        {
            await trustStore.ClearAsync(cancellationToken);
            // Every session becomes unauthorized in the registry -- InvalidateAllAsync's own first
            // action, before its best-effort notification/close is attempted -- as close to the
            // authoritative Clear as possible, closing the post-mutation window a concurrent request
            // could otherwise still find IsActive true in. Pairing cancellation has no such window to
            // close (a stale challenge is already fence-bound and safe), so it follows rather than
            // races ahead of session invalidation.
            await sessionInvalidator.InvalidateAllAsync(SessionInvalidationReason.FactoryReset, cancellationToken);
            pairingCoordinator.CancelAll();

            lock (gate)
            {
                if (ReferenceEquals(activeChallenge, challenge) && activeConfirmClaim == claim)
                {
                    activeChallenge = null;
                }

                activeConfirmClaim = null;
            }

            claimResolved = true;
            return true;
        }
        finally
        {
            if (!claimResolved)
            {
                lock (gate)
                {
                    if (activeConfirmClaim == claim)
                    {
                        activeConfirmClaim = null;
                    }
                }
            }
        }
    }
}
