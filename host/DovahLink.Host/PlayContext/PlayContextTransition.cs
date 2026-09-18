using DovahLink.Host.Identity;

namespace DovahLink.Host.PlayContext;

/// <summary>
/// A single play-context change: loading a save always produces a new <see cref="PlayContextId"/>;
/// the play context ending (loading has started, or the player returned to the main menu) produces
/// a transition to <see langword="null"/> instead.
/// </summary>
/// <param name="PreviousPlayContextId">The play context that was active before this transition, or <see langword="null"/> if this is the first transition observed.</param>
/// <param name="NewPlayContextId">The play context now active, or <see langword="null"/> if the play context just ended.</param>
public sealed record PlayContextTransition(PlayContextId? PreviousPlayContextId, PlayContextId? NewPlayContextId);
