using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DovahLink.Host.Identity;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <summary>
/// The domain-agnostic, already-JSON-encoded, read-only push source a per-session delivery queue
/// consumes to answer <c>subscribe</c> and <c>snapshot_request</c> and to forward ongoing
/// <c>state_event</c> updates. A view of <see cref="IAuthoritativeStateStore"/>, the single owner of
/// current state and revisions -- never a second copy of it -- and deliberately decoupled from that
/// owner's typed write surface, so this contract and its consumers never depend on a concrete Skyrim
/// domain shape.
///
/// The owner guarantees the following ordering and freshness, so no caller needs to re-derive them:
/// <list type="bullet">
/// <item>For one <see cref="StateAreaId"/>, <see cref="EventOccurred"/> is never invoked
/// concurrently for two different events, and is invoked in strictly increasing
/// <see cref="StateEventPublication.Revision"/> order.</item>
/// <item>For one <see cref="StateAreaId"/>, <see cref="SnapshotChanged"/> is never invoked
/// concurrently for two different values, and is invoked in strictly increasing
/// <see cref="StateSnapshotPublication.Revision"/> order.</item>
/// <item><see cref="TryGetSnapshot"/> never returns a value for an area whose revision is older
/// than the most recent <see cref="StateEventPublication.Revision"/> already raised through
/// <see cref="EventOccurred"/> for that same area, because both derive from the same committed record.</item>
/// <item>
/// The owner checks the adapter's current availability and authority, commits the resulting value,
/// and raises <see cref="EventOccurred"/>/<see cref="SnapshotChanged"/> as one atomic step under a
/// single lock, so a concurrent <see cref="IStateAuthorityLifecycle.Rotated"/> rotation can never let
/// an event produced under an already-rotated-away <see cref="StateAuthorityId"/> reach a subscriber
/// still treating an older baseline as live.
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
    /// Raised when adapter resynchronization completes and a cached snapshot may now be readable.
    /// This is only an availability hint; consumers must still call <see cref="TryGetSnapshot"/> to
    /// validate current authority, connection, play context, and resynchronization state.
    /// </summary>
    event Action? SnapshotAvailabilityChanged;

    /// <summary>
    /// Tries to read a state area's current value as a fresh baseline snapshot. See this
    /// interface's own summary for the freshness guarantee this read makes relative to
    /// <see cref="EventOccurred"/>.
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="snapshot">The area's current value as a snapshot, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot);

    /// <summary>
    /// Creates the generic revision-zero unavailable baseline for an accepted area at a committed
    /// play-context boundary, without storing it or advancing normal revision state.
    /// </summary>
    /// <param name="areaId">The registered state area the baseline belongs to.</param>
    /// <param name="playContext">The newly committed play-context identity and generation.</param>
    /// <param name="occurredAt">The time the Host established the boundary baseline.</param>
    /// <returns>A generic unavailable Snapshot owned by the supplied identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="areaId"/> is not registered.</exception>
    /// <exception cref="InvalidOperationException">The state-authority lifecycle is faulted.</exception>
    StateSnapshotPublication CreateUnavailableBoundaryBaseline(
        StateAreaId areaId,
        PlayContextSnapshot playContext,
        DateTimeOffset occurredAt);
}
