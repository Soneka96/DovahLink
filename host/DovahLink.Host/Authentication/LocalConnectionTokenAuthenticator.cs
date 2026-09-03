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
    /// succeeding. A caller that receives <see langword="true"/> owns the sole reservation, stamped
    /// with the exact token issuance it validated, and must call exactly one of
    /// <see cref="CommitConsumption"/> (once its own admission succeeds) or
    /// <see cref="RollbackReservation"/> (if admission fails for an unrelated reason, for example the
    /// session slot is full) to resolve it -- per <c>ai/context/protocol/security.md</c>'s "commit a
    /// successful one-time developer token only after session admission succeeds" and "a full session
    /// slot must not consume a retryable one-time token" -- while still guaranteeing the token can
    /// never authenticate more than one admitted connection concurrently, independent of how many
    /// active sessions the host is configured to admit. Stamping the reservation with the issuance it
    /// was validated against (rather than a bare outstanding/not-outstanding flag) means a caller
    /// holding a reservation from a superseded issuance can never affect a newer one: see
    /// <see cref="LocalConnectionTokenReservation"/>.
    /// </summary>
    /// <param name="presentedToken">The token presented by a connecting client.</param>
    /// <param name="reservation">
    /// The reservation stamped with this token's exact issuance, valid only when this method returns
    /// <see langword="true"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the presented token matched the current, unexpired token and no other
    /// reservation was already outstanding.
    /// </returns>
    bool TryValidate(string presentedToken, out LocalConnectionTokenReservation reservation);

    /// <summary>
    /// Consumes the token, ending its validity and releasing the reservation, but only if
    /// <paramref name="reservation"/> still identifies the single outstanding reservation's exact
    /// token issuance. A reservation from a superseded issuance (the token was reissued, or this
    /// reservation was already resolved) is a safe no-op: it can never consume a different token than
    /// the one it was stamped against. Safe to call even when nothing is currently issued or reserved.
    /// </summary>
    /// <param name="reservation">The reservation a prior <see cref="TryValidate"/> call returned.</param>
    void CommitConsumption(LocalConnectionTokenReservation reservation);

    /// <summary>
    /// Releases the reservation without consuming the token, so a later legitimate retry (by this
    /// caller or another) can validate it again, but only if <paramref name="reservation"/> still
    /// identifies the single outstanding reservation's exact token issuance -- the same
    /// superseded-issuance safe-no-op guarantee as <see cref="CommitConsumption"/>. Intended for a
    /// caller whose own admission failed downstream for a reason unrelated to the token itself. Safe
    /// to call even when no reservation is outstanding.
    /// </summary>
    /// <param name="reservation">The reservation a prior <see cref="TryValidate"/> call returned.</param>
    void RollbackReservation(LocalConnectionTokenReservation reservation);
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
    /// The token issuance currently in effect. Bumped by every <see cref="IssueToken"/> call so each
    /// issuance has its own identity; starts at <c>0</c>; the first issuance is generation <c>1</c>,
    /// matching <see cref="LocalConnectionTokenReservation"/>'s documented "default value can never
    /// match a real issuance" guarantee.
    /// </summary>
    private long currentGeneration;

    /// <summary>
    /// The token issuance a prior <see cref="TryValidate"/> call's reservation is still outstanding
    /// for (neither committed nor rolled back), or <see langword="null"/> if none is outstanding.
    /// While non-null, no other <see cref="TryValidate"/> call can succeed, even for the identical
    /// correct token -- this is what keeps the token single-use under concurrency independent of how
    /// many sessions the host admits at once. Tracking the exact generation, rather than a bare
    /// outstanding/not-outstanding flag, is what lets <see cref="CommitConsumption"/> and
    /// <see cref="RollbackReservation"/> safely ignore a reservation from a superseded issuance
    /// instead of acting on whatever reservation happens to be outstanding now.
    /// </summary>
    private long? reservedGeneration;

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
            currentGeneration++;
            currentToken = token;
            expiresAtUtc = clock.UtcNow + Constants.LocalConnectionTokenLifetime;
            // A new issuance immediately supersedes any reservation still outstanding on the prior
            // one: that reservation's generation no longer matches currentGeneration, so a later
            // CommitConsumption/RollbackReservation call presenting it becomes a safe no-op instead of
            // acting on this new issuance's state.
            reservedGeneration = null;
        }

        return token;
    }

    /// <inheritdoc/>
    public bool TryConsume(string presentedToken)
    {
        lock (gate)
        {
            if (reservedGeneration is not null)
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
    public bool TryValidate(string presentedToken, out LocalConnectionTokenReservation reservation)
    {
        lock (gate)
        {
            if (reservedGeneration is not null)
            {
                // Another caller's reservation is already outstanding for this token: this is a
                // losing race against the correct token, not a wrong secret, so no failure is
                // recorded against the rate limit.
                reservation = default;
                return false;
            }

            if (!MatchesLocked(presentedToken))
            {
                reservation = default;
                return false;
            }

            reservedGeneration = currentGeneration;
            reservation = new LocalConnectionTokenReservation(currentGeneration);
            return true;
        }
    }

    /// <inheritdoc/>
    public void CommitConsumption(LocalConnectionTokenReservation reservation)
    {
        lock (gate)
        {
            if (reservedGeneration != reservation.Generation)
            {
                // Stale reservation: either already resolved, or superseded by a later IssueToken
                // call. Acting here would consume a token this reservation never validated.
                return;
            }

            currentToken = null;
            reservedGeneration = null;
        }
    }

    /// <inheritdoc/>
    public void RollbackReservation(LocalConnectionTokenReservation reservation)
    {
        lock (gate)
        {
            if (reservedGeneration != reservation.Generation)
            {
                // Stale reservation: see CommitConsumption's identical guard. Clearing here would
                // release a different, newer reservation this caller never held.
                return;
            }

            reservedGeneration = null;
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
