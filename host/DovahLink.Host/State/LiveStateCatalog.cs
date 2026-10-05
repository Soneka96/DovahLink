namespace DovahLink.Host.State;

/// <summary>
/// The Host-owned declaration of capture units and state areas. It can derive bounded
/// Adapter resynchronization plans through <see cref="BuildResynchronizationPlan"/>, but does not decode
/// payload bytes, schedule captures, or apply state; those remain the composed
/// <see cref="DovahLink.Host.Adapter.Ipc.LiveCaptureSink"/> and
/// <see cref="DovahLink.Host.Adapter.Ipc.LiveStateScheduler"/>'s jobs. The catalog remains
/// declarative data rather than a service locator.
/// </summary>
public sealed class LiveStateCatalog
{
    /// <summary>Creates a catalog from its complete capture unit and state area lists.</summary>
    /// <param name="captureUnits">Every capture unit this catalog defines.</param>
    /// <param name="stateAreas">Every state area this catalog defines.</param>
    /// <exception cref="InvalidOperationException">A capture unit is an Event with a rate class, or two capture units share the same source and capture key.</exception>
    public LiveStateCatalog(IReadOnlyList<CaptureUnitDefinition> captureUnits, IReadOnlyList<StateAreaDefinition> stateAreas)
    {
        HashSet<(CaptureSourceKind Source, uint CaptureKey)> identities = [];
        foreach (CaptureUnitDefinition unit in captureUnits)
        {
            if (unit.Source == CaptureSourceKind.Event && unit.RateClass is not null)
            {
                throw new InvalidOperationException($"Event capture key {unit.CaptureKey} cannot have a rate class.");
            }

            if (!identities.Add((unit.Source, unit.CaptureKey)))
            {
                throw new InvalidOperationException(
                    $"Capture identity ({unit.Source}, {unit.CaptureKey}) is duplicated in the live-state catalog.");
            }
        }

        CaptureUnits = captureUnits;
        StateAreas = stateAreas;
    }

    /// <summary>Every capture unit this catalog defines.</summary>
    public IReadOnlyList<CaptureUnitDefinition> CaptureUnits { get; }

    /// <summary>Every state area this catalog defines.</summary>
    public IReadOnlyList<StateAreaDefinition> StateAreas { get; }

    /// <summary>
    /// The production catalog: one fast coherent Vitals sample, one medium XP sample, three slow
    /// Snapshot domains (including the Host-orchestrated quest capture), and one Level event with
    /// its own baseline sample.
    /// </summary>
    public static LiveStateCatalog Default { get; } = new(
        captureUnits:
        [
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterVitals,
                RateClass.Fast,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterVitalsStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterXp,
                RateClass.Medium,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterXpStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterIdentity,
                RateClass.Slow,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterIdentityStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterSupernaturalTraits,
                RateClass.Slow,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterSupernaturalTraitsStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.PlayerLocation,
                RateClass.Slow,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.PlayerLocationStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.GameTime,
                RateClass.Slow,
                SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.GameTimeStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)TrackedQuestCaptureKey.Page,
                RateClass: null,
                SynchronizationRole.HostOrchestratedBaseline,
                [new StateAreaId(Constants.TrackedQuestsStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Sample,
                (uint)CharacterSampleToken.CharacterLevelBaseline,
                RateClass: null,
                SynchronizationRole: SynchronizationRole.BaselineSample,
                [new StateAreaId(Constants.CharacterLevelStateArea)]),
            new CaptureUnitDefinition(
                CaptureSourceKind.Event,
                (uint)CharacterEventKey.CharacterLevelChanged,
                RateClass: null,
                SynchronizationRole: SynchronizationRole.PersistentEvent,
                [new StateAreaId(Constants.CharacterLevelStateArea)]),
        ],
        stateAreas:
        [
            new StateAreaDefinition(new StateAreaId(Constants.CharacterVitalsStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterXpStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterIdentityStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterSupernaturalTraitsStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.PlayerLocationStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.GameTimeStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.TrackedQuestsStateArea), UpdateMode.Snapshot),
            new StateAreaDefinition(new StateAreaId(Constants.CharacterLevelStateArea), UpdateMode.Event),
        ]);

