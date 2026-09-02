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

    /// <summary>Verifies that TryValidate succeeds for the correct token without consuming it.</summary>
    [Fact]
    public void TryValidate_CorrectToken_SucceedsWithoutConsuming()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        Assert.True(authenticator.TryValidate(token));
        Assert.True(authenticator.TryValidate(token));
    }

    /// <summary>Verifies that TryValidate fails for the wrong token and records the failure the same way TryConsume does.</summary>
    [Fact]
    public void TryValidate_WrongToken_FailsAndCountsTowardTheThrottle()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            Assert.False(authenticator.TryValidate("wrong"));
        }

        Assert.False(authenticator.TryValidate(token));
    }

    /// <summary>Verifies that CommitConsumption after a successful TryValidate ends the token's validity.</summary>
    [Fact]
    public void CommitConsumption_AfterSuccessfulValidate_EndsTokenValidity()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token));

        authenticator.CommitConsumption();

        Assert.False(authenticator.TryValidate(token));
    }

    /// <summary>
    /// Verifies the exact scenario this two-phase split exists for: a validated token that is never
    /// committed (because the caller's own admission failed for an unrelated reason) remains valid
    /// for a legitimate retry.
    /// </summary>
    [Fact]
    public void TryValidate_NotCommitted_TokenRemainsValidForRetry()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());
        string token = authenticator.IssueToken();
        Assert.True(authenticator.TryValidate(token));

        // Simulated: the caller's own admission failed downstream, so it never calls CommitConsumption.

        Assert.True(authenticator.TryValidate(token));
        authenticator.CommitConsumption();
        Assert.False(authenticator.TryValidate(token));
    }

    /// <summary>Verifies that CommitConsumption is safe to call even when nothing is currently issued.</summary>
    [Fact]
    public void CommitConsumption_NothingIssued_DoesNotThrow()
    {
        var authenticator = new LocalConnectionTokenAuthenticator(new FakeClock());

        authenticator.CommitConsumption();
    }
}
