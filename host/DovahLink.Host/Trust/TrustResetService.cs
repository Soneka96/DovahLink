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
/// Distinct from <see cref="ITrustAdminService.ResetAsync"/>, which resets one device.
/// </summary>
public interface ITrustResetService
{
    /// <summary>Issues a new factory-reset challenge, replacing any previously issued one.</summary>
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

    /// <summary>The sessions unconditionally invalidated by a confirmed reset.</summary>
    private readonly ISessionRegistry sessionRegistry;

    /// <summary>The pairing state cancelled by a confirmed reset.</summary>
    private readonly IPairingCoordinator pairingCoordinator;

    /// <summary>The time source used to issue and check challenge expiry.</summary>
    private readonly IClock clock;

    /// <summary>Guards <see cref="activeChallenge"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Serializes challenge validation and the destructive reset operation.</summary>
    private readonly SemaphoreSlim confirmSemaphore = new(1, 1);

    /// <summary>The currently issued, not-yet-confirmed challenge, or <see langword="null"/> if none is active.</summary>
    private FactoryResetChallenge? activeChallenge;

    /// <summary>Creates a factory-reset service.</summary>
    /// <param name="trustStore">The trust records to reset.</param>
    /// <param name="sessionRegistry">The session registry to invalidate on a confirmed reset.</param>
    /// <param name="pairingCoordinator">The pairing state to cancel after a confirmed reset.</param>
    /// <param name="clock">The time source used for challenge issuance and expiry.</param>
    public TrustResetService(ITrustStore trustStore, ISessionRegistry sessionRegistry, IPairingCoordinator pairingCoordinator, IClock clock)
    {
        this.trustStore = trustStore;
        this.sessionRegistry = sessionRegistry;
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
            activeChallenge = challenge;
        }

        return challenge;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The challenge is only cleared once the trust store has been cleared and every session and
    /// pairing operation has been invalidated. If persistence fails, the challenge stays valid so
    /// the caller can retry the same all-or-nothing store mutation.
    /// </remarks>
    public async Task<bool> ConfirmResetAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        await confirmSemaphore.WaitAsync(cancellationToken);
        try
        {
            FactoryResetChallenge? challenge;
            lock (gate)
            {
                challenge = activeChallenge;
            }

            if (challenge is null || clock.UtcNow > challenge.ExpiresAtUtc)
            {
                lock (gate)
                {
                    if (ReferenceEquals(activeChallenge, challenge))
                    {
                        activeChallenge = null;
                    }
                }

                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(challenge.Code), Encoding.UTF8.GetBytes(code)))
            {
                lock (gate)
                {
                    if (ReferenceEquals(activeChallenge, challenge))
                    {
                        activeChallenge = null;
                    }
                }

                return false;
            }

            await trustStore.ClearAsync(cancellationToken);
            pairingCoordinator.CancelAll();
            sessionRegistry.InvalidateAll();

            lock (gate)
            {
                if (ReferenceEquals(activeChallenge, challenge))
                {
                    activeChallenge = null;
                }
            }

            return true;
        }
        finally
        {
            confirmSemaphore.Release();
        }
    }
}
