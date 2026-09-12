using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DovahLink.Host.State;

namespace DovahLink.Host.Client.Transport;

/// <summary>
/// The <see cref="PublicOutboundLane.Data"/> lane's own ordered admission and draining structure:
/// one replaceable keyed slot per state area for Snapshot values, plus an ordered FIFO for Event
/// values, sharing one combined outstanding-message bound with each other -- not with the
/// Control-or-Recovery lane, which owns its own separate reservation elsewhere. Per
/// <c>ai/context/protocol/security.md</c>'s bounded outbound queue policy: "one pending keyed
/// Snapshot slot per registered Snapshot state area... plus the remaining ordered Event FIFO
/// capacity." This type owns only ordering, replacement, and outstanding-message-count mechanics;
/// byte-budget admission (shared with the Control-or-Recovery lane) and the
/// force-close-on-overflow policy for an unadmittable Event belong to the caller -- see
/// <see cref="PublicWebSocketConnection.TrySend"/> and
/// <see cref="PublicWebSocketConnection.TrySendSnapshot"/>. Thread-safe: every member may be called
/// concurrently.
/// </summary>
public interface IDataLaneOutboundQueue
{
    /// <summary>The number of outstanding messages this queue currently owns, from admission until <see cref="ReleaseOutstanding"/>.</summary>
    int OutstandingMessages { get; }

    /// <summary>
    /// Atomically admits a snapshot value for <paramref name="areaId"/>: replaces an already-pending
    /// snapshot for the same area in place, at its existing queue position, without consuming an
    /// additional outstanding-message slot; or, when no pending snapshot for the area exists,
    /// reserves a new outstanding-message slot and appends a new position at the back of the queue.
    /// </summary>
    /// <param name="areaId">The state area this snapshot value belongs to.</param>
    /// <param name="payload">The complete, already-encoded message payload.</param>
    /// <param name="maxOutstandingMessages">The outstanding-message bound a new slot must stay within; ignored when replacing an existing slot.</param>
    /// <param name="canAffordByteDelta">
    /// Consulted, atomically with the rest of this decision, with the byte difference this admission
    /// would make -- the full payload length for a new slot, or the replacement delta (possibly
    /// negative) for an existing one. When it declines, this call makes no change at all.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is now the pending snapshot for <paramref name="areaId"/>;
    /// <see langword="false"/> when it was declined -- a deferral, not a failure the caller must
    /// react to.
    /// </returns>
    bool TryAdmitSnapshot(StateAreaId areaId, byte[] payload, int maxOutstandingMessages, Func<long, bool> canAffordByteDelta);

    /// <summary>Tries to admit an event value, reserving a new outstanding-message slot and appending it at the back of the queue.</summary>
    /// <param name="payload">The complete, already-encoded message payload.</param>
    /// <param name="maxOutstandingMessages">The outstanding-message bound this admission must stay within.</param>
    /// <param name="canAffordBytes">Consulted with <paramref name="payload"/>'s byte length; when it declines, this call makes no change at all.</param>
    /// <returns><see langword="true"/> when the event is now queued; <see langword="false"/> when it was declined.</returns>
    bool TryAdmitEvent(byte[] payload, int maxOutstandingMessages, Func<long, bool> canAffordBytes);

    /// <summary>Tries to dequeue the entry at the front of the queue, in admission order.</summary>
    /// <param name="payload">The dequeued entry's bytes, if any.</param>
    /// <returns><see langword="true"/> when an entry was dequeued.</returns>
    bool TryDequeue([MaybeNullWhen(false)] out byte[] payload);

    /// <summary>
    /// Releases one outstanding-message slot, once a dequeued frame's send has fully completed --
    /// not merely once <see cref="TryDequeue"/> removed it from the queue.
    /// </summary>
    void ReleaseOutstanding();

    /// <summary>
    /// Waits until <see cref="TryDequeue"/> may have something new to return, or until
    /// <see cref="Complete"/> has been called, whichever happens first.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop waiting.</param>
    Task WaitForReadyAsync(CancellationToken cancellationToken);

    /// <summary>Marks this queue as never admitting another value; a later admit call fails.</summary>
    void Complete();

    /// <summary>Whether <see cref="Complete"/> has been called and every admitted entry has since been dequeued.</summary>
    bool IsCompletedAndEmpty { get; }
}

/// <inheritdoc cref="IDataLaneOutboundQueue"/>
public sealed class DataLaneOutboundQueue : IDataLaneOutboundQueue
{
    /// <summary>Guards every field below against concurrent access.</summary>
    private readonly object gate = new();

    /// <summary>Every currently queued entry, in admission order.</summary>
    private readonly LinkedList<Entry> entries = new();

    /// <summary>Maps a state area with a currently pending snapshot to its node in <see cref="entries"/>.</summary>
    private readonly Dictionary<StateAreaId, LinkedListNode<Entry>> snapshotNodesByArea = new();

