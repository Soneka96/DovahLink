using DovahLink.Host.Adapter;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>Tests for <see cref="CoreServiceExtensions.AddCoreServices"/>.</summary>
public class CoreServiceExtensionsTests
{
    /// <summary>Verifies that every core service resolves to a non-null instance, not left unregistered.</summary>
    [Fact]
    public void AddCoreServices_ResolvesNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddCoreServices(new SystemClock(), new SecurityStateGate(), shutdown);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAdapterAvailabilityTracker>());
        Assert.NotNull(provider.GetRequiredService<IClock>());
        Assert.NotNull(provider.GetRequiredService<HostSettings>());
        Assert.NotNull(provider.GetRequiredService<ISecurityStateGate>());
        Assert.NotNull(provider.GetRequiredService<IStateAuthorityLifecycle>());
    }

    /// <summary>Verifies that the resolved settings come from the supplied provider rather than the shipped default.</summary>
    [Fact]
    public void AddCoreServices_HostSettingsProviderSupplied_SettingsUsesResolvedCap()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        var services = new ServiceCollection();
        services.AddCoreServices(new SystemClock(), new SecurityStateGate(), shutdown, hostSettingsProvider);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetRequiredService<HostSettings>().MaxActiveSessions);
    }

    /// <summary>
    /// Verifies that the registered <see cref="IStateAuthorityLifecycle"/> is wired to the same
    /// <see cref="IAdapterAvailabilityTracker"/> singleton -- not an independently constructed,
    /// unwired service -- by committing and publishing a real availability loss through the resolved
    /// tracker and observing the resolved lifecycle rotate in response.
    /// </summary>
    [Fact]
    public void AddCoreServices_AdapterAvailabilityLossPublishedThroughResolvedTracker_RotatesResolvedStateAuthorityLifecycle()
    {
        using var shutdown = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddCoreServices(new SystemClock(), new SecurityStateGate(), shutdown);
        using ServiceProvider provider = services.BuildServiceProvider();
        IAdapterAvailabilityTracker tracker = provider.GetRequiredService<IAdapterAvailabilityTracker>();
        IStateAuthorityLifecycle lifecycle = provider.GetRequiredService<IStateAuthorityLifecycle>();
        StateAuthorityId before = lifecycle.Current;

        var instanceId = AdapterInstanceId.NewId();
        AdapterAvailabilityTransition? connected = tracker.CommitConnected(instanceId, generation: 1);
        Assert.NotNull(connected);
        tracker.PublishTransition(connected);
        AdapterAvailabilityTransition? disconnected = tracker.CommitDisconnected(instanceId, connectionGeneration: 1);
        Assert.NotNull(disconnected);
        tracker.PublishTransition(disconnected);

        Assert.NotEqual(before, lifecycle.Current);
    }

    /// <summary>Verifies that resolving a host-lifetime singleton twice from the same provider returns the same instance.</summary>
    [Fact]
    public void AddCoreServices_ClockResolvedTwice_ReturnsSameInstance()
    {
        using var shutdown = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddCoreServices(new SystemClock(), new SecurityStateGate(), shutdown);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IClock>(), provider.GetRequiredService<IClock>());
    }
}
