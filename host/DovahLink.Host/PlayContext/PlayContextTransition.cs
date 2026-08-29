using DovahLink.Host.Identity;

namespace DovahLink.Host.PlayContext;

/// <summary>A single play-context change: loading a save always produces a new <see cref="PlayContextId"/>.</summary>
/// <param name="PreviousPlayContextId">The play context that was active before this transition, or <see langword="null"/> if this is the first transition observed.</param>
/// <param name="NewPlayContextId">The play context now active.</param>
public sealed record PlayContextTransition(PlayContextId? PreviousPlayContextId, PlayContextId NewPlayContextId);
