using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for live-state catalog validation and resynchronization plans.</summary>
public class LiveStateCatalogTests
{
    /// <summary>Verifies that the default catalog defines exactly the five state areas Stage 4's "Real capture and host integration" slice names, each with its documented update mode.</summary>
    [Fact]
    public void Default_DefinesExactlyTheFiveProductionStateAreasWithTheirUpdateModes()
    {
        Dictionary<string, UpdateMode> byId = LiveStateCatalog.Default.StateAreas.ToDictionary(area => area.Id.Value, area => area.UpdateMode);

        Assert.Equal(5, byId.Count);
        Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterHealthStateArea]);
        Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterMagickaStateArea]);
        Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterStaminaStateArea]);
        Assert.Equal(UpdateMode.Snapshot, byId[Constants.CharacterXpStateArea]);
        Assert.Equal(UpdateMode.Event, byId[Constants.CharacterLevelStateArea]);
    }

    /// <summary>Verifies that the vitals capture unit is one coherent Fast sample feeding all three resource areas.</summary>
    [Fact]
    public void Default_VitalsCaptureUnit_IsOneFastSampleFeedingAllThreeResourceAreas()
    {
        CaptureUnitDefinition vitals = LiveStateCatalog.Default.CaptureUnits.Single(unit => unit.Source == CaptureSourceKind.Sample && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);

        Assert.Equal(CaptureSourceKind.Sample, vitals.Source);
        Assert.Equal(RateClass.Fast, vitals.RateClass);
        Assert.Equal(SynchronizationRole.BaselineSample, vitals.SynchronizationRole);
        Assert.Equal(
            [new StateAreaId(Constants.CharacterHealthStateArea), new StateAreaId(Constants.CharacterMagickaStateArea), new StateAreaId(Constants.CharacterStaminaStateArea)],
            vitals.StateAreas);
    }

    /// <summary>Verifies that the experience capture unit is a Medium sample feeding only the experience area.</summary>
    [Fact]
    public void Default_XpCaptureUnit_IsMediumSampleFeedingOnlyXpArea()
    {
        CaptureUnitDefinition xp = LiveStateCatalog.Default.CaptureUnits.Single(unit => unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp && unit.Source == CaptureSourceKind.Sample);

        Assert.Equal(RateClass.Medium, xp.RateClass);
        Assert.Equal(SynchronizationRole.BaselineSample, xp.SynchronizationRole);
        Assert.Equal([new StateAreaId(Constants.CharacterXpStateArea)], xp.StateAreas);
    }

    /// <summary>Verifies that the level state area is fed by exactly two capture units: the baseline sample and the level-changed event, neither polled on any cadence.</summary>
    [Fact]
    public void Default_LevelStateArea_IsFedByBaselineSampleAndEventNeitherOnACadence()
    {
        var levelAreaId = new StateAreaId(Constants.CharacterLevelStateArea);
        List<CaptureUnitDefinition> feedingLevel = LiveStateCatalog.Default.CaptureUnits.Where(unit => unit.StateAreas.Contains(levelAreaId)).ToList();

        Assert.Equal(2, feedingLevel.Count);
        CaptureUnitDefinition baseline = Assert.Single(feedingLevel, unit => unit.Source == CaptureSourceKind.Sample);
        Assert.Equal((uint)CharacterSampleToken.CharacterLevelBaseline, baseline.CaptureKey);
        Assert.Null(baseline.RateClass);
        Assert.Equal(SynchronizationRole.BaselineSample, baseline.SynchronizationRole);
        CaptureUnitDefinition levelChanged = Assert.Single(feedingLevel, unit => unit.Source == CaptureSourceKind.Event);
        Assert.Equal((uint)CharacterEventKey.CharacterLevelChanged, levelChanged.CaptureKey);
        Assert.Null(levelChanged.RateClass);
        Assert.Equal(SynchronizationRole.PersistentEvent, levelChanged.SynchronizationRole);
    }

    /// <summary>
    /// Verifies that SynchronizationRole is not inferable from RateClass: the level baseline sample
    /// has no RateClass (it is polled on no cadence) yet is still a BaselineSample, while Vitals and
    /// XP have a RateClass yet are also BaselineSample -- the two properties vary independently, per
    /// <see cref="DovahLink.Host.SynchronizationRole"/>'s own documentation.
    /// </summary>
    [Fact]
    public void Default_SynchronizationRole_IsIndependentOfRateClass()
    {
        CaptureUnitDefinition[] baselineSamples = [.. LiveStateCatalog.Default.CaptureUnits.Where(unit => unit.SynchronizationRole == SynchronizationRole.BaselineSample)];

        Assert.Equal(3, baselineSamples.Length);
        Assert.Contains(baselineSamples, unit => unit.RateClass == RateClass.Fast);
        Assert.Contains(baselineSamples, unit => unit.RateClass == RateClass.Medium);
        Assert.Contains(baselineSamples, unit => unit.RateClass == null);

        CaptureUnitDefinition persistentEvent = Assert.Single(LiveStateCatalog.Default.CaptureUnits, unit => unit.SynchronizationRole == SynchronizationRole.PersistentEvent);
        Assert.Null(persistentEvent.RateClass);

        //  Exhaustiveness: every capture unit falls into exactly one of the two roles checked above,
        //  so a future unit added with no role assignment (or an unexpected one) cannot silently
        //  evade both counts.
        Assert.Equal(LiveStateCatalog.Default.CaptureUnits.Count, baselineSamples.Length + 1);
    }

    /// <summary>Verifies that every capture unit's state areas are already registered in the catalog's own state-area list, so nothing feeds an area the catalog does not also define.</summary>
    [Fact]
    public void Default_EveryCaptureUnitStateArea_IsDefinedInStateAreas()
    {
        var definedAreaIds = LiveStateCatalog.Default.StateAreas.Select(area => area.Id).ToHashSet();

        foreach (CaptureUnitDefinition unit in LiveStateCatalog.Default.CaptureUnits)
        {
            foreach (StateAreaId areaId in unit.StateAreas)
            {
                Assert.Contains(areaId, definedAreaIds);
            }
        }
    }

    /// <summary>Verifies the inverse of <see cref="Default_EveryCaptureUnitStateArea_IsDefinedInStateAreas"/>: every defined state area is fed by at least one capture unit, so nothing is registered as servable without any way to ever receive a value.</summary>
    [Fact]
    public void Default_EveryStateArea_IsFedByAtLeastOneCaptureUnit()
    {
        var fedAreaIds = LiveStateCatalog.Default.CaptureUnits.SelectMany(unit => unit.StateAreas).ToHashSet();

        foreach (StateAreaDefinition area in LiveStateCatalog.Default.StateAreas)
        {
            Assert.Contains(area.Id, fedAreaIds);
        }
    }

    /// <summary>Verifies that the default catalog builds its event and baseline-sample plan from synchronization roles.</summary>
    [Fact]
    public void Default_BuildsExpectedResynchronizationPlan()
    {
        ResynchronizationPlan plan = LiveStateCatalog.Default.BuildResynchronizationPlan();

        Assert.Equal([(uint)CharacterEventKey.CharacterLevelChanged], plan.PersistentEventKeys);
        uint[] expectedSamples =
        [
            (uint)CharacterSampleToken.CharacterVitals,
            (uint)CharacterSampleToken.CharacterXp,
            (uint)CharacterSampleToken.CharacterLevelBaseline,
        ];
        Assert.Equal(expectedSamples.OrderBy(token => token), plan.BaselineSampleTokens.OrderBy(token => token));
    }

    /// <summary>Verifies that a future baseline sample joins the plan without a scheduling special case.</summary>
    [Fact]
    public void BuildResynchronizationPlan_IncludesFutureSampleToken()
    {
        var catalog = new LiveStateCatalog(
            [new CaptureUnitDefinition(CaptureSourceKind.Sample, 999, RateClass: null, SynchronizationRole.BaselineSample, [])],
            []);

        ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

        Assert.Equal([999u], plan.BaselineSampleTokens);
        Assert.Empty(plan.PersistentEventKeys);
    }

    /// <summary>Verifies that a future persistent event joins the plan without a connection special case.</summary>
    [Fact]
    public void BuildResynchronizationPlan_IncludesFuturePersistentEventKey()
    {
        var catalog = new LiveStateCatalog(
            [new CaptureUnitDefinition(CaptureSourceKind.Event, 998, RateClass: null, SynchronizationRole.PersistentEvent, [])],
            []);

        ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

        Assert.Equal([998u], plan.PersistentEventKeys);
        Assert.Empty(plan.BaselineSampleTokens);
    }

    /// <summary>Verifies that an empty catalog intentionally produces a valid empty plan.</summary>
    [Fact]
    public void BuildResynchronizationPlan_EmptyCatalog_ReturnsNoOpPlan()
    {
        ResynchronizationPlan plan = new LiveStateCatalog([], []).BuildResynchronizationPlan();

        Assert.Empty(plan.PersistentEventKeys);
        Assert.Empty(plan.BaselineSampleTokens);
    }

    /// <summary>Verifies that a repeated Sample identity is rejected even when the units feed different state areas.</summary>
    [Fact]
    public void LiveStateCatalog_DuplicateSampleIdentity_Throws()
    {
        CaptureUnitDefinition first = new(
            CaptureSourceKind.Sample,
            999,
            RateClass.Fast,
            SynchronizationRole.BaselineSample,
            [new StateAreaId(Constants.CharacterHealthStateArea)]);
        CaptureUnitDefinition second = new(
            CaptureSourceKind.Sample,
            999,
            RateClass.Medium,
            SynchronizationRole.BaselineSample,
            [new StateAreaId(Constants.CharacterMagickaStateArea)]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new LiveStateCatalog([first, second], []));

        Assert.Contains("Sample", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    /// <summary>Verifies that a repeated Event identity is rejected during catalog construction.</summary>
    [Fact]
    public void LiveStateCatalog_DuplicateEventIdentity_Throws()
    {
        CaptureUnitDefinition unit = new(
            CaptureSourceKind.Event,
            999,
            RateClass: null,
            SynchronizationRole.PersistentEvent,
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new LiveStateCatalog([unit, unit], []));

        Assert.Contains("Event", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    /// <summary>Verifies that one raw key can identify a Sample and an Event because they occupy different source namespaces.</summary>
    [Fact]
    public void LiveStateCatalog_SameKeyAcrossSources_IsAllowed()
    {
        var catalog = new LiveStateCatalog(
        [
            new CaptureUnitDefinition(CaptureSourceKind.Sample, 999, null, SynchronizationRole.BaselineSample, []),
            new CaptureUnitDefinition(CaptureSourceKind.Event, 999, null, SynchronizationRole.PersistentEvent, []),
        ], []);

        ResynchronizationPlan plan = catalog.BuildResynchronizationPlan();

        Assert.Equal([999u], plan.BaselineSampleTokens);
        Assert.Equal([999u], plan.PersistentEventKeys);
    }

    /// <summary>Verifies that Event captures with a rate class fail during catalog construction with a clear configuration error.</summary>
    [Fact]
    public void LiveStateCatalog_EventWithRateClass_Throws()
    {
        var eventUnit = new CaptureUnitDefinition(
            CaptureSourceKind.Event,
            999,
            RateClass.Fast,
            SynchronizationRole.PersistentEvent,
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new LiveStateCatalog([eventUnit], []));

        Assert.Contains("Event", exception.Message);
        Assert.Contains("rate class", exception.Message);
    }

    /// <summary>Verifies that unique identities at both exact plan bounds remain valid and adding a duplicate identity fails.</summary>
    [Fact]
    public void BuildResynchronizationPlan_AtBoundUniqueCatalogSucceeds_DuplicateAppendThrows()
    {
        CaptureUnitDefinition[] eventUnits = Enumerable.Range(1, Constants.MaxResynchronizationEventKeys)
            .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)key, null, SynchronizationRole.PersistentEvent, []))
            .ToArray();
        CaptureUnitDefinition[] sampleUnits = Enumerable.Range(1, Constants.MaxResynchronizationSampleTokens)
            .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Sample, (uint)key, null, SynchronizationRole.BaselineSample, []))
            .ToArray();

        ResynchronizationPlan plan = new LiveStateCatalog([.. eventUnits, .. sampleUnits], []).BuildResynchronizationPlan();

        Assert.Equal(Constants.MaxResynchronizationEventKeys, plan.PersistentEventKeys.Count);
        Assert.Equal(Constants.MaxResynchronizationSampleTokens, plan.BaselineSampleTokens.Count);
        Assert.Throws<InvalidOperationException>(
            () => new LiveStateCatalog([.. eventUnits, eventUnits[0]], []));
        Assert.Throws<InvalidOperationException>(
            () => new LiveStateCatalog([.. sampleUnits, sampleUnits[0]], []));
    }

    /// <summary>Verifies that source and synchronization role must refer to the same key namespace.</summary>
    /// <param name="source">The capture unit's declared key namespace.</param>
    /// <param name="role">The capture unit's declared resynchronization role.</param>
    [Theory]
    [InlineData(CaptureSourceKind.Event, SynchronizationRole.BaselineSample)]
    [InlineData(CaptureSourceKind.Sample, SynchronizationRole.PersistentEvent)]
    public void BuildResynchronizationPlan_MismatchedSourceAndRole_Throws(
        CaptureSourceKind source,
        SynchronizationRole role)
    {
        var catalog = new LiveStateCatalog(
            [new CaptureUnitDefinition(source, 998, null, role, [])],
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());

        Assert.Contains("synchronization role", exception.Message);
        Assert.Contains("source", exception.Message);
    }

    /// <summary>Verifies that zero catalog keys cannot become resynchronization intents.</summary>
    [Fact]
    public void BuildResynchronizationPlan_ZeroCaptureKey_Throws()
    {
        var catalog = new LiveStateCatalog(
            [new CaptureUnitDefinition(CaptureSourceKind.Sample, 0, null, SynchronizationRole.BaselineSample, [])],
            []);

        Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());
    }

    /// <summary>Verifies that an undefined synchronization role fails with a clear catalog error.</summary>
    [Fact]
    public void BuildResynchronizationPlan_UnsupportedRole_Throws()
    {
        var catalog = new LiveStateCatalog(
            [new CaptureUnitDefinition(CaptureSourceKind.Sample, 1, null, (SynchronizationRole)byte.MaxValue, [])],
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => catalog.BuildResynchronizationPlan());

        Assert.Contains("unsupported synchronization role", exception.Message);
    }

    /// <summary>Verifies that the Host plan builder fails instead of exceeding either private IPC count bound.</summary>
    [Fact]
    public void BuildResynchronizationPlan_OverBoundNamespace_Throws()
    {
        CaptureUnitDefinition[] tooManyEvents = Enumerable.Range(1, Constants.MaxResynchronizationEventKeys + 1)
            .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Event, (uint)key, null, SynchronizationRole.PersistentEvent, []))
            .ToArray();
        CaptureUnitDefinition[] tooManySamples = Enumerable.Range(1, Constants.MaxResynchronizationSampleTokens + 1)
            .Select(key => new CaptureUnitDefinition(CaptureSourceKind.Sample, (uint)key, null, SynchronizationRole.BaselineSample, []))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => new LiveStateCatalog(tooManyEvents, []).BuildResynchronizationPlan());
        Assert.Throws<InvalidOperationException>(() => new LiveStateCatalog(tooManySamples, []).BuildResynchronizationPlan());
    }
}
