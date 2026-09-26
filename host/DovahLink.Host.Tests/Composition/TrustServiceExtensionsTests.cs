using System.IO;
using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Pairing;
using DovahLink.Host.PlayContext;
using DovahLink.Host.Security;
using DovahLink.Host.Sessions;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;
using DovahLink.Host.Trust;
using Microsoft.Extensions.DependencyInjection;

namespace DovahLink.Host.Tests.Composition;

/// <summary>Tests for <see cref="TrustServiceExtensions.CreateTrustStoreAsync"/> and <see cref="TrustServiceExtensions.AddTrustServices"/>.</summary>
public class TrustServiceExtensionsTests
{
    /// <summary>Verifies that every trust-graph service resolves to a non-null instance, not left unregistered.</summary>
    [Fact]
    public async Task AddTrustServices_ResolvesNonNullInstanceForEveryService()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITrustStore>());
        Assert.NotNull(provider.GetRequiredService<ISessionRegistry>());
        Assert.NotNull(provider.GetRequiredService<IPairingCoordinator>());
        Assert.NotNull(provider.GetRequiredService<IPlayContextTracker>());
        Assert.NotNull(provider.GetRequiredService<IPublicEnvelopeCodec>());
        Assert.NotNull(provider.GetRequiredService<IPublicSessionConnectionRegistry>());
        Assert.NotNull(provider.GetRequiredService<ISessionTerminationNotifier>());
        Assert.NotNull(provider.GetRequiredService<IClientSessionInvalidator>());
        Assert.NotNull(provider.GetRequiredService<ITrustAdminService>());
        Assert.NotNull(provider.GetRequiredService<ITrustResetService>());
        Assert.NotNull(provider.GetRequiredService<IAdapterTrustAdminRequestHandler>());
    }

    /// <summary>
    /// Verifies that malformed or undecryptable trust persistence fails the bootstrap closed --
    /// propagating before any service in the graph is registered -- rather than silently starting
    /// with a reset or partially loaded trust store.
    /// </summary>
    [Fact]
    public async Task CreateTrustStoreAsync_MalformedTrustPersistence_ThrowsInvalidDataException()
    {
        var persistence = new FakeTrustStorePersistence { ThrowOnLoad = new InvalidDataException("corrupt") };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => TrustServiceExtensions.CreateTrustStoreAsync(new SystemClock(), new SecurityStateGate(), persistence));
    }

    /// <summary>Verifies that the registered session registry's cap comes from the supplied <see cref="HostSettings"/>.</summary>
    [Fact]
    public async Task AddTrustServices_UsesResolvedCapFromCoreServices()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown, hostSettingsProvider);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetRequiredService<ISessionRegistry>().MaxActiveSessions);
    }

    /// <summary>
    /// Verifies that <see cref="ITrustAdminService"/> actually mutates the same <see cref="ITrustStore"/>
    /// singleton resolved alongside it -- not an independently constructed, unwired copy that happens
    /// to start with identical loaded data -- by revoking a pre-seeded trusted device through the
    /// admin service and observing the change directly through the separately resolved trust store.
    /// </summary>
    [Fact]
    public async Task AddTrustServices_TrustAdminServiceMutation_ObservableThroughResolvedTrustStore()
    {
        using var shutdown = new CancellationTokenSource();
        var clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow)
        {
            Incarnation = KnownDeviceIncarnationId.NewId(),
        };
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([record]);
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, persistence);
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITrustAdminService>().RevokeAsync(clientId);

        Assert.Equal(KnownDeviceState.Revoked, provider.GetRequiredService<ITrustStore>().TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies that the registered <see cref="IPublicEnvelopeCodec"/> is actually wired to the same
    /// <see cref="IStateAuthorityLifecycle"/> singleton -- not an independently constructed,
    /// unconfigured codec -- by encoding a <c>hello_ack</c> (a message type that requires
    /// <c>stateAuthorityId</c>) and observing the stamped value match.
    /// </summary>
    [Fact]
    public async Task AddTrustServices_EnvelopeCodecWiredToCoreStateAuthorityLifecycle()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();
        IPublicEnvelopeCodec envelopeCodec = provider.GetRequiredService<IPublicEnvelopeCodec>();

        byte[] encoded = envelopeCodec.Encode(
            PublicMessageType.HelloAck, "message-1", "session-1", null, null, null,
            new HelloAckPayload
            {
                HostId = "81869993-955c-4ba3-a7d0-d35ca86078ea",
                HostName = "Soneka-Desktop",
                HostVersion = Constants.PublicProtocolHostVersion,
                ClientIdentityKind = ClientIdentityKind.Unpaired,
            });
        Assert.True(envelopeCodec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.Equal(provider.GetRequiredService<IStateAuthorityLifecycle>().Current.ToString(), envelope!.StateAuthorityId);
    }

    /// <summary>Verifies that resolving the trust store twice from the same provider returns the same instance.</summary>
    [Fact]
    public async Task AddTrustServices_TrustStoreResolvedTwice_ReturnsSameInstance()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ITrustStore>(), provider.GetRequiredService<ITrustStore>());
    }

    /// <summary>Verifies that resolving <see cref="ISessionRegistry"/> twice from the same provider returns the same instance.</summary>
    [Fact]
    public async Task AddTrustServices_SessionRegistryResolvedTwice_ReturnsSameInstance()
    {
        using var shutdown = new CancellationTokenSource();
        IClock clock = new SystemClock();
        ISecurityStateGate securityGate = new SecurityStateGate();
        ITrustStore trustStore = await TrustServiceExtensions.CreateTrustStoreAsync(clock, securityGate, new FakeTrustStorePersistence());
        var services = new ServiceCollection();
        services.AddCoreServices(clock, securityGate, shutdown);
        services.AddTrustServices(trustStore);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ISessionRegistry>(), provider.GetRequiredService<ISessionRegistry>());
        Assert.Same(provider.GetRequiredService<IPairingCoordinator>(), provider.GetRequiredService<IPairingCoordinator>());
    }
}