    /// <summary>
    /// A single-slot wake-up signal for <see cref="WaitForReadyAsync"/>: written (best-effort) on
    /// every successful admission and completed by <see cref="Complete"/>. Carries no payload of its
    /// own significance -- a waiter always re-checks <see cref="TryDequeue"/> after waking, so a
    /// dropped or coalesced write here can never lose real data, only delay a wake-up that a later
    /// write (or the next poll) still covers.
    /// </summary>
    private readonly Channel<bool> readySignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });

    /// <summary>The number of outstanding messages this queue currently owns.</summary>
    private int outstandingMessages;

    /// <summary>Whether <see cref="Complete"/> has been called.</summary>
    private bool completed;

    /// <inheritdoc/>
    public int OutstandingMessages
    {
        get
        {
            lock (gate)
            {
                return outstandingMessages;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryAdmitSnapshot(StateAreaId areaId, byte[] payload, int maxOutstandingMessages, Func<long, bool> canAffordByteDelta)
    {
        lock (gate)
        {
            if (completed)
            {
                return false;
            }

            if (snapshotNodesByArea.TryGetValue(areaId, out LinkedListNode<Entry>? node))
            {
                var snapshotEntry = (SnapshotEntry)node.Value;
                long delta = payload.Length - snapshotEntry.Bytes.Length;
                if (!canAffordByteDelta(delta))
                {
                    return false;
                }

                snapshotEntry.SetBytes(payload);
                readySignal.Writer.TryWrite(true);
                return true;
            }

            if (outstandingMessages >= maxOutstandingMessages || !canAffordByteDelta(payload.Length))
            {
                return false;
            }

            var newEntry = new SnapshotEntry(areaId, payload);
            LinkedListNode<Entry> newNode = entries.AddLast(newEntry);
            snapshotNodesByArea.Add(areaId, newNode);
            outstandingMessages++;
            readySignal.Writer.TryWrite(true);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryAdmitEvent(byte[] payload, int maxOutstandingMessages, Func<long, bool> canAffordBytes)
    {
        lock (gate)
        {
            if (completed || outstandingMessages >= maxOutstandingMessages || !canAffordBytes(payload.Length))
            {
                return false;
            }

            entries.AddLast(new EventEntry(payload));
            outstandingMessages++;
            readySignal.Writer.TryWrite(true);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryDequeue([MaybeNullWhen(false)] out byte[] payload)
    {
        lock (gate)
        {
            LinkedListNode<Entry>? first = entries.First;
            if (first is null)
            {
                payload = null;
                return false;
            }

            entries.RemoveFirst();
            if (first.Value is SnapshotEntry snapshotEntry)
            {
                snapshotNodesByArea.Remove(snapshotEntry.AreaId);
            }

            payload = first.Value.Bytes;
            return true;
        }
    }

    /// <inheritdoc/>
    public void ReleaseOutstanding()
    {
        lock (gate)
        {
            outstandingMessages--;
        }
    }

    /// <inheritdoc/>
    public async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        await readySignal.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        readySignal.Reader.TryRead(out _);
    }

    /// <inheritdoc/>
    public void Complete()
    {
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
        }

        readySignal.Writer.TryComplete();
    }

    /// <inheritdoc/>
    public bool IsCompletedAndEmpty
    {
        get
        {
            lock (gate)
            {
                return completed && entries.Count == 0;
            }
        }
    }

    /// <summary>One entry held by this queue: either a keyed, replaceable snapshot slot or a plain event.</summary>
    private abstract class Entry
    {
        /// <summary>The entry's current payload bytes.</summary>
        public abstract byte[] Bytes { get; }
    }

    /// <summary>A plain, immutable event entry.</summary>
    /// <param name="bytes">The event's payload bytes.</param>
    private sealed class EventEntry(byte[] bytes) : Entry
    {
        /// <inheritdoc/>
        public override byte[] Bytes { get; } = bytes;
    }

    /// <summary>A keyed, replaceable snapshot entry.</summary>
    /// <param name="areaId">The state area this snapshot belongs to.</param>
    /// <param name="bytes">The snapshot's initial payload bytes.</param>
    private sealed class SnapshotEntry(StateAreaId areaId, byte[] bytes) : Entry
    {
        /// <summary>The current payload bytes, mutated in place by <see cref="SetBytes"/>.</summary>
        private byte[] bytes = bytes;

        /// <summary>The state area this snapshot belongs to.</summary>
        public StateAreaId AreaId { get; } = areaId;

        /// <inheritdoc/>
        public override byte[] Bytes => bytes;

        /// <summary>Replaces this entry's payload bytes in place, without changing its position.</summary>
        /// <param name="newBytes">The new payload bytes.</param>
        public void SetBytes(byte[] newBytes) => bytes = newBytes;
    }
}
