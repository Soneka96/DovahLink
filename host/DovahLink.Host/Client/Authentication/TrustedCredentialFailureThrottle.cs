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
