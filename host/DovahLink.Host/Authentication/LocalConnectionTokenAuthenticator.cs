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
    /// consume a given token. Also fails while a <see cref="TryValidate"/> reservation is
    /// outstanding, so this method can never consume a token out from under a caller that is still
    /// deciding whether to <see cref="CommitConsumption"/> or <see cref="RollbackReservation"/> it.
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <returns><see langword="true"/> if the presented token matched the current, unexpired token and no reservation was outstanding.</returns>
    bool TryConsume(string presentedToken);

    /// <summary>
    /// Checks the current token the same way <see cref="TryConsume"/> does -- including recording a
    /// failed attempt against the shared rate limit on a mismatch -- and, on a match, atomically
    /// reserves it so no concurrent caller can also validate the same token before this reservation is
    /// resolved: a second, simultaneous <see cref="TryValidate"/> call presenting the identical correct
    /// token returns <see langword="false"/> while a reservation is outstanding, rather than also
    /// succeeding. A caller that receives <see langword="true"/> owns the sole reservation and must
    /// call exactly one of <see cref="CommitConsumption"/> (once its own admission succeeds) or
    /// <see cref="RollbackReservation"/> (if admission fails for an unrelated reason, for example the
    /// session slot is full) to resolve it -- per <c>ai/context/protocol/security.md</c>'s "commit a
    /// successful one-time developer token only after session admission succeeds" and "a full session
    /// slot must not consume a retryable one-time token" -- while still guaranteeing the token can
    /// never authenticate more than one admitted connection concurrently, independent of how many
    /// active sessions the host is configured to admit.
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <returns>
    /// <see langword="true"/> if the presented token matched the current, unexpired token and no other
    /// reservation was already outstanding.
    /// </returns>
    bool TryValidate(string presentedToken);

    /// <summary>
    /// Consumes the token, ending its validity and releasing the reservation a prior
    /// <see cref="TryValidate"/> call placed. Idempotent and safe to call even when nothing is
    /// currently issued or reserved.
    /// </summary>
    void CommitConsumption();

    /// <summary>
    /// Releases the reservation a prior successful <see cref="TryValidate"/> call placed, without
    /// consuming the token, so a later legitimate retry (by this caller or another) can validate it
    /// again. Intended for a caller whose own admission failed downstream for a reason unrelated to
    /// the token itself. Idempotent and safe to call even when no reservation is outstanding.
    /// </summary>
    void RollbackReservation();
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

    /// <summary>
    /// Whether a prior <see cref="TryValidate"/> call's reservation is still outstanding (neither
    /// committed nor rolled back). While <see langword="true"/>, no other <see cref="TryValidate"/>
    /// call can succeed, even for the identical correct token -- this is what keeps the token
    /// single-use under concurrency independent of how many sessions the host admits at once.
    /// </summary>
    private bool reserved;

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
            reserved = false;
        }

        return token;
    }

    /// <inheritdoc/>
    public bool TryConsume(string presentedToken)
    {
        lock (gate)
        {
            if (reserved)
            {
                // A TryValidate reservation is already outstanding: this call must not consume the
                // token out from under it, and this is not a wrong-secret attempt, so no failure is
                // recorded against the rate limit.
                return false;
            }

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
            if (reserved)
            {
                // Another caller's reservation is already outstanding for this token: this is a
                // losing race against the correct token, not a wrong secret, so no failure is
                // recorded against the rate limit.
                return false;
            }

            if (!MatchesLocked(presentedToken))
            {
                return false;
            }

            reserved = true;
            return true;
        }
    }

    /// <inheritdoc/>
    public void CommitConsumption()
    {
        lock (gate)
        {
            currentToken = null;
            reserved = false;
        }
    }

    /// <inheritdoc/>
    public void RollbackReservation()
    {
        lock (gate)
        {
            reserved = false;
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
