using DovahLink.Host.Authentication;
using DovahLink.Host.Time;

namespace DovahLink.Host.Client.Authentication;

/// <summary>
/// Tracks failed <c>trusted_device_credential</c> hello attempts and rejects further attempts once
/// the bounded failure window's limit is reached, per <c>ai/context/protocol/security.md</c>'s
/// "Maintain a separate failed trusted-credential throttle from the developer-token throttle": a
/// malformed or non-matching trusted credential consumes only this budget, never
/// <see cref="ILocalConnectionTokenAuthenticator"/>'s own developer-token budget, and an
/// <c>unpaired</c> hello has no credential budget to consume at all. Global across every connection,
/// mirroring <see cref="ILocalConnectionTokenAuthenticator"/>'s own globally rate-limited failed
/// attempts rather than tracking per-client or per-connection state.
/// </summary>
public interface ITrustedCredentialFailureThrottle
{
    /// <summary>
    /// Reports whether another trusted-credential verification attempt may proceed right now, without
    /// itself recording anything. A caller checks this before attempting verification and calls
    /// <see cref="RecordFailure"/> only if that attempt then fails.
    /// </summary>
    /// <returns><see langword="true"/> when the failure window has not yet reached its limit.</returns>
    bool IsAllowed();

    /// <summary>Records one failed trusted-credential verification attempt.</summary>
    void RecordFailure();

    /// <summary>
    /// Atomically checks the failure window and, only while still allowed, runs
    /// <paramref name="verifyCredential"/> and records a failure on a mismatch. Closes the gap a
    /// separate <see cref="IsAllowed"/> check followed later by <see cref="RecordFailure"/> leaves
    /// open: under that split API, several concurrent callers can each observe the window as open
    /// before any of their outcomes are recorded, letting more attempts through than the configured
    /// bound allows. <paramref name="verifyCredential"/> must be a fast, non-blocking,
    /// side-effect-free comparison: it runs while this throttle's internal lock is held.
    /// </summary>
    /// <param name="verifyCredential">Performs the actual credential comparison; invoked at most once, and only when the window still allows an attempt.</param>
    /// <returns>
    /// <see langword="true"/> only when the window allowed the attempt and
    /// <paramref name="verifyCredential"/> returned <see langword="true"/>; otherwise
    /// <see langword="false"/>, having recorded a failure when the window allowed the attempt but the
    /// credential did not match.
    /// </returns>
    bool TryAttempt(Func<bool> verifyCredential);
}

/// <inheritdoc cref="ITrustedCredentialFailureThrottle"/>
public sealed class TrustedCredentialFailureThrottle : ITrustedCredentialFailureThrottle
{
    /// <summary>The time source used for the failure-rate window.</summary>
    private readonly IClock clock;

    /// <summary>Guards <see cref="recentFailures"/> against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>The UTC time of each failed attempt still inside the rate-limit window, oldest first.</summary>
    private readonly Queue<DateTimeOffset> recentFailures = new();

    /// <summary>Creates a trusted-credential failure throttle.</summary>
    /// <param name="clock">The time source used for the failure-rate window.</param>
    public TrustedCredentialFailureThrottle(IClock clock)
    {
        this.clock = clock;
    }

    /// <inheritdoc/>
    public bool IsAllowed()
    {
        lock (gate)
        {
            PruneExpiredFailures();
            return recentFailures.Count < Constants.TrustedCredentialMaxFailuresPerWindow;
        }
    }

    /// <inheritdoc/>
    public void RecordFailure()
    {
        lock (gate)
        {
            PruneExpiredFailures();
            recentFailures.Enqueue(clock.UtcNow);
        }
    }

    /// <inheritdoc/>
    public bool TryAttempt(Func<bool> verifyCredential)
    {
        lock (gate)
        {
            PruneExpiredFailures();
            if (recentFailures.Count >= Constants.TrustedCredentialMaxFailuresPerWindow)
            {
                return false;
            }

            if (verifyCredential())
            {
                return true;
            }

            recentFailures.Enqueue(clock.UtcNow);
            return false;
        }
    }

    /// <summary>Drops failure timestamps that have aged out of the rate-limit window.</summary>
    private void PruneExpiredFailures()
    {
        DateTimeOffset windowStart = clock.UtcNow - Constants.TrustedCredentialFailureWindow;
        while (recentFailures.Count > 0 && recentFailures.Peek() < windowStart)
        {
            recentFailures.Dequeue();
        }
    }
}
