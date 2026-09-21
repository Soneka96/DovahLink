using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
}

// TODO(stage4-file-extraction): Move IStatePublicationSink to its own
// IStatePublicationSink.cs in the post-Stage-4 structural cleanup PR.
// Temporarily colocated with its paired read-side interface to hold this
// PR's changed-file count down; extraction only, no behavior change.
/// <summary>
/// The producer-facing counterpart to <see cref="IStatePublicationFeed"/>: where an accepted,
/// changed capture becomes a publication. Kept as a separate interface from the consumer-facing
/// feed so a per-session delivery queue only ever sees the read side, per
/// <see cref="IStatePublicationFeed"/>'s own "domain-agnostic... push source" framing -- the same
/// concrete type implements both, registered under both interfaces.
/// </summary>
public interface IStatePublicationSink
{
    /// <summary>
    /// Publishes an accepted, changed Snapshot-mode value, already JSON-encoded. A silent no-op --
    /// no event raised, no stored value updated -- when <paramref name="areaId"/> is not currently
    /// registered, or the adapter is no longer available and resynchronized, or the play context has
    /// already moved on from <paramref name="capturedPlayContextId"/>/<paramref name="capturedPlayContextGeneration"/>:
    /// the caller's own <see cref="IStatePublisher{TState}.Apply"/> check and this call are two
    /// separate lock scopes, and re-validating here -- the same check, under this feed's own lock,
    /// atomic with the raise -- closes the gap between them per <see cref="IStatePublicationFeed"/>'s
    /// own documented freshness guarantee.
    /// </summary>
    /// <param name="areaId">The state area this value belongs to.</param>
    /// <param name="revision">The revision this value is current as of.</param>
    /// <param name="data">The complete, already-encoded state for this area.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this value was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this value was captured.</param>
    /// <param name="occurredAt">When this value was captured, for display and diagnostics only.</param>
    void PublishSnapshot(
        StateAreaId areaId,
        RevisionNumber revision,
        JsonElement data,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt);

    /// <summary>Publishes an accepted, changed Event-mode value, already JSON-encoded. Same drop rule as <see cref="PublishSnapshot"/>.</summary>
    /// <param name="areaId">The state area this event belongs to.</param>
    /// <param name="baseRevision">The revision a recipient must already hold for this event to apply.</param>
    /// <param name="revision">The revision this event advances the state area to.</param>
    /// <param name="data">The complete post-change state for this area, not a partial patch.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this event was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this event was captured.</param>
    /// <param name="occurredAt">When this change was captured, for display and diagnostics only.</param>
    void PublishEvent(
        StateAreaId areaId,
        RevisionNumber baseRevision,
        RevisionNumber revision,
        JsonElement data,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt);

    /// <summary>
    /// Repopulates the pull-read cache for an accepted resynchronization baseline whose value is
    /// unchanged from what was already stored -- distinct from <see cref="PublishSnapshot"/>, which
    /// only ever runs for a value that actually changed. A continuity loss clears the pull-read cache
    /// unconditionally (see <see cref="IStatePublicationFeed"/>'s own defense-in-depth clearing), so
    /// without this call, a same-value resynchronization would leave that cache empty forever even
    /// though the authoritative store the resynchronization itself updates is correct: a fresh client
    /// requesting a snapshot after resynchronization would be told no value is available merely
    /// because nothing about it changed. Does not raise <see cref="IStatePublicationFeed.SnapshotChanged"/>:
    /// the value is, by definition, not a change any subscriber needs telling about. Same drop rule as
    /// <see cref="PublishSnapshot"/>.
    /// </summary>
    /// <param name="areaId">The state area this baseline belongs to.</param>
    /// <param name="revision">The revision this value is current as of.</param>
    /// <param name="data">The complete, already-encoded state for this area.</param>
    /// <param name="capturedPlayContextId">The play context that was current at the moment this baseline was captured.</param>
    /// <param name="capturedPlayContextGeneration">The play-context transition generation that was current at the moment this baseline was captured.</param>
    /// <param name="occurredAt">When this baseline was captured, for display and diagnostics only.</param>
    void EstablishBaseline(
        StateAreaId areaId,
        RevisionNumber revision,
        JsonElement data,
        PlayContextId capturedPlayContextId,
        long capturedPlayContextGeneration,
        DateTimeOffset occurredAt);
}
