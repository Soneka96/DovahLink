using DovahLink.Host.Adapter;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Composition;

/// <summary>Tests for <see cref="CoreServiceExtensions.ComposeCoreServices"/>.</summary>
public class CoreServiceExtensionsTests
{
    /// <summary>Verifies that every service in the composed bundle is actually constructed, not left as a default/omitted field.</summary>
    [Fact]
    public void ComposeCoreServices_ReturnsNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();

        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);

        Assert.NotNull(core.AdapterAvailability);
        Assert.NotNull(core.Clock);
        Assert.NotNull(core.Settings);
        Assert.NotNull(core.SecurityGate);
        Assert.NotNull(core.StateAuthorityLifecycle);
    }

    /// <summary>Verifies that the resolved settings come from the supplied provider rather than the shipped default.</summary>
    [Fact]
    public void ComposeCoreServices_HostSettingsProviderSupplied_SettingsUsesResolvedCap()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };

        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown, hostSettingsProvider);

        Assert.Equal(2, core.Settings.MaxActiveSessions);
    }

    /// <summary>
    /// Verifies that the returned <see cref="IStateAuthorityLifecycle"/> is wired to the same
    /// <see cref="IAdapterAvailabilityTracker"/> instance also returned in <see cref="CoreServices"/>
    /// -- proving the two are actually composed together, not merely two independently constructed
    /// services -- by committing and publishing a real availability loss through the returned tracker
    /// and observing the returned lifecycle rotate in response.
    /// </summary>
    [Fact]
    public void ComposeCoreServices_AdapterAvailabilityLossPublishedThroughReturnedTracker_RotatesReturnedStateAuthorityLifecycle()
    {
        using var shutdown = new CancellationTokenSource();

        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        StateAuthorityId before = core.StateAuthorityLifecycle.Current;

        var instanceId = AdapterInstanceId.NewId();
        AdapterAvailabilityTransition? connected = core.AdapterAvailability.CommitConnected(instanceId, generation: 1);
        Assert.NotNull(connected);
        core.AdapterAvailability.PublishTransition(connected);
        AdapterAvailabilityTransition? disconnected = core.AdapterAvailability.CommitDisconnected(instanceId, connectionGeneration: 1);
        Assert.NotNull(disconnected);
        core.AdapterAvailability.PublishTransition(disconnected);

        Assert.NotEqual(before, core.StateAuthorityLifecycle.Current);
    }
}
