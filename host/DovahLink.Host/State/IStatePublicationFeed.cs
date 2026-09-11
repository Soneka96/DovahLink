using System.Diagnostics.CodeAnalysis;

namespace DovahLink.Host.State;

/// <summary>
/// The domain-agnostic, already-JSON-encoded push source a per-session delivery queue consumes to
/// answer <c>subscribe</c> and <c>snapshot_request</c> and to forward ongoing <c>state_event</c>
/// updates. Deliberately decoupled from <see cref="IStatePublisher{TState}"/>'s strongly-typed
/// domain storage: a later concept adapts each concrete registered domain's captured values into
/// this feed once that domain's C# type and wire mapping exist, so this contract and its consumers
/// never depend on a concrete Skyrim domain shape.
///
/// The implementation backing this feed owns the following ordering and freshness guarantees --
/// matching the single authoritative per-state-area ordering point that assigns revisions before
/// either member of this interface can observe them -- so that no caller needs to re-derive them
/// independently:
/// <list type="bullet">
/// <item>For one <see cref="StateAreaId"/>, <see cref="EventOccurred"/> is never invoked
/// concurrently for two different events, and is invoked in strictly increasing
/// <see cref="StateEventPublication.Revision"/> order.</item>
/// <item><see cref="TryGetSnapshot"/> never returns a value for an area whose revision is older
/// than the most recent <see cref="StateEventPublication.Revision"/> already raised through
/// <see cref="EventOccurred"/> for that same area -- a read is never allowed to appear staler than
/// an event this feed has already announced.</item>
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
    /// Tries to read a state area's current value as a fresh baseline snapshot. See this
    /// interface's own summary for the freshness guarantee this read makes relative to
    /// <see cref="EventOccurred"/>.
    /// </summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="snapshot">The area's current value as a snapshot, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot);
}
