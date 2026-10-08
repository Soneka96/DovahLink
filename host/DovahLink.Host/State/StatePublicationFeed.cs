using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.PlayContext;

namespace DovahLink.Host.State;

/// <inheritdoc cref="IStatePublicationFeed"/>
/// <remarks>
/// A stateless view over <see cref="IAuthoritativeStateStore"/>, the single owner of current state: it
/// holds no snapshot, revision, or value of its own, so it can never disagree with that owner. Every
/// read and subscription is forwarded to the store as-is. It exists only so consumers depend on the
/// domain-agnostic, read-only <see cref="IStatePublicationFeed"/> contract rather than on the store's
/// write surface.
/// </remarks>
public sealed class StatePublicationFeed : IStatePublicationFeed
{
    /// <summary>The single owner of authoritative state this feed projects.</summary>
    private readonly IAuthoritativeStateStore stateStore;

    /// <summary>Creates a feed projecting <paramref name="stateStore"/>.</summary>
    /// <param name="stateStore">The authoritative store every read and subscription is forwarded to.</param>
    public StatePublicationFeed(IAuthoritativeStateStore stateStore)
    {
        this.stateStore = stateStore;
    }

    /// <inheritdoc/>
    public event Action<StateEventPublication>? EventOccurred
    {
        add => stateStore.EventOccurred += value;
        remove => stateStore.EventOccurred -= value;
    }

    /// <inheritdoc/>
    public event Action<StateSnapshotPublication>? SnapshotChanged
    {
        add => stateStore.SnapshotChanged += value;
        remove => stateStore.SnapshotChanged -= value;
    }

    /// <inheritdoc/>
    public event Action? SnapshotAvailabilityChanged
    {
        add => stateStore.SnapshotAvailabilityChanged += value;
        remove => stateStore.SnapshotAvailabilityChanged -= value;
    }

    /// <inheritdoc/>
    public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot) =>
        stateStore.TryGetSnapshot(areaId, out snapshot);

    /// <inheritdoc/>
    public StateSnapshotPublication CreateUnavailableBoundaryBaseline(StateAreaId areaId, PlayContextSnapshot playContext, DateTimeOffset occurredAt) =>
        stateStore.CreateUnavailableBoundaryBaseline(areaId, playContext, occurredAt);
}
