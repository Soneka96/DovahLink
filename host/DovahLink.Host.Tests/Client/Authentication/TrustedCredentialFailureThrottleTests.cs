using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Authentication;

/// <summary>Tests for <see cref="TrustedCredentialFailureThrottle"/>.</summary>
public class TrustedCredentialFailureThrottleTests
{
    /// <summary>Verifies that a fresh throttle with no recorded failures allows an attempt.</summary>
    [Fact]
    public void IsAllowed_NoFailuresRecorded_ReturnsTrue()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        Assert.True(throttle.IsAllowed());
    }

    /// <summary>Verifies that the attempt following the fifth recorded failure within the window is rejected.</summary>
    [Fact]
    public void IsAllowed_AfterFiveFailuresWithinWindow_ReturnsFalse()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        for (int i = 0; i < 5; i++)
        {
            throttle.RecordFailure();
        }

        Assert.False(throttle.IsAllowed());
    }

    /// <summary>Verifies that fewer than five recorded failures still allows another attempt.</summary>
    [Fact]
    public void IsAllowed_FourFailuresWithinWindow_ReturnsTrue()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        for (int i = 0; i < 4; i++)
        {
            throttle.RecordFailure();
        }

        Assert.True(throttle.IsAllowed());
    }

    /// <summary>Verifies the exact boundary transition: the fourth failure still allows an attempt, and the fifth trips the limit.</summary>
    [Fact]
    public void IsAllowed_ExactFourToFiveFailureTransition_FlipsFromTrueToFalse()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        for (int i = 0; i < 4; i++)
        {
            throttle.RecordFailure();
        }

        Assert.True(throttle.IsAllowed());

        throttle.RecordFailure();

        Assert.False(throttle.IsAllowed());
    }

    /// <summary>
    /// Verifies the sliding window prunes only the failures that have individually aged out rather
    /// than the whole queue at once: after the oldest of five staggered failures ages past the
    /// window, exactly that one is dropped, and the remaining four fall back under the limit.
    /// </summary>
    [Fact]
    public void IsAllowed_StaggeredFailures_OnlyTheIndividuallyExpiredOneIsPruned()
    {
        var clock = new FakeClock();
        var throttle = new TrustedCredentialFailureThrottle(clock);

        throttle.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(59));
        for (int i = 0; i < 4; i++)
        {
            throttle.RecordFailure();
        }

        Assert.False(throttle.IsAllowed());

        // Advancing 2 more seconds ages the very first failure (recorded 61 seconds ago) out of the
        // 60-second window, while the four recorded a second later (now 59 seconds ago) remain --
        // proving pruning drops exactly the expired entry rather than clearing the whole queue or
        // requiring every entry to expire together.
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(throttle.IsAllowed());
    }

    /// <summary>Verifies that once the failure window elapses, attempts are allowed again.</summary>
    [Fact]
    public void IsAllowed_AfterFailureWindowElapses_ReturnsTrueAgain()
    {
        var clock = new FakeClock();
        var throttle = new TrustedCredentialFailureThrottle(clock);
        for (int i = 0; i < 5; i++)
        {
            throttle.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(throttle.IsAllowed());
    }

    /// <summary>Verifies that a failure exactly 60 seconds old still counts toward the window (the window boundary is inclusive, matching this codebase's other expiry checks).</summary>
    [Fact]
    public void IsAllowed_AtExactWindowBoundary_StillRateLimited()
    {
        var clock = new FakeClock();
        var throttle = new TrustedCredentialFailureThrottle(clock);
        for (int i = 0; i < 5; i++)
        {
            throttle.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.False(throttle.IsAllowed());
    }

    /// <summary>
    /// Verifies this throttle's own state is entirely independent of a separately instantiated
    /// developer-token authenticator -- proving the two failure budgets never share state, per
    /// <c>ai/context/protocol/security.md</c>'s "Maintain a separate failed trusted-credential
    /// throttle from the developer-token throttle."
    /// </summary>
    [Fact]
    public void RecordFailure_DoesNotAffectASeparateDeveloperTokenAuthenticatorInstance()
    {
        var clock = new FakeClock();
        var throttle = new TrustedCredentialFailureThrottle(clock);
        var developerTokenAuthenticator = new DovahLink.Host.Authentication.LocalConnectionTokenAuthenticator(clock);
        string token = developerTokenAuthenticator.IssueToken();

        for (int i = 0; i < 5; i++)
        {
            throttle.RecordFailure();
        }

        Assert.True(developerTokenAuthenticator.TryConsume(token));
    }

    /// <summary>Verifies that of several concurrent failures, every one is recorded and the limit still trips correctly.</summary>
    [Fact]
    public async Task RecordFailure_ConcurrentCalls_AllCountTowardTheLimit()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => throttle.RecordFailure())));

        Assert.False(throttle.IsAllowed());
    }

    /// <summary>Verifies that a successful verify returns true and records no failure.</summary>
    [Fact]
    public void TryAttempt_VerifySucceeds_ReturnsTrueAndRecordsNoFailure()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        Assert.True(throttle.TryAttempt(() => true));

        Assert.True(throttle.IsAllowed());
        for (int i = 0; i < 4; i++)
        {
            throttle.RecordFailure();
        }
        Assert.True(throttle.IsAllowed());
    }

    /// <summary>Verifies that a failing verify returns false and records exactly one failure.</summary>
    [Fact]
    public void TryAttempt_VerifyFails_ReturnsFalseAndRecordsOneFailure()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());

        Assert.False(throttle.TryAttempt(() => false));

        for (int i = 0; i < 3; i++)
        {
            throttle.RecordFailure();
        }
        Assert.True(throttle.IsAllowed());
        throttle.RecordFailure();
        Assert.False(throttle.IsAllowed());
    }

    /// <summary>Verifies that once the window is already exhausted, TryAttempt returns false without ever invoking the verify callback.</summary>
    [Fact]
    public void TryAttempt_WindowAlreadyExhausted_ReturnsFalseWithoutInvokingVerify()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());
        for (int i = 0; i < 5; i++)
        {
            throttle.RecordFailure();
        }

        bool verifyInvoked = false;
        Assert.False(throttle.TryAttempt(() => { verifyInvoked = true; return true; }));

        Assert.False(verifyInvoked);
    }

    /// <summary>
    /// Verifies the exact race the split IsAllowed()/RecordFailure() API allowed: with many concurrent
    /// TryAttempt calls all presenting a failing verify, the actual verify callback is invoked at most
    /// five times in total -- this suite's configured failure bound (see the other tests above). Under
    /// the old split API, every concurrent caller could observe an open gate and proceed to attempt
    /// verification before any outcome was recorded, letting far more than the bound's worth of
    /// verification attempts through; a concurrency proof against RecordFailure alone (already covered
    /// above) does not exercise that gate-then-verify gap at all.
    /// </summary>
    [Fact]
    public async Task TryAttempt_ManyConcurrentFailingAttempts_InvokesVerifyAtMostTheConfiguredBoundTimes()
    {
        var throttle = new TrustedCredentialFailureThrottle(new FakeClock());
        int verifyInvocations = 0;

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            throttle.TryAttempt(() =>
            {
                Interlocked.Increment(ref verifyInvocations);
                return false;
            }))));

        Assert.All(results, Assert.False);
        Assert.Equal(5, verifyInvocations);
        Assert.False(throttle.IsAllowed());
    }
}
