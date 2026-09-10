using System.Text.Json;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>state_event</c> message payload, per <c>protocol/schema/README.md</c>'s "State envelope"
/// Event shape and its "<c>state_event</c>" section. Host-originated only: contains one ordered
/// update from <see cref="BaseRevision"/> to <see cref="Revision"/> for one subscribed state area.
/// <see cref="Data"/> is the complete post-change state, not a partial patch.
/// </summary>
public sealed record StateEventPayload
{
    /// <summary>The canonical identifier of the state area this event belongs to.</summary>
    public required string StateArea { get; init; }

    /// <summary>The revision a recipient must already hold for this event to apply.</summary>
    public required ulong BaseRevision { get; init; }

    /// <summary>The revision this event advances the state area to.</summary>
    public required ulong Revision { get; init; }

    /// <summary>UTC wall-clock time this change was captured, for display and diagnostics only -- not an ordering source.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The complete post-change state for this area, not a partial patch.</summary>
    public required JsonElement Data { get; init; }
}
