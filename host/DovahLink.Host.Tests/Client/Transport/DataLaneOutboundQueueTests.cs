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
        queue.ReleaseOutstanding(maxOutstandingMessages: 10, AlwaysAffordable); // simulates the dequeued frame's send fully completing

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

        queue.ReleaseOutstanding(maxOutstandingMessages: 10, AlwaysAffordable);

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

    /// <summary>Verifies that a snapshot declined by the outstanding-message bound is retained as that area's dirty snapshot rather than disappearing.</summary>
    [Fact]
    public void TryAdmitSnapshot_NewAreaAtMessageCountBound_RetainsPayloadAsDirty()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryAdmitSnapshot(new StateAreaId("example_area"), [9], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryDequeue(out _); // drains the event, freeing the one outstanding-message slot

        queue.ReleaseOutstanding(maxOutstandingMessages: 1, AlwaysAffordable);

        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 9 }, payload); // the previously declined snapshot, promoted rather than lost
    }

    /// <summary>Verifies that a replacement declined by the byte budget retains the newer payload as dirty, not the older still-queued one.</summary>
    [Fact]
    public void TryAdmitSnapshot_ReplaceDeclinedByByteBudget_RetainsNewerPayloadAsDirty()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable);
        queue.TryAdmitSnapshot(areaId, [2, 2, 2], maxOutstandingMessages: 10, NeverAffordable); // declined; [2, 2, 2] becomes dirty
        queue.TryDequeue(out _); // drains the still-queued [1], freeing its outstanding-message slot

        queue.ReleaseOutstanding(maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 2, 2, 2 }, payload);
    }

    /// <summary>
    /// Verifies that releasing an outstanding slot promotes a dirty snapshot that now fits before the
    /// call returns -- the atomicity <see cref="DataLaneOutboundQueue.ReleaseOutstanding"/> exists to
    /// guarantee, proven here by observing the promoted entry immediately, with no separate promotion
    /// call in between.
    /// </summary>
    [Fact]
    public void ReleaseOutstanding_DirtySnapshotFits_PromotesBeforeReturning()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryAdmitSnapshot(new StateAreaId("example_area"), [9], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryDequeue(out _);

        queue.ReleaseOutstanding(maxOutstandingMessages: 1, AlwaysAffordable);

        Assert.Equal(1, queue.OutstandingMessages); // the promoted snapshot now owns the freed slot
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 9 }, payload);
    }

    /// <summary>Verifies that a dirty snapshot the byte budget still declines after a slot frees stays dirty rather than being dropped.</summary>
    [Fact]
    public void ReleaseOutstanding_DirtySnapshotStillDoesNotFitByteBudget_StaysDirty()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryAdmitSnapshot(new StateAreaId("example_area"), [9], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryDequeue(out _);

        queue.ReleaseOutstanding(maxOutstandingMessages: 1, NeverAffordable);

        Assert.Equal(0, queue.OutstandingMessages);
        Assert.False(queue.TryDequeue(out _));

        // A later attempt with room in the byte budget still promotes the retained value.
        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 1, AlwaysAffordable);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 9 }, payload);
    }

    /// <summary>
    /// Verifies that promoting deferred snapshots without releasing a slot -- the Control/Recovery
    /// lane's own path, once its send frees shared byte budget the Data lane did not reserve --
    /// promotes a dirty snapshot the byte budget now affords, without needing a slot release.
    /// </summary>
    [Fact]
    public void TryPromoteDeferredSnapshots_ByteBudgetNowAffordable_PromotesWithoutSlotRelease()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitSnapshot(new StateAreaId("example_area"), [9], maxOutstandingMessages: 10, NeverAffordable);
        Assert.Equal(0, queue.OutstandingMessages);

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 9 }, payload);
    }

    /// <summary>Verifies that a direct successful admission for an area clears any dirty value already retained for it.</summary>
    [Fact]
    public void TryAdmitSnapshot_SucceedsDirectly_ClearsAnyPriorDirtyValueForArea()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable);
        queue.TryAdmitSnapshot(areaId, [8], maxOutstandingMessages: 1, AlwaysAffordable); // declined; [8] becomes dirty
        queue.TryDequeue(out _);
        queue.ReleaseOutstanding(maxOutstandingMessages: 1, NeverAffordable); // frees the slot but declines promotion, so [8] stays dirty

        bool result = queue.TryAdmitSnapshot(areaId, [9], maxOutstandingMessages: 1, AlwaysAffordable);

        Assert.True(result);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 9 }, payload); // the fresh value, not the stale dirty [8]

        // The stale dirty [8] must not resurface on a later promotion attempt.
        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 1, AlwaysAffordable);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>Verifies that completing the queue clears any retained dirty snapshots, so they never resurface after teardown.</summary>
    [Fact]
    public void Complete_ClearsAnyDirtySnapshots()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitSnapshot(new StateAreaId("example_area"), [9], maxOutstandingMessages: 10, NeverAffordable);

        queue.Complete();

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, AlwaysAffordable);
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>
    /// Verifies that a repeated decline for the same area while it is already dirty overwrites the
    /// retained payload with the newer one, rather than promoting the stale earlier value.
    /// </summary>
    [Fact]
    public void TryAdmitSnapshot_DeclinedAgainWhileAlreadyDirty_RetainsOnlyTheNewerPayload()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 0, AlwaysAffordable); // declined; [1] becomes dirty

        queue.TryAdmitSnapshot(areaId, [2, 2], maxOutstandingMessages: 0, AlwaysAffordable); // still declined; overwrites the dirty value

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, AlwaysAffordable);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 2, 2 }, payload);
    }

    /// <summary>
    /// Verifies that promotion halts at the outstanding-message bound rather than dropping a later
    /// dirty area: when only one freed slot is available, exactly one of two simultaneously dirty
    /// areas promotes and the other remains dirty for a later attempt.
    /// </summary>
    [Fact]
    public void PromoteDirtySnapshots_MultipleAreasButOnlyOneSlotFits_PromotesOneAndKeepsTheOtherDirty()
    {
        var queue = new DataLaneOutboundQueue();
        queue.TryAdmitEvent([1], maxOutstandingMessages: 1, AlwaysAffordable); // occupies the only slot
        var areaA = new StateAreaId("area_a");
        var areaB = new StateAreaId("area_b");
        queue.TryAdmitSnapshot(areaA, [1], maxOutstandingMessages: 1, AlwaysAffordable); // declined; dirty
        queue.TryAdmitSnapshot(areaB, [2], maxOutstandingMessages: 1, AlwaysAffordable); // declined; dirty
        queue.TryDequeue(out _); // drains the event

        queue.ReleaseOutstanding(maxOutstandingMessages: 1, AlwaysAffordable); // exactly one freed slot

        Assert.Equal(1, queue.OutstandingMessages); // only one of the two dirty areas could be promoted
        Assert.True(queue.TryDequeue(out byte[]? firstPromoted));
        Assert.False(queue.TryDequeue(out _)); // the second area is still dirty, not queued

        // The leftover dirty area promotes once capacity allows a second slot.
        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 2, AlwaysAffordable);
        Assert.True(queue.TryDequeue(out byte[]? secondPromoted));
        Assert.Equal(
            new HashSet<byte> { 1, 2 },
            new HashSet<byte> { firstPromoted![0], secondPromoted![0] }); // both areas eventually promoted, in either order
    }

    /// <summary>
    /// Verifies that a byte-budget decline for one dirty area does not halt promotion of another --
    /// unlike the outstanding-message bound, an unaffordable area is skipped, not a stopping point.
    /// </summary>
    [Fact]
    public void PromoteDirtySnapshots_OneAreaTooLargeForByteBudget_PromotesTheOtherRegardless()
    {
        var queue = new DataLaneOutboundQueue();
        var smallArea = new StateAreaId("small_area");
        var bigArea = new StateAreaId("big_area");
        queue.TryAdmitSnapshot(smallArea, [1], maxOutstandingMessages: 10, NeverAffordable); // declined; dirty
        queue.TryAdmitSnapshot(bigArea, [1, 2, 3], maxOutstandingMessages: 10, NeverAffordable); // declined; dirty

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, canAffordBytes: length => length <= 1);

        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1 }, payload); // only the affordable area promoted

        // The still-too-large area remains dirty and promotes once the budget allows it.
        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, AlwaysAffordable);
        Assert.True(queue.TryDequeue(out byte[]? secondPayload));
        Assert.Equal(new byte[] { 1, 2, 3 }, secondPayload);
    }

    /// <summary>
    /// Verifies that promoting a dirty area whose declined replacement left its old value still
    /// queued replaces that value in place, rather than throwing when it tries to add a second node
    /// for an area <see cref="DataLaneOutboundQueue"/> already has one for.
    /// </summary>
    [Fact]
    public void PromoteDirtySnapshots_AreaStillQueued_ReplacesInPlaceWithoutThrowing()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable); // queued
        queue.TryAdmitSnapshot(areaId, [2, 2, 2], maxOutstandingMessages: 10, NeverAffordable); // declined; dirty, old value still queued

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, AlwaysAffordable);

        Assert.Equal(1, queue.OutstandingMessages); // replaced in place; no new slot consumed
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 2, 2, 2 }, payload);
        Assert.False(queue.TryDequeue(out _)); // nothing else queued for this area
    }

    /// <summary>Verifies that promoting an area still queued consults the replacement byte delta, not the promoted value's full length.</summary>
    [Fact]
    public void PromoteDirtySnapshots_AreaStillQueued_ChecksReplacementDeltaNotFullLength()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1, 2, 3], maxOutstandingMessages: 10, AlwaysAffordable); // queued, length 3
        queue.TryAdmitSnapshot(areaId, [9], maxOutstandingMessages: 10, NeverAffordable); // declined; dirty, length 1
        long? observedDelta = null;

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, delta =>
        {
            observedDelta = delta;
            return true;
        });

        Assert.Equal(-2, observedDelta); // 1 - 3, not the dirty value's own length of 1
    }

    /// <summary>Verifies that a declined in-place replacement leaves the queued area's old value untouched and promotes nothing.</summary>
    [Fact]
    public void PromoteDirtySnapshots_AreaStillQueued_DeclinedByDelta_StaysDirtyAndOldValueUnchanged()
    {
        var queue = new DataLaneOutboundQueue();
        var areaId = new StateAreaId("example_area");
        queue.TryAdmitSnapshot(areaId, [1], maxOutstandingMessages: 10, AlwaysAffordable); // queued
        queue.TryAdmitSnapshot(areaId, [2, 2, 2], maxOutstandingMessages: 10, NeverAffordable); // declined; dirty, old value still queued

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 10, NeverAffordable); // declines the in-place replace too

        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 1 }, payload); // untouched by the declined replacement attempt
        Assert.False(queue.TryDequeue(out _)); // the dirty value was not queued, only retained
    }

    /// <summary>
    /// Verifies that a still-queued area's in-place replacement is not blocked by the
    /// outstanding-message bound being exhausted by an unrelated new-slot candidate in the same
    /// promotion call, since the in-place path needs no new slot at all.
    /// </summary>
    [Fact]
    public void PromoteDirtySnapshots_MixedInPlaceAndNewSlotCandidates_InPlaceReplacementNotBlockedByMessageBound()
    {
        var queue = new DataLaneOutboundQueue();
        var queuedArea = new StateAreaId("queued_area");
        queue.TryAdmitSnapshot(queuedArea, [1], maxOutstandingMessages: 1, AlwaysAffordable); // consumes the only slot
        queue.TryAdmitSnapshot(queuedArea, [2, 2], maxOutstandingMessages: 1, NeverAffordable); // declined; dirty, still queued
        var newArea = new StateAreaId("new_area");
        queue.TryAdmitSnapshot(newArea, [9], maxOutstandingMessages: 1, AlwaysAffordable); // declined by the message-count bound; dirty, no node

        queue.TryPromoteDeferredSnapshots(maxOutstandingMessages: 1, AlwaysAffordable);

        // The in-place replacement succeeds even though the message bound is already exhausted and
        // the unrelated new-slot candidate could not be promoted in this same call.
        Assert.Equal(1, queue.OutstandingMessages);
        Assert.True(queue.TryDequeue(out byte[]? payload));
        Assert.Equal(new byte[] { 2, 2 }, payload); // queued_area replaced in place
        Assert.False(queue.TryDequeue(out _)); // new_area did not get a slot in this call

        // new_area's dirty value still promotes once a slot frees.
        queue.ReleaseOutstanding(maxOutstandingMessages: 2, AlwaysAffordable);
        Assert.True(queue.TryDequeue(out byte[]? secondPayload));
        Assert.Equal(new byte[] { 9 }, secondPayload);
    }
}
