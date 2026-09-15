using System.IO;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.Process;
using DovahLink.Host.Security;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>Tests for <see cref="HostRuntimeServiceExtensions.AddHostRuntime"/>.</summary>
[Collection(RealSocketAndProcessTestCollection.Name)]
public class HostRuntimeServiceExtensionsTests
{
    /// <summary>Verifies that every Host-runtime service resolves to a non-null instance, not left unregistered.</summary>
    [Fact]
    public async Task AddHostRuntime_ResolvesNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, publicListenerPort: 0);

        Assert.NotNull(provider.GetRequiredService<IHostShutdownSignal>());
        Assert.NotNull(provider.GetRequiredService<IHostRendezvousPublisher>());
        Assert.NotNull(provider.GetRequiredService<IHostRuntime>());
    }

    /// <summary>
    /// Verifies that <see cref="IHostRuntime"/> still resolves -- its optional
    /// <see cref="IPublicWebSocketListener"/> constructor dependency automatically supplied as
    /// <see langword="null"/> by Microsoft.Extensions.DependencyInjection's own optional-parameter
    /// resolution -- when <see cref="PublicClientServiceExtensions.AddPublicClientServices"/> left it
    /// unregistered, rather than throwing.
    /// </summary>
    [Fact]
    public async Task AddHostRuntime_NoPublicListenerRegistered_DovahLinkHostRuntimeStillResolves()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, publicListenerPort: null);

        Assert.NotNull(provider.GetRequiredService<IHostRuntime>());
    }

    /// <summary>
    /// Verifies the concrete <see cref="DovahLinkHostRuntime"/> type is never itself resolvable --
    /// only <see cref="IHostRuntime"/> is registered, closing the last concrete-type DI resolution
    /// D9's no-service-locator refinement flagged.
    /// </summary>
    [Fact]
    public async Task AddHostRuntime_ConcreteDovahLinkHostRuntimeNotDirectlyResolvable()
    {
        using var shutdown = new CancellationTokenSource();
        using ServiceProvider provider = await BuildProviderAsync(shutdown, publicListenerPort: 0);

        Assert.Null(provider.GetService<DovahLinkHostRuntime>());
    }

    /// <summary>Builds a real Core/Trust/AdapterIpc/PublicClient/HostRuntime container -- the same registrations production composes it with.</summary>
    private static async Task<ServiceProvider> BuildProviderAsync(CancellationTokenSource shutdown, int? publicListenerPort)
    {
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());

        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, new FakeHostSettingsProvider());
        services.AddTrustServices(trustStore);
        services.AddAdapterIpcServices(listenerPort: 0, new OwnerLifetimeId(1, 2));
        services.AddPublicClientServices(publicListenerPort);
        services.AddHostRuntime(new HostProcessLifetime(), TextWriter.Null);

        return services.BuildServiceProvider();
    }
}
