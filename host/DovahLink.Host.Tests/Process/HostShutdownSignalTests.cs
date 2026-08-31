using DovahLink.Host.Process;

namespace DovahLink.Host.Tests.Process;

/// <summary>Tests for <see cref="NamedEventHostShutdownSignal"/>.</summary>
public class HostShutdownSignalTests
{
    /// <summary>The default time allowed for an expected-to-complete wait.</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The bounded time allowed to observe an expected-to-still-be-pending wait.</summary>
    private static readonly TimeSpan StillPendingWindow = TimeSpan.FromMilliseconds(200);

    /// <summary>Verifies that signaling the named event (as the adapter would, from a separate handle) completes the wait.</summary>
    [Fact]
    public async Task WaitAsync_Signaled_Completes()
    {
        string eventName = UniqueEventName();
        using var signal = new NamedEventHostShutdownSignal(eventName);
        Task waitTask = signal.WaitAsync();

        using var adapterSideHandle = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        adapterSideHandle.Set();

        await waitTask.WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies that cancellation completes the wait without the signal ever being set.</summary>
    [Fact]
    public async Task WaitAsync_Cancelled_CompletesWithoutSignal()
    {
        using var signal = new NamedEventHostShutdownSignal(UniqueEventName());
        using var cancellation = new CancellationTokenSource();

        Task waitTask = signal.WaitAsync(cancellation.Token);
        cancellation.Cancel();

        await waitTask.WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies that a token already cancelled before WaitAsync is called still completes the wait, without throwing.</summary>
    [Fact]
    public async Task WaitAsync_AlreadyCancelledBeforeCall_CompletesImmediately()
    {
        using var signal = new NamedEventHostShutdownSignal(UniqueEventName());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await signal.WaitAsync(cancellation.Token).WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies that disposing the signal twice is a harmless, idempotent no-op.</summary>
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var signal = new NamedEventHostShutdownSignal(UniqueEventName());

        signal.Dispose();
        signal.Dispose();
    }

    /// <summary>Verifies that a signal already set before waiting begins is observed immediately.</summary>
    [Fact]
    public async Task WaitAsync_AlreadySignaledBeforeWaiting_CompletesImmediately()
    {
        string eventName = UniqueEventName();
        using var adapterSideHandle = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        adapterSideHandle.Set();
        using var signal = new NamedEventHostShutdownSignal(eventName);

        await signal.WaitAsync().WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies that setting the event more than once is a harmless, idempotent no-op for a still-pending wait.</summary>
    [Fact]
    public async Task WaitAsync_SignaledRepeatedly_IsSafe()
    {
        string eventName = UniqueEventName();
        using var signal = new NamedEventHostShutdownSignal(eventName);
        using var adapterSideHandle = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);

        adapterSideHandle.Set();
        adapterSideHandle.Set();

        await signal.WaitAsync().WaitAsync(CompletionTimeout);
    }

    /// <summary>Verifies that signaling one lifetime's named event never wakes a different lifetime's wait.</summary>
    [Fact]
    public async Task DifferentEventNames_SignalingOneNeverWakesTheOther()
    {
        string eventNameA = UniqueEventName();
        string eventNameB = UniqueEventName();
        using var signalA = new NamedEventHostShutdownSignal(eventNameA);
        using var signalB = new NamedEventHostShutdownSignal(eventNameB);
        Task waitTaskA = signalA.WaitAsync();
        Task waitTaskB = signalB.WaitAsync();

        // Signal only A's underlying event, exactly as the adapter would for one specific lifetime.
        using var adapterSideHandleA = new EventWaitHandle(false, EventResetMode.ManualReset, eventNameA);
        adapterSideHandleA.Set();

        await waitTaskA.WaitAsync(CompletionTimeout);
        Task completedFirst = await Task.WhenAny(waitTaskB, Task.Delay(StillPendingWindow));
        Assert.NotEqual(waitTaskB, completedFirst);
    }

    /// <summary>Builds a unique per-test event name, so parallel test runs and repeated runs never collide.</summary>
    private static string UniqueEventName() => $@"Local\DovahLinkTest.Shutdown.{Guid.NewGuid():N}";
}
