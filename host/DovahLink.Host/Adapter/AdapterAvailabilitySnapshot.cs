using DovahLink.Host.Identity;

namespace DovahLink.Host.Adapter;

/// <summary>
/// A single, internally consistent read of <see cref="IAdapterAvailabilityTracker"/>'s state,
/// taken under one lock so a caller that needs more than one of these values together never
/// combines them from separately synchronized reads.
/// </summary>
/// <param name="Current">Whether an adapter is currently connected.</param>
/// <param name="CurrentInstanceId">The most recently connected adapter's instance identity, or <see langword="null"/> if none has ever connected.</param>
/// <param name="NeedsResynchronization">Whether a resynchronization handshake must complete before adapter-sourced state can be published as current.</param>
/// <param name="ConnectionGeneration">The monotonically increasing connection generation for the current adapter instance.</param>
public sealed record AdapterAvailabilitySnapshot(
    AdapterAvailability Current,
    AdapterInstanceId? CurrentInstanceId,
    bool NeedsResynchronization,
    long ConnectionGeneration);
