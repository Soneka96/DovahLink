using DovahLink.Host.Identity;

namespace DovahLink.Host.PlayContext;

/// <summary>A coherent read of the current play context and its transition generation.</summary>
/// <param name="Current">The currently active play context, or <see langword="null"/> if none has been notified yet.</param>
/// <param name="TransitionGeneration">The transition generation this snapshot was read at.</param>
public sealed record PlayContextSnapshot(PlayContextId? Current, long TransitionGeneration);
