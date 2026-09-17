namespace DovahLink.Host.State;

/// <summary>
/// The host-owned declaration of every capture unit and state area Stage 4's "Real capture and host
/// integration" slice defines, per
/// <c>roadmap/04-live-state-synchronization-foundation.md</c>. Purely declarative data: it does not
/// decode payload bytes, schedule captures, or apply state -- those remain the composed
/// <c>LiveCaptureSink</c> and <c>LiveStateScheduler</c>'s own jobs, so this catalog stays a plain
/// lookup rather than a service locator.
/// </summary>
public sealed class LiveStateCatalog
{
    /// <summary>Creates a catalog from its complete capture unit and state area lists.</summary>
    /// <param name="captureUnits">Every capture unit this catalog defines.</param>
    /// <param name="stateAreas">Every state area this catalog defines.</param>
    public LiveStateCatalog(IReadOnlyList<CaptureUnitDefinition> captureUnits, IReadOnlyList<StateAreaDefinition> stateAreas)
    {
        CaptureUnits = captureUnits;
        StateAreas = stateAreas;
    }

    /// <summary>Every capture unit this catalog defines.</summary>
    public IReadOnlyList<CaptureUnitDefinition> CaptureUnits { get; }

    /// <summary>Every state area this catalog defines.</summary>
    public IReadOnlyList<StateAreaDefinition> StateAreas { get; }

    /// <summary>
    /// The production catalog: one fast coherent vitals sample (health, magicka, stamina), one
    /// medium experience sample, and one level-up event with its own baseline sample -- the narrow
    /// first state slice named in <c>roadmap/04-live-state-synchronization-foundation.md</c>'s "Real
    /// capture and host integration". None of these five state areas is Slow-rate.
    /// </summary>
    public static LiveStateCatalog Default { get; } = new(
        captureUnits:
        [
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterVitals,
                RateClass.Fast,
                [
                    new StateAreaId(Constants.CharacterHealthStateArea),
                    new StateAreaId(Constants.CharacterMagickaStateArea),
                    new StateAreaId(Constants.CharacterStaminaStateArea),
                ]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterXp,
                RateClass.Medium,
                [new StateAreaId(Constants.CharacterXpStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterLevelBaseline,
                RateClass: null,
                [new StateAreaId(Constants.CharacterLevelStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Event,
                (uint)CharacterEventKey.CharacterLevelChanged,
                RateClass: null,
                [new StateAreaId(Constants.CharacterLevelStateArea)]),
        ],
        stateAreas:
        [
            new StateAreaDefinition(new StateAreaId(Constants.CharacterHealthStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterMagickaStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterStaminaStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterXpStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterLevelStateArea), UpdateMode.Event),
        ]);
}
