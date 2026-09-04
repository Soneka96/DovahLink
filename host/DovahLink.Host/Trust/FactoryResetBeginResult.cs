namespace DovahLink.Host.Trust;

/// <summary>
/// The result of requesting a new Factory Reset confirmation challenge. <see cref="Challenge"/> is
/// populated only when <see cref="Outcome"/> is <see cref="FactoryResetBeginOutcome.Started"/>: a
/// caller must never be handed a freshly generated challenge that did not actually become the active,
/// confirmable one.
/// </summary>
/// <param name="Outcome">Whether the returned challenge became active.</param>
/// <param name="Challenge">The active challenge whose code must be confirmed, or <see langword="null"/> when <paramref name="Outcome"/> is <see cref="FactoryResetBeginOutcome.AlreadyInProgress"/>.</param>
public sealed record FactoryResetBeginResult(FactoryResetBeginOutcome Outcome, FactoryResetChallenge? Challenge);
