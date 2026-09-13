using System.Text.Json;

namespace DovahLink.Host.Client.Protocol;

/// <summary>
/// The <c>state_snapshot</c> message payload, per <c>protocol/schema/README.md</c>'s "State
/// envelope" Snapshot shape and its "<c>state_snapshot</c>" section. Host-originated only: contains
/// the complete state for one subscribed or recovered state area at a revision.
/// </summary>
public sealed record StateSnapshotPayload
{
    /// <summary>The canonical identifier of the state area this snapshot belongs to.</summary>
    public required string StateArea { get; init; }

    /// <summary>The revision this snapshot is current as of.</summary>
    public required ulong Revision { get; init; }

    /// <summary>UTC wall-clock time this value was captured, for display and diagnostics only -- not an ordering source.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The complete, already-encoded state-area contract for this area.</summary>
    public required JsonElement Data { get; init; }
}
