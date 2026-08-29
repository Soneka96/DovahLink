using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter;

/// <summary>A transition in the host's view of adapter availability.</summary>
/// <param name="Previous">The availability before the transition.</param>
/// <param name="Current">The availability after the transition.</param>
/// <param name="InstanceId">The adapter instance associated with the transition.</param>
/// <param name="ConnectionGeneration">The connection generation associated with the transition.</param>
public sealed record AdapterAvailabilityTransition(
    AdapterAvailability Previous,
    AdapterAvailability Current,
    AdapterInstanceId? InstanceId,
    long ConnectionGeneration);
