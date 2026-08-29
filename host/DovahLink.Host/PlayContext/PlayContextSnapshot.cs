using DovahLink.Host.Identity;

namespace DovahLink.Host.PlayContext;

/// <summary>A coherent read of the current play context and its transition generation.</summary>
public sealed record PlayContextSnapshot(PlayContextId? Current, long TransitionGeneration);
