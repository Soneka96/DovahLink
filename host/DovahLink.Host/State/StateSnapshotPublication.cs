using System.Text.Json;

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
public sealed record StateSnapshotPublication(
    StateAreaId StateArea,
    RevisionNumber Revision,
    DateTimeOffset OccurredAt,
    JsonElement Data);
