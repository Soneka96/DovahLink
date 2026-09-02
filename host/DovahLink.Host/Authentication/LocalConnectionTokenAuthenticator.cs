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

    /// <summary>
    /// Checks the current token the same way <see cref="TryConsume"/> does -- including recording a
    /// failed attempt against the shared rate limit on a mismatch -- but does not itself consume a
    /// match. A caller that receives <see langword="true"/> must call
    /// <see cref="CommitConsumption"/> once its own admission succeeds; if admission fails for an
    /// unrelated reason (for example the session slot is full), simply not committing leaves the
    /// token valid for a legitimate retry, per <c>ai/context/protocol/security.md</c>'s "commit a
    /// successful one-time developer token only after session admission succeeds" and "a full
    /// session slot must not consume a retryable one-time token."
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <returns><see langword="true"/> if the presented token matched the current, unexpired token.</returns>
    bool TryValidate(string presentedToken);

    /// <summary>
    /// Consumes the token, ending its validity. Intended to follow a successful
    /// <see cref="TryValidate"/> once the caller's own admission has succeeded. Idempotent and safe
    /// to call even when nothing is currently issued.
    /// </summary>
    void CommitConsumption();
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
        lock (gate)
        {
            if (!MatchesLocked(presentedToken))
            {
                return false;
            }

            currentToken = null;
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryValidate(string presentedToken)
    {
        lock (gate)
        {
            return MatchesLocked(presentedToken);
        }
    }

    /// <inheritdoc/>
    public void CommitConsumption()
    {
        lock (gate)
        {
            currentToken = null;
        }
    }

    /// <summary>
    /// Checks <paramref name="presentedToken"/> against the current token under
    /// <see cref="gate"/>, recording a failed attempt on any mismatch, but never consuming a match
    /// itself -- shared by <see cref="TryConsume"/> (which consumes immediately after) and
    /// <see cref="TryValidate"/> (which leaves consumption to a later <see cref="CommitConsumption"/>).
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <returns><see langword="true"/> if the presented token matched the current, unexpired token.</returns>
    private bool MatchesLocked(string presentedToken)
    {
        ArgumentNullException.ThrowIfNull(presentedToken);

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

        return true;
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
