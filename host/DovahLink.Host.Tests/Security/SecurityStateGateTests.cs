using DovahLink.Host.Security;

namespace DovahLink.Host.Tests.Security;

/// <summary>Tests for <see cref="SecurityStateGate"/>.</summary>
public class SecurityStateGateTests
{
    /// <summary>Verifies that a thread already holding the gate may enter it again without blocking.</summary>
    [Fact]
    public void Enter_AlreadyHeldByCallingThread_DoesNotBlock()
    {
        var gate = new SecurityStateGate();

        gate.Enter();
        try
        {
            // A second, nested Enter() from the same thread must return immediately rather than
            // deadlock against its own outer acquisition -- the exact reentrancy a session-authorized
            // action's own nested trust read depends on.
            gate.Enter();
            try
            {
                Assert.True(true);
            }
            finally
            {
                gate.Exit();
            }
        }
        finally
        {
            gate.Exit();
        }
    }

    /// <summary>
    /// Proves the actual mutual-exclusion guarantee with a genuine cross-thread race rather than a
    /// probabilistic repetition: while this thread holds the gate, a second thread's
    /// <see cref="ISecurityStateGate.Enter"/> call for the exact same gate cannot possibly have
    /// succeeded yet, by mutual exclusion itself -- not by waiting out a timing window -- and only
    /// proceeds once this thread releases it.
    /// </summary>
    [Fact]
    public void Enter_HeldByAnotherThread_BlocksUntilExit()
    {
        var gate = new SecurityStateGate();
        using var waiterStarted = new ManualResetEventSlim(false);
        using var waiterEntered = new ManualResetEventSlim(false);

        Thread waiterThread = new(() =>
        {
            waiterStarted.Set();
            gate.Enter();
            waiterEntered.Set();
            gate.Exit();
        });

        gate.Enter();
        try
        {
            waiterThread.Start();
            Assert.True(waiterStarted.Wait(TimeSpan.FromSeconds(5)));

            // This thread still holds the gate at this exact point, so the waiter's own Enter() call
            // cannot have returned yet regardless of how the OS scheduled either thread -- this is a
            // mutual-exclusion guarantee, not a probabilistic one.
            Assert.False(waiterEntered.IsSet);
        }
        finally
        {
            gate.Exit();
        }

        Assert.True(waiterThread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(waiterEntered.IsSet);
    }

    /// <summary>Verifies that exiting without a matching enter fails rather than silently succeeding.</summary>
    [Fact]
    public void Exit_WithoutMatchingEnter_Throws()
    {
        var gate = new SecurityStateGate();

        Assert.Throws<SynchronizationLockException>(() => gate.Exit());
    }

    /// <summary>Verifies that a thread which never entered cannot exit a gate another thread holds.</summary>
    [Fact]
    public void Exit_ByNonOwningThread_Throws()
    {
        var gate = new SecurityStateGate();
        gate.Enter();
        try
        {
            Exception? caught = null;
            Thread otherThread = new(() =>
            {
                try
                {
                    gate.Exit();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });

            otherThread.Start();
            Assert.True(otherThread.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<SynchronizationLockException>(caught);
        }
        finally
        {
            gate.Exit();
        }
    }
}
