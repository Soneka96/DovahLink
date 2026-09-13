using System.Text.Json;
using DovahLink.Host.Identity;

namespace DovahLink.Host.State;

/// <summary>
/// One ordered update to a state area, already encoded as JSON and ready to become a
/// <c>state_event</c> wire message. Corresponds to <c>protocol/schema/README.md</c>'s "State
/// envelope" Event shape: <see cref="Data"/> is the complete post-change state, not a partial patch,
/// and <see cref="Revision"/> must equal <see cref="BaseRevision"/> plus one.
/// </summary>
/// <param name="StateArea">The state area this event belongs to.</param>
/// <param name="BaseRevision">The revision a recipient must already hold for this event to apply.</param>
/// <param name="Revision">The revision this event advances the state area to.</param>
/// <param name="OccurredAt">When this change was captured, for display and diagnostics only -- not an ordering source.</param>
/// <param name="Data">The complete post-change state for this area, not a partial patch.</param>
/// <param name="PlayContextId">
/// The play context this event was captured under, established at the state area's own
/// authoritative ordering point -- never re-derived later from a tracker read at delivery time, so
/// a consumer can label outbound wire messages from this value alone. <see langword="null"/> when no
/// play context was established yet at capture time.
/// </param>
/// <param name="PlayContextGeneration">
/// The play-context transition generation this event was captured under, comparable against
/// <see cref="PlayContext.IPlayContextTracker.GetSnapshot"/>'s own generation to detect an event that
/// has already gone stale relative to a later transition.
/// </param>
public sealed record StateEventPublication(
    StateAreaId StateArea,
    RevisionNumber BaseRevision,
    RevisionNumber Revision,
    DateTimeOffset OccurredAt,
    JsonElement Data,
    PlayContextId? PlayContextId,
    long PlayContextGeneration);
