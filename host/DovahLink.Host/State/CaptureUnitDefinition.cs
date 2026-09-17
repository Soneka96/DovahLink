namespace DovahLink.Host.State;

/// <summary>
/// One <see cref="LiveStateCatalog"/>-defined capture unit: one host-owned sample token or event
/// key, opaque to the adapter beyond mapping it to its one approved native operation, and the
/// state area(s) it feeds. Several state areas may share one coherent capture (for example one
/// vitals sample feeding health, magicka, and stamina); one state area may in turn be fed by more
/// than one capture unit (for example the level baseline sample and the level-changed event both
/// feed <c>character_level</c>).
/// </summary>
/// <param name="Source">Which host-owned key namespace <paramref name="CaptureKey"/> belongs to.</param>
/// <param name="CaptureKey">The sample token or event key, matching a <c>CharacterSampleToken</c> or <c>CharacterEventKey</c> value.</param>
/// <param name="RateClass">
/// The cadence a scheduler polls this capture unit at, or <see langword="null"/> when it is not
/// polled on any cadence -- either because it is event-sourced, or because it is a sample used only
/// to establish a resynchronization baseline.
/// </param>
/// <param name="StateAreas">Every state area this capture unit's value is applied to.</param>
public sealed record CaptureUnitDefinition(
    CaptureSourceKind Source,
    uint CaptureKey,
    RateClass? RateClass,
    IReadOnlyList<StateAreaId> StateAreas);
