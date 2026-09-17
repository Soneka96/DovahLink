using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for <see cref="LiveStateCatalog.Default"/>.</summary>
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
}
