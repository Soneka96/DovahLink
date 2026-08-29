using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Identity;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Pairing;

/// <summary>
/// The host's pairing state machine. Only one pairing challenge may be active at a time, globally,
/// per <c>ai/context/protocol/security.md</c>'s "Persistent local trust": a request while a
/// challenge is already active does not generate a second code, it stays bound to the existing one.
/// </summary>
public interface IPairingCoordinator
{
    /// <summary>
    /// Begins pairing, or returns the still-active challenge if one was already issued and has not
    /// expired.
    /// </summary>
    /// <returns>The challenge whose code must be confirmed to complete pairing.</returns>
    PairingChallenge BeginPairing();

    /// <summary>Confirms a pairing challenge's code and, if valid, commits a new trusted device.</summary>
    /// <param name="code">The code to check against the currently active challenge.</param>
    /// <param name="displayName">The display name to give the newly paired device.</param>
    /// <param name="cancellationToken">The token used to cancel the underlying persistence write.</param>
    /// <returns>The confirmation outcome: trusted, rejected, or expired.</returns>
    Task<PairingConfirmationResult> ConfirmCredentialAsync(string code, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Cancels the active pairing challenge so it cannot be confirmed later.</summary>
    void CancelAll();
}

/// <inheritdoc cref="IPairingCoordinator"/>
public sealed class PairingCoordinator : IPairingCoordinator
{
    /// <summary>The trust store a successful pairing commits a new trusted device to.</summary>
    private readonly ITrustStore trustStore;

    /// <summary>The time source used to issue and check challenge expiry.</summary>
    private readonly IClock clock;

    /// <summary>Guards <see cref="activeChallenge"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>
    /// Serializes the full check-then-commit sequence in <see cref="ConfirmCredentialAsync"/> so two
    /// concurrent confirmations of the same code cannot both pass validation and both commit a
    /// trusted device before either clears the challenge.
    /// </summary>
    private readonly SemaphoreSlim confirmSemaphore = new(1, 1);

    /// <summary>The currently issued, not-yet-resolved challenge, or <see langword="null"/> if none is active.</summary>
    private PairingChallenge? activeChallenge;

    /// <summary>Creates a pairing coordinator.</summary>
    /// <param name="trustStore">The trust store a successful pairing commits a new trusted device to.</param>
    /// <param name="clock">The time source used for challenge issuance and expiry.</param>
    public PairingCoordinator(ITrustStore trustStore, IClock clock)
    {
        this.trustStore = trustStore;
        this.clock = clock;
    }

    /// <inheritdoc/>
    public PairingChallenge BeginPairing()
    {
        lock (gate)
        {
            if (activeChallenge is { } existing && clock.UtcNow <= existing.ExpiresAtUtc)
            {
                return existing;
            }

            int codeUpperBound = (int)Math.Pow(10, Constants.PairingChallengeCodeDigits);
            string code = RandomNumberGenerator.GetInt32(0, codeUpperBound)
                .ToString("D" + Constants.PairingChallengeCodeDigits, CultureInfo.InvariantCulture);
            var challenge = new PairingChallenge(code, clock.UtcNow + Constants.PairingChallengeLifetime);
            activeChallenge = challenge;
            return challenge;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The challenge is only cleared once the new trusted device has actually been persisted, so a
    /// persistence failure leaves the challenge confirmable again with the same code.
    /// </remarks>
    public async Task<PairingConfirmationResult> ConfirmCredentialAsync(string code, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(displayName);

        await confirmSemaphore.WaitAsync(cancellationToken);
        try
        {
            PairingChallenge? challenge;
            lock (gate)
            {
                challenge = activeChallenge;
            }

            if (challenge is null)
            {
                return new PairingConfirmationResult(PairingState.Rejected, null, null);
            }

            if (clock.UtcNow > challenge.ExpiresAtUtc)
            {
                return new PairingConfirmationResult(PairingState.Expired, null, null);
            }

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(challenge.Code), Encoding.UTF8.GetBytes(code)))
            {
                return new PairingConfirmationResult(PairingState.Rejected, null, null);
            }

            string credential = RandomNumberGenerator.GetHexString(Constants.PairingCredentialLength, lowercase: true);
            string credentialVerifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
            string shortId = RandomNumberGenerator.GetHexString(Constants.PairingShortIdLength, lowercase: false);
            ClientId clientId = ClientId.NewId();
            var record = new TrustRecord(clientId, shortId, displayName, KnownDeviceState.Trusted, credentialVerifier, clock.UtcNow);

            await trustStore.UpsertAsync(record, cancellationToken);

            lock (gate)
            {
                activeChallenge = null;
            }

            return new PairingConfirmationResult(PairingState.Trusted, clientId, credential);
        }
        finally
        {
            confirmSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void CancelAll()
    {
        lock (gate)
        {
            activeChallenge = null;
        }
    }
}
