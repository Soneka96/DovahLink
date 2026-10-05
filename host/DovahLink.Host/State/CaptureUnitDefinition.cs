namespace DovahLink.Host.State;

/// <summary>
/// One <see cref="LiveStateCatalog"/>-defined capture unit: one host-owned sample token or event
/// key, opaque to the adapter beyond mapping it to its one approved native operation, and the
/// state area(s) it feeds. Several state areas may share one coherent capture (for example one
/// vitals sample feeding current and maximum health, magicka, and stamina); one state area may in
/// turn be fed by more
/// than one capture unit (for example the level baseline sample and the level-changed event both
/// feed <c>character_level</c>).
/// </summary>
/// <param name="Source">Which host-owned key namespace <paramref name="CaptureKey"/> belongs to.</param>
/// <param name="CaptureKey">The sample token, event key, or host-owned private capture key for this unit.</param>
/// <param name="RateClass">
/// The cadence a scheduler polls this capture unit at, or <see langword="null"/> when it is not
/// polled on any cadence -- either because it is event-sourced, because it is a sample used only
/// to establish a resynchronization baseline, or because the Host orchestrates a bounded multi-page
/// baseline collection itself.
/// </param>
/// <param name="SynchronizationRole">This unit's role in resynchronization, independent of <paramref name="RateClass"/>.</param>
/// <param name="StateAreas">Every state area this capture unit's value is applied to.</param>
public sealed record CaptureUnitDefinition(
    CaptureSourceKind Source,
    uint CaptureKey,
    RateClass? RateClass,
    SynchronizationRole SynchronizationRole,
    IReadOnlyList<StateAreaId> StateAreas);
