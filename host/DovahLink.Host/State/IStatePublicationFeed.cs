using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.Identity;

namespace DovahLink.Host.State;

/// <summary>
/// The domain-agnostic, already-JSON-encoded push source a per-session delivery queue consumes to
/// answer <c>subscribe</c> and <c>snapshot_request</c> and to forward ongoing <c>state_event</c>
/// updates. Deliberately decoupled from <see cref="IStatePublisher{TState}"/>'s strongly-typed
/// domain storage, so this contract and its consumers never depend on a concrete Skyrim domain shape.
///
/// The implementation backing this feed owns the following ordering and freshness guarantees --
/// matching the single authoritative per-state-area ordering point that assigns revisions before
/// either member of this interface can observe them -- so that no caller needs to re-derive them
/// independently:
/// <list type="bullet">
/// <item>For one <see cref="StateAreaId"/>, <see cref="EventOccurred"/> is never invoked
/// concurrently for two different events, and is invoked in strictly increasing
/// <see cref="StateEventPublication.Revision"/> order.</item>
/// <item>For one <see cref="StateAreaId"/>, <see cref="SnapshotChanged"/> is never invoked
/// concurrently for two different values, and is invoked in strictly increasing
/// <see cref="StateSnapshotPublication.Revision"/> order.</item>
/// <item><see cref="TryGetSnapshot"/> never returns a value for an area whose revision is older
/// than the most recent <see cref="StateEventPublication.Revision"/> already raised through
/// <see cref="EventOccurred"/> for that same area.</item>
/// <item>
/// The implementation must check the adapter's current availability/authority and raise
/// <see cref="EventOccurred"/>/<see cref="SnapshotChanged"/> for the resulting value as one
/// atomic step under a single lock -- matching <see cref="IStatePublisher{TState}.Apply"/>'s own
/// check-then-store discipline -- never as two separable steps, so a concurrent
/// <see cref="IStateAuthorityLifecycle.Rotated"/> rotation can never let an event produced under an
/// already-rotated-away <see cref="StateAuthorityId"/> reach a subscriber still treating an older
/// baseline as live.
/// </item>
/// </list>
/// </summary>
public interface IStatePublicationFeed
{
    /// <summary>
    /// Raised whenever a registered state area's authoritative value changes, carrying the
    /// resulting event. Never raised for an area that is not currently registered. See this
    /// interface's own summary for the per-area ordering and concurrency guarantee this raises
    /// under.
    /// </summary>
    event Action<StateEventPublication>? EventOccurred;

    /// <summary>
    /// Raised whenever a registered Snapshot-mode state area's authoritative value changes,
    /// carrying the resulting complete current-state value -- the unsolicited counterpart to
    /// <see cref="TryGetSnapshot"/>'s pull-based read. Never raised for an area that is not
    /// currently registered. See this interface's own summary for the per-area ordering and
    /// concurrency guarantee this raises under.
    /// </summary>
    event Action<StateSnapshotPublication>? SnapshotChanged;

    /// <summary>
    /// Tries to read a state area's current value as a fresh baseline snapshot. See this
    /// interface's own summary for the freshness guarantee this read makes relative to
    /// <see cref="EventOccurred"/>.
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="snapshot">The area's current value as a snapshot, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot);
}
