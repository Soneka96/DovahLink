using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Sessions;
using DovahLink.Host.Time;

namespace DovahLink.Host.Trust;

/// <summary>
/// The global factory reset: a challenge-confirmed operation that resets every known device back to
/// unpaired and unconditionally invalidates every active session, per
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

    /// <summary>The time source used to issue and check challenge expiry.</summary>
    private readonly IClock clock;

    /// <summary>Guards <see cref="activeChallenge"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently issued, not-yet-confirmed challenge, or <see langword="null"/> if none is active.</summary>
    private FactoryResetChallenge? activeChallenge;

    /// <summary>Creates a factory-reset service.</summary>
    /// <param name="trustStore">The trust records to reset.</param>
    /// <param name="sessionRegistry">The session registry to invalidate on a confirmed reset.</param>
    /// <param name="clock">The time source used for challenge issuance and expiry.</param>
    public TrustResetService(ITrustStore trustStore, ISessionRegistry sessionRegistry, IClock clock)
    {
        this.trustStore = trustStore;
        this.sessionRegistry = sessionRegistry;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public FactoryResetChallenge BeginReset()
    {
        var challenge = new FactoryResetChallenge(
            RandomNumberGenerator.GetHexString(Constants.FactoryResetChallengeCodeLength, lowercase: true),
            clock.UtcNow + Constants.FactoryResetChallengeLifetime);

        lock (gate)
        {
            activeChallenge = challenge;
        }

        return challenge;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The challenge is only cleared once every device has actually been reset and every session
    /// invalidated. If a persistence write fails partway through, the challenge stays valid so the
    /// caller can retry with the same code; retrying is safe because re-resetting an
    /// already-<see cref="KnownDeviceState.Unpaired"/> device is a harmless no-op.
    /// </remarks>
    public async Task<bool> ConfirmResetAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        FactoryResetChallenge? challenge;
        lock (gate)
        {
            challenge = activeChallenge;
        }

        if (challenge is null || clock.UtcNow > challenge.ExpiresAtUtc)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(challenge.Code), Encoding.UTF8.GetBytes(code)))
        {
            return false;
        }

        foreach (TrustRecord record in trustStore.List())
        {
            await trustStore.UpsertAsync(record with { State = KnownDeviceState.Unpaired }, cancellationToken);
        }

        sessionRegistry.InvalidateAll();

        lock (gate)
        {
            activeChallenge = null;
        }

        return true;
    }
}
