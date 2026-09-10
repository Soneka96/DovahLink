using System.Diagnostics.CodeAnalysis;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.TestDoubles;

/// <summary>A controllable stand-in for <see cref="IStatePublicationFeed"/> with settable snapshot values and a way to raise events.</summary>
public sealed class FakeStatePublicationFeed : IStatePublicationFeed
{
    /// <summary>The value each area's <see cref="TryGetSnapshot"/> call returns.</summary>
    private readonly Dictionary<StateAreaId, StateSnapshotPublication> snapshotsByArea = [];

    /// <inheritdoc/>
    public event Action<StateEventPublication>? EventOccurred;

    /// <summary>Sets the value <see cref="TryGetSnapshot"/> returns for <paramref name="areaId"/>.</summary>
    /// <param name="areaId">The state area to set a value for.</param>
    /// <param name="snapshot">The value <see cref="TryGetSnapshot"/> should return.</param>
    public void SetSnapshot(StateAreaId areaId, StateSnapshotPublication snapshot) => snapshotsByArea[areaId] = snapshot;

    /// <inheritdoc/>
    public bool TryGetSnapshot(StateAreaId areaId, [MaybeNullWhen(false)] out StateSnapshotPublication snapshot) =>
        snapshotsByArea.TryGetValue(areaId, out snapshot);

    /// <summary>Raises <see cref="EventOccurred"/>, as a real feed would when a registered area's value changes.</summary>
    /// <param name="eventPublication">The event to raise.</param>
    public void RaiseEvent(StateEventPublication eventPublication) => EventOccurred?.Invoke(eventPublication);
}