    /// <summary>
    /// Builds the bounded Adapter event and sample intents from roles owned by the Adapter, preserving
    /// catalog order for each unique key. Host-orchestrated baselines remain transaction requirements
    /// but add no Adapter resynchronization token.
    /// </summary>
    /// <returns>The plan derived from this catalog's capture units.</returns>
    /// <exception cref="InvalidOperationException">A capture has an invalid source/role, zero key, or exceeds a plan bound.</exception>
    public ResynchronizationPlan BuildResynchronizationPlan()
    {
        List<uint> eventKeys = [];
        HashSet<uint> seenEventKeys = [];
        List<uint> sampleTokens = [];
        HashSet<uint> seenSampleTokens = [];

        foreach (CaptureUnitDefinition unit in CaptureUnits)
        {
            switch (unit.SynchronizationRole)
            {
                case SynchronizationRole.PersistentEvent:
                    if (unit.Source != CaptureSourceKind.Event)
                    {
                        throw new InvalidOperationException(
                            $"Capture key {unit.CaptureKey} has synchronization role {unit.SynchronizationRole} but source {unit.Source}.");
                    }

                    AddResynchronizationKey(
                        unit.CaptureKey,
                        eventKeys,
                        seenEventKeys,
                        Constants.MaxResynchronizationEventKeys,
                        "persistent event");
                    break;

                case SynchronizationRole.BaselineSample:
                    if (unit.Source != CaptureSourceKind.Sample)
                    {
                        throw new InvalidOperationException(
                            $"Capture key {unit.CaptureKey} has synchronization role {unit.SynchronizationRole} but source {unit.Source}.");
                    }

                    AddResynchronizationKey(
                        unit.CaptureKey,
                        sampleTokens,
                        seenSampleTokens,
                        Constants.MaxResynchronizationSampleTokens,
                        "baseline sample");
                    break;

                case SynchronizationRole.HostOrchestratedBaseline:
                    if (unit.Source != CaptureSourceKind.Sample || unit.RateClass is not null)
                    {
                        throw new InvalidOperationException(
                            $"Capture key {unit.CaptureKey} has invalid source or rate class for Host-orchestrated baseline role.");
                    }
                    if (unit.CaptureKey == 0)
                    {
                        throw new InvalidOperationException("A Host-orchestrated baseline capture key must be nonzero.");
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Capture key {unit.CaptureKey} has unsupported synchronization role {unit.SynchronizationRole}.");
            }
        }

        return new ResynchronizationPlan(eventKeys.ToArray(), sampleTokens.ToArray());
    }

    /// <summary>Adds one nonzero intent key once, failing when the resulting plan would exceed its bound.</summary>
    /// <param name="key">The event key or sample token to include.</param>
    /// <param name="keys">The ordered list being built.</param>
    /// <param name="seenKeys">The keys already added to that list.</param>
    /// <param name="maximumCount">The maximum number of unique keys allowed.</param>
    /// <param name="intentName">The intent kind used in failure messages.</param>
    /// <exception cref="InvalidOperationException">The key is zero or adding it would exceed <paramref name="maximumCount"/>.</exception>
    private static void AddResynchronizationKey(
        uint key,
        List<uint> keys,
        HashSet<uint> seenKeys,
        int maximumCount,
        string intentName)
    {
        if (key == 0)
        {
            throw new InvalidOperationException($"A {intentName} capture key must be nonzero.");
        }

        if (!seenKeys.Add(key))
        {
            return;
        }

        if (keys.Count >= maximumCount)
        {
            throw new InvalidOperationException(
                $"The catalog exceeds the maximum of {maximumCount} {intentName} intents in a resynchronization plan.");
        }

        keys.Add(key);
    }
}
