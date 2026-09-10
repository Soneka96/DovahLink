using System.Diagnostics.CodeAnalysis;

namespace DovahLink.Host.State;

/// <summary>
/// The domain-agnostic, already-JSON-encoded push source a per-session delivery queue consumes to
/// answer <c>subscribe</c> and <c>snapshot_request</c> and to forward ongoing <c>state_event</c>
/// updates. Deliberately decoupled from <see cref="IStatePublisher{TState}"/>'s strongly-typed
/// domain storage: a later concept adapts each concrete registered domain's captured values into
/// this feed once that domain's C# type and wire mapping exist, so this contract and its consumers
/// never depend on a concrete Skyrim domain shape.
/// </summary>
public interface IStatePublicationFeed
{
    /// <summary>
    /// Raised whenever a registered state area's authoritative value changes, carrying the
    /// resulting event. Never raised for an area that is not currently registered.
    /// </summary>
    event Action<StateEventPublication>? EventOccurred;

    /// <summary>Tries to read a state area's current value as a fresh baseline snapshot.</summary>
    /// <param name="areaId">The state area to read.</param>
    /// <param name="snapshot">The area's current value as a snapshot, if available.</param>
    /// <returns><see langword="true"/> if a current value is available.</returns>
    bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot);
}
