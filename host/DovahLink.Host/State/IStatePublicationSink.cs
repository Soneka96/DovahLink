using System.Text.Json;
using DovahLink.Host.Identity;

namespace DovahLink.Host.State;

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
