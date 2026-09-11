using System.Text.Json;
using DovahLink.Host.Identity;

namespace DovahLink.Host.State;

/// <summary>
/// One state area's complete authoritative value at a revision, already encoded as JSON and ready
/// to become a <c>state_snapshot</c> wire message. Corresponds to <c>protocol/schema/README.md</c>'s
/// "State envelope" Snapshot shape: an accepted snapshot becomes the baseline for its state area and
/// supersedes older events for that area.
/// </summary>
/// <param name="StateArea">The state area this value belongs to.</param>
/// <param name="Revision">The revision this value is current as of.</param>
/// <param name="OccurredAt">When this value was captured, for display and diagnostics only -- not an ordering source.</param>
/// <param name="Data">
/// The complete, already-encoded state for this area. An unavailable value is represented
/// explicitly within this JSON -- as <see langword="null"/> or a documented availability field --
/// never by omitting the publication.
/// </param>
/// <param name="PlayContextId">
/// The play context this value was captured under, established at the state area's own
/// authoritative ordering point -- never re-derived later from a tracker read at delivery time, so
/// a consumer can label outbound wire messages from this value alone. <see langword="null"/> when no
/// play context was established yet at capture time.
/// </param>
/// <param name="PlayContextGeneration">
/// The play-context transition generation this value was captured under, comparable against
/// <see cref="PlayContext.IPlayContextTracker.GetSnapshot"/>'s own generation to detect a value that
/// has already gone stale relative to a later transition.
/// </param>
public sealed record StateSnapshotPublication(
    StateAreaId StateArea,
    RevisionNumber Revision,
    DateTimeOffset OccurredAt,
    JsonElement Data,
    PlayContextId? PlayContextId,
    long PlayContextGeneration);
