using DovahLink.Host;
using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Verifies the production state-area and capture catalog.</summary>
public class LiveStateCatalogTests
{
    /// <summary>Verifies that production exposes independent areas with their canonical modes.</summary>
    [Fact]
    public void Default_RegistersOnlyCanonicalCharacterAreas()
    {
        Dictionary<string, UpdateMode> actual = LiveStateCatalog.Default.StateAreas
            .ToDictionary(area => area.Id.Value, area => area.UpdateMode);
        Dictionary<string, UpdateMode> expected = new()
        {
            [Constants.CharacterVitalsStateArea] = UpdateMode.Snapshot,
            [Constants.CharacterXpStateArea] = UpdateMode.Snapshot,
            [Constants.CharacterIdentityStateArea] = UpdateMode.Snapshot,
            [Constants.CharacterSupernaturalTraitsStateArea] = UpdateMode.Snapshot,
            [Constants.PlayerLocationStateArea] = UpdateMode.Snapshot,
            [Constants.GameTimeStateArea] = UpdateMode.Snapshot,
            [Constants.CharacterLevelStateArea] = UpdateMode.Event,
        };

        Assert.Equal(expected.OrderBy(pair => pair.Key), actual.OrderBy(pair => pair.Key));
    }

    /// <summary>Verifies that the Fast Vitals sample feeds exactly one public area.</summary>
    [Fact]
    public void Default_VitalsCaptureUnit_FeedsOneFastSnapshotArea()
    {
        CaptureUnitDefinition vitals = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterVitals);

        Assert.Equal(RateClass.Fast, vitals.RateClass);
        Assert.Equal(SynchronizationRole.BaselineSample, vitals.SynchronizationRole);
        Assert.Equal([new StateAreaId(Constants.CharacterVitalsStateArea)], vitals.StateAreas);
    }

    /// <summary>Verifies that XP remains its own Medium Snapshot area.</summary>
    [Fact]
    public void Default_XpCaptureUnit_RemainsIndependentAtMediumCadence()
    {
        CaptureUnitDefinition xp = LiveStateCatalog.Default.CaptureUnits.Single(
            unit => unit.Source == CaptureSourceKind.Sample
                && unit.CaptureKey == (uint)CharacterSampleToken.CharacterXp);

        Assert.Equal(RateClass.Medium, xp.RateClass);
        Assert.Equal(SynchronizationRole.BaselineSample, xp.SynchronizationRole);
        Assert.Equal([new StateAreaId(Constants.CharacterXpStateArea)], xp.StateAreas);
    }

    /// <summary>Verifies metadata, location, and calendar use independent Slow baseline samples and Snapshot areas.</summary>
    [Fact]
    public void Default_SlowCaptureUnits_UseIndependentSnapshotAreas()
    {
        (CharacterSampleToken Token, string StateArea)[] metadataSamples =
        [
            (CharacterSampleToken.CharacterIdentity, Constants.CharacterIdentityStateArea),
            (CharacterSampleToken.CharacterSupernaturalTraits, Constants.CharacterSupernaturalTraitsStateArea),
            (CharacterSampleToken.PlayerLocation, Constants.PlayerLocationStateArea),
            (CharacterSampleToken.GameTime, Constants.GameTimeStateArea),
        ];

        foreach ((CharacterSampleToken token, string stateArea) in metadataSamples)
        {
            CaptureUnitDefinition unit = LiveStateCatalog.Default.CaptureUnits.Single(
                candidate => candidate.Source == CaptureSourceKind.Sample
                    && candidate.CaptureKey == (uint)token);
            Assert.Equal(RateClass.Slow, unit.RateClass);
            Assert.Equal(SynchronizationRole.BaselineSample, unit.SynchronizationRole);
            Assert.Equal([new StateAreaId(stateArea)], unit.StateAreas);
            Assert.Equal(UpdateMode.Snapshot, LiveStateCatalog.Default.StateAreas
                .Single(area => area.Id == new StateAreaId(stateArea)).UpdateMode);
        }
    }

    /// <summary>Verifies that Level keeps its baseline sample and persistent Event sources.</summary>
    [Fact]
    public void Default_LevelArea_KeepsBaselineAndEventSources()
    {
        StateAreaId levelArea = new(Constants.CharacterLevelStateArea);
        CaptureUnitDefinition[] sources = LiveStateCatalog.Default.CaptureUnits
            .Where(unit => unit.StateAreas.Contains(levelArea))
            .ToArray();

        Assert.Equal(2, sources.Length);
        Assert.Contains(sources, unit => unit.Source == CaptureSourceKind.Sample
            && unit.CaptureKey == (uint)CharacterSampleToken.CharacterLevelBaseline
            && unit.RateClass is null
            && unit.SynchronizationRole == SynchronizationRole.BaselineSample);
        Assert.Contains(sources, unit => unit.Source == CaptureSourceKind.Event
            && unit.CaptureKey == (uint)CharacterEventKey.CharacterLevelChanged
            && unit.RateClass is null
            && unit.SynchronizationRole == SynchronizationRole.PersistentEvent);
    }

    /// <summary>Verifies that the production catalog contributes one token for each baseline sample and its Level Event key.</summary>
    [Fact]
    public void Default_BuildsExpectedResynchronizationPlan()
    {
        ResynchronizationPlan plan = LiveStateCatalog.Default.BuildResynchronizationPlan();

        Assert.Equal([(uint)CharacterEventKey.CharacterLevelChanged], plan.PersistentEventKeys);
        uint[] expectedSamples =
        [
            (uint)CharacterSampleToken.CharacterVitals,
            (uint)CharacterSampleToken.CharacterXp,
            (uint)CharacterSampleToken.CharacterIdentity,
            (uint)CharacterSampleToken.CharacterSupernaturalTraits,
            (uint)CharacterSampleToken.PlayerLocation,
            (uint)CharacterSampleToken.GameTime,
            (uint)CharacterSampleToken.CharacterLevelBaseline,
        ];
        Assert.Equal(expectedSamples.Order(), plan.BaselineSampleTokens.Order());
    }
}
