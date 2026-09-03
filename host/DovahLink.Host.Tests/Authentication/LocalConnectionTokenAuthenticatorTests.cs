using DovahLink.Host.Authentication;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Authentication;

/// <summary>Tests for <see cref="LocalConnectionTokenAuthenticator"/>.</summary>
public class LocalConnectionTokenAuthenticatorTests
{
    /// <summary>Verifies that presenting the exact issued token succeeds.</summary>
    [Fact]
    public void TryConsume_CorrectToken_Succeeds()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        Assert.True(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that presenting the wrong token fails.</summary>
    [Fact]
    public void TryConsume_WrongToken_Fails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        authenticator.IssueToken();

        Assert.False(authenticator.TryConsume("not-the-token"));
    }

    /// <summary>Verifies that presenting a token before any has ever been issued fails rather than throwing.</summary>
    [Fact]
    public void TryConsume_NoTokenIssuedYet_Fails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());

        Assert.False(authenticator.TryConsume("anything"));
    }

    /// <summary>Verifies that the token can no longer be consumed once its lifetime has elapsed.</summary>
    [Fact]
    public void TryConsume_AfterExpiry_Fails()
    {
        var clock = new FakeClock();
        var authenticator = new LocalConnectionTokenAuthenticator(clock);
        string token = authenticator.IssueToken();

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.False(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that a token confirmed at the exact moment it expires is still accepted (expiry is exclusive).</summary>
    [Fact]
    public void TryConsume_AtExactExpiryMoment_StillAccepted()
    {
        var clock = new FakeClock();
        var authenticator = new LocalConnectionTokenAuthenticator(clock);
        string token = authenticator.IssueToken();

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(authenticator.TryConsume(token));
    }

    /// <summary>Verifies single-use: a token that already succeeded once can never be consumed again.</summary>
    [Fact]
    public void TryConsume_SameTokenTwice_SecondAttemptFails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryConsume(token));

        Assert.False(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that issuing a new token invalidates a still-unconsumed previous one.</summary>
    [Fact]
    public void IssueToken_CalledAgain_InvalidatesThePriorToken()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string firstToken = authenticator.IssueToken();
        authenticator.IssueToken();

        Assert.False(authenticator.TryConsume(firstToken));
    }

    /// <summary>Verifies that the sixth failed attempt within the rate-limit window is rejected outright.</summary>
    [Fact]
    public void TryConsume_SixthFailureWithinWindow_IsRateLimited()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            Assert.False(authenticator.TryConsume("wrong"));
        }

        Assert.False(authenticator.TryConsume("wrong"));
    }

    /// <summary>
    /// Verifies that once rate-limited, even the correct token is rejected until the window
    /// clears -- a rate limit that let a correct guess through defeats its own purpose.
    /// </summary>
    [Fact]
    public void TryConsume_RateLimited_RejectsEvenTheCorrectToken()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            authenticator.TryConsume("wrong");
        }

        Assert.False(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that once the failure window elapses, attempts are accepted again.</summary>
    [Fact]
    public void TryConsume_AfterFailureWindowElapses_AcceptsCorrectTokenAgain()
    {
        var clock = new FakeClock();
        var authenticator = new LocalConnectionTokenAuthenticator(clock);
        string token = authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            authenticator.TryConsume("wrong");
        }

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that a failure exactly 60 seconds old still counts toward the window (the window boundary is inclusive, matching this codebase's other expiry checks).</summary>
    [Fact]
    public void TryConsume_AtExactWindowBoundary_StillRateLimited()
    {
        var clock = new FakeClock();
        var authenticator = new LocalConnectionTokenAuthenticator(clock);
        string token = authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            authenticator.TryConsume("wrong");
        }

        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.False(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that presenting a null token is rejected explicitly rather than crashing inside the comparison logic.</summary>
    [Fact]
    public void TryConsume_NullToken_ThrowsArgumentNullException()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        authenticator.IssueToken();

        Assert.Throws<ArgumentNullException>(() => authenticator.TryConsume(null!));
    }

    /// <summary>Verifies that presenting an empty token fails rather than crashing or matching.</summary>
    [Fact]
    public void TryConsume_EmptyToken_Fails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        authenticator.IssueToken();

        Assert.False(authenticator.TryConsume(string.Empty));
    }

    /// <summary>
    /// Verifies that issuing a new token does not reset the failure counter -- an attacker who has
    /// just been rate-limited cannot reset their own limit by forcing a fresh token to be issued.
    /// </summary>
    [Fact]
    public void IssueToken_AfterRateLimitTripped_DoesNotResetFailureCounter()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        authenticator.IssueToken();
        for (int i = 0; i < 5; i++)
        {
            authenticator.TryConsume("wrong");
        }

        string newToken = authenticator.IssueToken();

        Assert.False(authenticator.TryConsume(newToken));
    }

    /// <summary>Verifies that of several concurrent attempts presenting the same correct token, only one succeeds.</summary>
    [Fact]
    public async Task TryConsume_ConcurrentAttemptsWithSameCorrectToken_OnlyOneSucceeds()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => authenticator.TryConsume(token))));

        Assert.Single(results, succeeded => succeeded);
    }

    /// <summary>Verifies that TryValidate succeeds for the correct token without consuming it -- provable by rolling back and validating again.</summary>
    [Fact]
    public void TryValidate_CorrectToken_SucceedsWithoutConsuming()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));
        authenticator.RollbackReservation(reservation);

        Assert.True(authenticator.TryValidate(token, out _));
    }

    /// <summary>Verifies that a second TryValidate call presenting the identical correct token fails while a prior reservation is still outstanding.</summary>
    [Fact]
    public void TryValidate_ReservationOutstanding_SecondCallWithSameCorrectTokenFails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out _));

        Assert.False(authenticator.TryValidate(token, out _));
    }

    /// <summary>Verifies that a second TryValidate call rejected only because a reservation was already outstanding does not count toward the failure throttle.</summary>
    [Fact]
    public void TryValidate_ReservationOutstanding_DoesNotCountTowardTheFailureThrottle()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));

        for (int i = 0; i < 10; i++)
        {
            Assert.False(authenticator.TryValidate(token, out _));
        }

        authenticator.RollbackReservation(reservation);
        Assert.True(authenticator.TryValidate(token, out _));
    }

    /// <summary>
    /// Verifies the exact mixed-path hazard this fix closes: a reservation held by TryValidate
    /// cannot be bypassed by calling TryConsume instead, and once the reservation is released,
    /// TryConsume works normally again.
    /// </summary>
    [Fact]
    public void TryConsume_ReservationOutstandingFromTryValidate_FailsUntilRolledBack()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));

        Assert.False(authenticator.TryConsume(token));

        authenticator.RollbackReservation(reservation);
        Assert.True(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that TryConsume rejects even the correct token while a reservation is outstanding, without recording a rate-limit failure (the reservation guard runs before the secret comparison).</summary>
    [Fact]
    public void TryConsume_WrongTokenWhileReserved_FailsWithoutRecordingAFailure()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));

        for (int i = 0; i < 10; i++)
        {
            Assert.False(authenticator.TryConsume("wrong"));
        }

        authenticator.RollbackReservation(reservation);
        Assert.True(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that TryConsume still fails after a reservation is committed, since CommitConsumption already nulled the token.</summary>
    [Fact]
    public void TryConsume_AfterCommitConsumption_Fails()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));
        authenticator.CommitConsumption(reservation);

        Assert.False(authenticator.TryConsume(token));
    }

    /// <summary>Verifies that TryValidate fails for the wrong token and records the failure the same way TryConsume does.</summary>
    [Fact]
    public void TryValidate_WrongToken_FailsAndCountsTowardTheThrottle()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            Assert.False(authenticator.TryValidate("wrong", out _));
        }

        Assert.False(authenticator.TryValidate(token, out _));
    }

    /// <summary>Verifies that CommitConsumption after a successful TryValidate ends the token's validity.</summary>
    [Fact]
    public void CommitConsumption_AfterSuccessfulValidate_EndsTokenValidity()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));

        authenticator.CommitConsumption(reservation);

        Assert.False(authenticator.TryValidate(token, out _));
    }

    /// <summary>
    /// Verifies the exact scenario this two-phase split exists for: a validated token whose caller's
    /// own admission failed downstream for an unrelated reason (for example the session slot is full)
    /// rolls back its reservation, and the token remains valid for a legitimate retry.
    /// </summary>
    [Fact]
    public void TryValidate_RolledBackAfterDownstreamFailure_TokenRemainsValidForRetry()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));

        // Simulated: the caller's own admission failed downstream for an unrelated reason.
        authenticator.RollbackReservation(reservation);

        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation secondReservation));
        authenticator.CommitConsumption(secondReservation);
        Assert.False(authenticator.TryValidate(token, out _));
    }

    /// <summary>Verifies that RollbackReservation is safe to call even when no reservation is outstanding.</summary>
    [Fact]
    public void RollbackReservation_NothingReserved_DoesNotThrow()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());

        authenticator.RollbackReservation(default);
    }

    /// <summary>Verifies that issuing a fresh token releases a still-outstanding reservation on the prior one, rather than leaving a stale reservation that could never be validated again.</summary>
    [Fact]
    public void IssueToken_ReservationOutstandingOnPriorToken_ReleasesIt()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string firstToken = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(firstToken, out _));

        string secondToken = authenticator.IssueToken();

        Assert.True(authenticator.TryValidate(secondToken, out _));
    }

    /// <summary>
    /// Verifies the invariant this whole reservation lifecycle exists to guarantee: of many concurrent
    /// TryValidate attempts presenting the one correct token, exactly one succeeds -- independent of
    /// how many active sessions the host is configured to admit, which is a session-registry concern
    /// unrelated to this authenticator's own single-use guarantee.
    /// </summary>
    [Fact]
    public async Task TryValidate_ConcurrentAttemptsWithSameCorrectToken_OnlyOneSucceeds()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => authenticator.TryValidate(token, out _))));

        Assert.Single(results, succeeded => succeeded);
    }

    /// <summary>Verifies that CommitConsumption is safe to call even when nothing is currently issued.</summary>
    [Fact]
    public void CommitConsumption_NothingIssued_DoesNotThrow()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());

        authenticator.CommitConsumption(default);
    }

    /// <summary>Verifies that rolling back an already-committed reservation is a safe no-op: the reservation's generation no longer matches (it was cleared by the commit), so it cannot re-open or otherwise disturb the now-consumed token.</summary>
    [Fact]
    public void RollbackReservation_AfterAlreadyCommitted_DoesNotThrow()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));
        authenticator.CommitConsumption(reservation);

        authenticator.RollbackReservation(reservation);

        Assert.False(authenticator.TryConsume(token)); // still consumed; the redundant rollback changed nothing
    }

    /// <summary>Verifies that committing an already-rolled-back reservation is a safe no-op and does not resurrect or otherwise consume the token.</summary>
    [Fact]
    public void CommitConsumption_AfterAlreadyRolledBack_DoesNotThrow()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token, out LocalConnectionTokenReservation reservation));
        authenticator.RollbackReservation(reservation);

        authenticator.CommitConsumption(reservation);

        Assert.True(authenticator.TryConsume(token)); // still valid; the redundant commit changed nothing
    }

    /// <summary>
    /// Verifies the exact ABA hazard this generation-scoped design closes: a reservation from a
    /// superseded issuance can never commit a newer one. Issuing token B after reserving token A
    /// immediately supersedes A's reservation; committing the stale reservationA is a no-op, and
    /// tokenB remains fully valid for a legitimate caller to reserve and commit afterward.
    /// </summary>
    [Fact]
    public void CommitConsumption_StaleReservationFromSupersededIssuance_DoesNotConsumeNewerToken()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string tokenA = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(tokenA, out LocalConnectionTokenReservation reservationA));

        string tokenB = authenticator.IssueToken();
        authenticator.CommitConsumption(reservationA);

        Assert.True(authenticator.TryValidate(tokenB, out LocalConnectionTokenReservation reservationB));
        authenticator.CommitConsumption(reservationB);
        Assert.False(authenticator.TryValidate(tokenB, out _));
    }

    /// <summary>
    /// Verifies the exact ABA hazard this generation-scoped design closes on the rollback side: a
    /// reservation from a superseded issuance can never clear a newer, still-legitimately-held
    /// reservation. Rolling back the stale reservationA does nothing to reservationB, which still
    /// blocks a second TryValidate(tokenB) until reservationB itself is rolled back.
    /// </summary>
    [Fact]
    public void RollbackReservation_StaleReservationFromSupersededIssuance_DoesNotClearNewerReservation()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string tokenA = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(tokenA, out LocalConnectionTokenReservation reservationA));

        string tokenB = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(tokenB, out LocalConnectionTokenReservation reservationB));

        authenticator.RollbackReservation(reservationA);

        Assert.False(authenticator.TryValidate(tokenB, out _)); // reservationB still outstanding

        authenticator.RollbackReservation(reservationB);
        Assert.True(authenticator.TryValidate(tokenB, out _));
    }

    /// <summary>
    /// Verifies the full concurrency scenario the generation-scoped design exists to guarantee:
    /// reissuing a token while an old reservation is still outstanding cannot let more than one
    /// concurrent caller successfully reserve or consume the new token. Many threads race to
    /// reserve-then-immediately-commit tokenB while tokenA's reservation is deliberately left
    /// unresolved throughout.
    /// </summary>
    [Fact]
    public async Task ReissueWhileReservationOutstanding_ConcurrentAttemptsOnNewToken_OnlyOneSucceeds()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string tokenA = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(tokenA, out _)); // left outstanding, never resolved

        string tokenB = authenticator.IssueToken();

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            if (!authenticator.TryValidate(tokenB, out LocalConnectionTokenReservation reservation))
            {
                return false;
            }

            authenticator.CommitConsumption(reservation);
            return true;
        })));

        Assert.Single(results, succeeded => succeeded);
        Assert.False(authenticator.TryValidate(tokenB, out _)); // consumed, not merely reserved
    }
}
