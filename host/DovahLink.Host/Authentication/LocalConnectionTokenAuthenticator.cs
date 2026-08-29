using System.Security.Cryptography;
using System.Text;
using DovahLink.Host.Time;

namespace DovahLink.Host.Authentication;

/// <summary>
/// Issues and validates the one-time local connection token required before a client's first
/// message, per <c>ai/context/protocol/security.md</c>'s "Phase 1 exposure": a cryptographically
/// random token that expires after its lifetime or first successful use, whichever comes first,
/// with globally rate-limited failed attempts.
/// </summary>
public interface ILocalConnectionTokenAuthenticator
{
    /// <summary>Issues a new token, replacing any previously issued, unconsumed one.</summary>
    /// <returns>The newly issued token.</returns>
    string IssueToken();

    /// <summary>
    /// Atomically checks and consumes the current token: at most one caller can successfully
    /// consume a given token.
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <returns><see langword="true"/> if the presented token matched the current, unexpired token.</returns>
    bool TryConsume(string presentedToken);
}

/// <inheritdoc cref="ILocalConnectionTokenAuthenticator"/>
public sealed class LocalConnectionTokenAuthenticator : ILocalConnectionTokenAuthenticator
{
    /// <summary>The time source used for token expiry and the failure-rate window.</summary>
    private readonly IClock clock;

    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The currently issued, not-yet-consumed token, or <see langword="null"/> if none is active or it was already consumed.</summary>
    private string? currentToken;

    /// <summary>The UTC time after which <see cref="currentToken"/> can no longer be consumed.</summary>
    private DateTimeOffset expiresAtUtc;

    /// <summary>The UTC time of each failed attempt still inside the rate-limit window, oldest first.</summary>
    private readonly Queue<DateTimeOffset> recentFailures = new();

    /// <summary>Creates a local connection token authenticator.</summary>
    /// <param name="clock">The time source used for token expiry and the failure-rate window.</param>
    public LocalConnectionTokenAuthenticator(IClock clock)
    {
        this.clock = clock;
    }

    /// <inheritdoc/>
    public string IssueToken()
    {
        string token = RandomNumberGenerator.GetHexString(Constants.LocalConnectionTokenLength, lowercase: true);
        lock (gate)
        {
            currentToken = token;
            expiresAtUtc = clock.UtcNow + Constants.LocalConnectionTokenLifetime;
        }

        return token;
    }

    /// <inheritdoc/>
    public bool TryConsume(string presentedToken)
    {
        ArgumentNullException.ThrowIfNull(presentedToken);

        lock (gate)
        {
            PruneExpiredFailures();
            if (recentFailures.Count >= Constants.LocalConnectionTokenMaxFailuresPerWindow)
            {
                return false;
            }

            if (currentToken is null || clock.UtcNow > expiresAtUtc)
            {
                recentFailures.Enqueue(clock.UtcNow);
                return false;
            }

            bool matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(currentToken), Encoding.UTF8.GetBytes(presentedToken));
            if (!matches)
            {
                recentFailures.Enqueue(clock.UtcNow);
                return false;
            }

            currentToken = null;
            return true;
        }
    }

    /// <summary>Drops failure timestamps that have aged out of the rate-limit window.</summary>
    private void PruneExpiredFailures()
    {
        DateTimeOffset windowStart = clock.UtcNow - Constants.LocalConnectionTokenFailureWindow;
        while (recentFailures.Count > 0 && recentFailures.Peek() < windowStart)
        {
            recentFailures.Dequeue();
        }
    }
}
