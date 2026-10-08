using DovahLink.Host.State;

namespace DovahLink.Host.Tests.State;

/// <summary>Tests for live-state catalog validation and resynchronization plans.</summary>
public class LiveStateCatalogInfrastructureTests
{
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
            [new StateAreaId("area-a")]);
        CaptureUnitDefinition second = new(
            CaptureSourceKind.Sample,
            999,
            RateClass.Medium,
            SynchronizationRole.BaselineSample,
            [new StateAreaId("area-b")]);

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
