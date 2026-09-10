using DovahLink.Host.Client.Transport;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="DataLaneOutboundQueue"/>.</summary>
public class DataLaneOutboundQueueTests
{
    /// <summary>A byte-budget callback that always affords the requested delta.</summary>
    private static readonly Func<long, bool> AlwaysAffordable = _ => true;

    /// <summary>A byte-budget callback that always declines the requested delta.</summary>
    private static readonly Func<long, bool> NeverAffordable = _ => false;

    /// <summary>Verifies that a fresh queue has no outstanding messages and dequeues nothing.</summary>
    [Fact]
    public void FreshQueue_HasNoOutstandingMessagesAndDequeuesNothing()
    {
        var queue = new DataLaneOutboundQueue();

        Assert.Equal(0, queue.OutstandingMessages);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that an event is admitted, reserves one outstanding slot, and dequeues in order.</summary>
    [Fact]
    public void TryAdmitEvent_WithinBound_AdmitsAndReservesOneSlot()
    {
        var queue = new DataLaneOutboundQueue();

        bool result = queue.TryAdmitEvent([1, 2, 3], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(result);
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1, 2, 3 }, payload);
    }

    /// <summary>Verifies that an event is declined once the outstanding-message bound is reached, without any change.</summary>
    [Fact]
    public void TryAdmitEvent_AtBound_DeclinesWithoutChange()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);

        bool result = queue.TryAdmitEvent([2], maxOutstandingMessages: 1, AlwaysAffordable);

        Assert.False(result);
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1 }, payload);
    }

    /// <summary>Verifies that an event declined by the byte-budget callback makes no change to the queue.</summary>
    [Fact]
    public void TryAdmitEvent_ByteBudgetDeclines_MakesNoChange()
    {
        var queue = new DataLaneOutboundQueue();

        bool result = queue.TryAdmitEvent([1], maxOutstandingMessages: 10, NeverAffordable);

        Assert.False(result);
        Assert.Equal(0, queue.OutstandingMessages);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that a new area's snapshot is admitted and reserves one outstanding slot.</summary>
    [Fact]
    public void TryAdmitSnapshot_NewArea_AdmitsAndReservesOneSlot()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");

        bool result = queue.TryAdmitSnapshot(areaId, [1, 2], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(result);
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1, 2 }, payload);
    }

    /// <summary>Verifies that a new area's snapshot is declined once the outstanding-message bound is reached.</summary>
    [Fact]
    public void TryAdmitSnapshot_NewAreaAtBound_DeclinesWithoutChange()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);

        bool result = queue.TryAdmitSnapshot(new StateAreaId("example_area"), [2], maxOutstandingMessages: 1, AlwaysAffordable);

        Assert.False(result);
        Assert.Equal(1, queue.OutstandingMessages);
    }

    /// <summary>Verifies that a new area's snapshot declined by the byte-budget callback makes no change, including no outstanding-slot reservation.</summary>
    [Fact]
    public void TryAdmitSnapshot_NewAreaByteBudgetDeclines_MakesNoChange()
    {
        var queue = new DataLaneOutboundQueue();

        bool result = queue.TryAdmitSnapshot(new StateAreaId("example_area"), [1], maxOutstandingMessages: 10, NeverAffordable);

        Assert.False(result);
        Assert.Equal(0, queue.OutstandingMessages);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that replacing an existing pending snapshot succeeds without consuming an additional outstanding slot.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplaceExisting_SucceedsWithoutConsumingAnotherSlot()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);

        bool result = queue.TryAdmitSnapshot(areaId, [2, 2], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(result);
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 2, 2 }, payload);
    }

    /// <summary>Verifies that replacing an existing pending snapshot passes the replacement's byte delta -- not the full payload length -- to the byte-budget callback.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplaceExisting_PassesReplacementDeltaToCallback()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1, 2, 3], maxOutstandingMessages: 10, AlwaysAffordable);
        long? observedDelta = null;

        queue.TryAdmitSnapshot(areaId, [9], maxOutstandingMessages: 10, delta =>
        {
            observedDelta = delta;
            return true;
        });

        Assert.Equal(-2, observedDelta);
    }

    /// <summary>Verifies that a replacement declined by the byte-budget callback keeps the old value pending, unchanged.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplaceDeclinedByByteBudget_KeepsOldValue()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);

        bool result = queue.TryAdmitSnapshot(areaId, [2, 2, 2], maxOutstandingMessages: 10, NeverAffordable);

        Assert.False(result);
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1 }, payload);
    }

    /// <summary>Verifies that a snapshot replaced multiple times keeps its original queue position rather than moving to the back.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplacedAfterLaterEventAdmitted_KeepsOriginalPosition()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitEvent([9], maxOutstandingMessages: 10, AlwaysAffordable);

        queue.TryAdmitSnapshot(areaId, [2], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(queue.TryDequeue(out byte[]? first));
        Assert.Equal(new byte[] { 2 }, first);
        Assert.True(queue.TryDequeue(out byte[]? second));
        Assert.Equal(new byte[] { 9 }, second);
    }

    /// <summary>Verifies that dequeuing a snapshot removes it from the by-area lookup, so a later admission for the same area is treated as a fresh slot rather than a replacement.</summary>
    [Fact]
    public void TryDequeue_DequeuedSnapshot_AllowsFreshAdmissionForSameArea()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryDequeue(out _);
        queue.ReleaseOutstanding(); // simulates the dequeued frame's send fully completing

        long? observedDelta = null;
        bool result = queue.TryAdmitSnapshot(areaId, [2, 2], maxOutstandingMessages: 10, delta =>
        {
            observedDelta = delta;
            return true;
        });

        Assert.True(result);
        Assert.Equal(2, observedDelta); // full payload length, not a replacement delta -- treated as a new slot
        Assert.Equal(1, queue.OutstandingMessages);
    }

    /// <summary>Verifies that mixed snapshot and event admissions dequeue in admission order.</summary>
    [Fact]
    public void TryDequeue_MixedAdmissions_ReturnsInAdmissionOrder()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitSnapshot(new StateAreaId("area_a"), [2], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitEvent([3], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(queue.TryDequeue(out byte[]? first));
        Assert.Equal(new byte[] { 1 }, first);
        Assert.True(queue.TryDequeue(out byte[]? second));
        Assert.Equal(new byte[] { 2 }, second);
        Assert.True(queue.TryDequeue(out byte[]? third));
        Assert.Equal(new byte[] { 3 }, third);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that releasing an outstanding slot decrements the count.</summary>
    [Fact]
    public void ReleaseOutstanding_AfterAdmission_DecrementsCount()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable);

        queue.ReleaseOutstanding();

        Assert.Equal(0, queue.OutstandingMessages);
    }

    /// <summary>Verifies that multiple distinct areas' pending snapshots are each tracked and dequeued independently.</summary>
    [Fact]
    public void TryAdmitSnapshot_MultipleDistinctAreas_EachTrackedIndependently()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitSnapshot(new StateAreaId("area_a"), [1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitSnapshot(new StateAreaId("area_b"), [2], maxOutstandingMessages: 10, AlwaysAffordable);

        bool replacedA = queue.TryAdmitSnapshot(new StateAreaId("area_a"), [9], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(replacedA);
        Assert.Equal(2, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? first));
        Assert.Equal(new byte[] { 9 }, first); // area_a's replaced value, at its original front position
        Assert.True(queue.TryDequeue(out byte[]? second));
        Assert.Equal(new byte[] { 2 }, second); // area_b untouched by area_a's replacement
    }

    /// <summary>Verifies that replacing the same area's snapshot twice before it drains sends only the final value.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplacedTwiceBeforeDequeue_OnlyFinalValueDequeues()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);

        queue.TryAdmitSnapshot(areaId, [2], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitSnapshot(areaId, [3], maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 3 }, payload);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that a wait started after the queue is already completed still resolves rather than hanging.</summary>
    [Fact]
    public async Task WaitForReadyAsync_AfterQueueAlreadyCompleted_ResolvesImmediately()
    {
        var queue = new DataLaneOutboundQueue();
        queue.Complete();

        Task waitTask = queue.WaitForReadyAsync(CancellationToken.None);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a fresh, non-completed empty queue does not report itself as completed-and-empty.</summary>
    [Fact]
    public void IsCompletedAndEmpty_BeforeComplete_IsFalse()
    {
        var queue = new DataLaneOutboundQueue();

        Assert.False(queue.IsCompletedAndEmpty);
    }

    /// <summary>Verifies that a completed queue with an entry still pending does not report itself as completed-and-empty.</summary>
    [Fact]
    public void IsCompletedAndEmpty_CompletedButNotEmpty_IsFalse()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable);

        queue.Complete();

        Assert.False(queue.IsCompletedAndEmpty);
    }

    /// <summary>Verifies that a completed, fully drained queue reports itself as completed-and-empty.</summary>
    [Fact]
    public void IsCompletedAndEmpty_CompletedAndDrained_IsTrue()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryDequeue(out _);

        queue.Complete();

        Assert.True(queue.IsCompletedAndEmpty);
    }

    /// <summary>Verifies that neither an event nor a snapshot can be admitted once the queue is completed.</summary>
    [Fact]
    public void Complete_ThenTryAdmit_BothDecline()
    {
        var queue = new DataLaneOutboundQueue();
        queue.Complete();

        Assert.False(queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable));
        Assert.False(queue.TryAdmitSnapshot(new StateAreaId("example_area"), [1], maxOutstandingMessages: 10, AlwaysAffordable));
    }

    /// <summary>Verifies that calling <see cref="DataLaneOutboundQueue.Complete"/> more than once does not throw.</summary>
    [Fact]
    public void Complete_CalledTwice_DoesNotThrow()
    {
        var queue = new DataLaneOutboundQueue();

        queue.Complete();
        queue.Complete();
    }

    /// <summary>Verifies that a wait in progress before any admission completes promptly once one is admitted.</summary>
    [Fact]
    public async Task WaitForReadyAsync_ThenAdmission_CompletesPromptly()
    {
        var queue = new DataLaneOutboundQueue();

        Task waitTask = queue.WaitForReadyAsync(CancellationToken.None);
        Assert.False(waitTask.IsCompleted);
        queue.TryAdmitEvent([1], maxOutstandingMessages: 10, AlwaysAffordable);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that a wait in progress completes once the queue is completed, even with nothing ever admitted.</summary>
    [Fact]
    public async Task WaitForReadyAsync_ThenComplete_CompletesPromptly()
    {
        var queue = new DataLaneOutboundQueue();

        Task waitTask = queue.WaitForReadyAsync(CancellationToken.None);
        Assert.False(waitTask.IsCompleted);
        queue.Complete();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies that cancelling the token passed to a pending wait ends it with cancellation.</summary>
    [Fact]
    public async Task WaitForReadyAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var queue = new DataLaneOutboundQueue();
        using var cancellation = new CancellationTokenSource();

        Task waitTask = queue.WaitForReadyAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
