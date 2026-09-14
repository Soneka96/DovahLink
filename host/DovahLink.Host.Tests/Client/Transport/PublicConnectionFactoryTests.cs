using DovahLink.Host.Authentication;
using DovahLink.Host.Client.Authentication;
using DovahLink.Host.Client.Dispatch;
using DovahLink.Host.Client.Transport;
using DovahLink.Host.Composition;
using DovahLink.Host.State;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.Client.Transport;

/// <summary>Tests for <see cref="PublicConnectionFactory"/>.</summary>
public class PublicConnectionFactoryTests
{
    /// <summary>Verifies that two calls to <see cref="PublicConnectionFactory.Create"/> return distinct connection instances.</summary>
    [Fact]
    public async Task Create_CalledTwice_ReturnsDistinctConnections()
    {
        IPublicConnectionFactory factory = await BuildFactoryAsync();

        IPublicWebSocketConnection first = factory.Create(new MemoryStream());
        IPublicWebSocketConnection second = factory.Create(new MemoryStream());

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Verifies that each connection's outbound state is its own -- not shared through the factory's
    /// Host-lifetime collaborators -- by exhausting one connection's outbound capacity and observing
    /// the other connection's own capacity is untouched.
    /// </summary>
    [Fact]
    public async Task Create_MutatingOneConnectionsOutboundState_LeavesTheOthersUnaffected()
    {
        IPublicConnectionFactory factory = await BuildFactoryAsync();
        IPublicWebSocketConnection first = factory.Create(new MemoryStream());
        IPublicWebSocketConnection second = factory.Create(new MemoryStream());
        int secondCapacityBefore = second.RemainingOutboundCapacity(PublicOutboundLane.Data);

        Assert.True(first.TrySend(new byte[] { 1, 2, 3 }, PublicOutboundLane.Data));

        Assert.True(first.RemainingOutboundCapacity(PublicOutboundLane.Data) < secondCapacityBefore);
        Assert.Equal(secondCapacityBefore, second.RemainingOutboundCapacity(PublicOutboundLane.Data));
    }

    /// <summary>Builds a factory over a real, freshly composed core/trust graph -- the same collaborators production composes it with.</summary>
    private static async Task<IPublicConnectionFactory> BuildFactoryAsync()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());
        var dispatcher = new ClientMessageDispatcher(
            trust.EnvelopeCodec, trust.TrustAdminService, trust.PairingCoordinator, new FakePairingAdapterNotifier(), trust.PlayContextTracker, core.Clock, trust.SessionRegistry);

        return new PublicConnectionFactory(
            trust.EnvelopeCodec, trust.SessionRegistry, trust.TrustStore,
            new LocalConnectionTokenAuthenticator(core.Clock), new TrustedCredentialFailureThrottle(core.Clock),
            trust.PlayContextTracker, core.Clock, dispatcher, trust.PairingCoordinator, trust.ConnectionRegistry,
            new RegisteredStateAreaPolicy(), new FakeStatePublicationFeed(), core.StateAuthorityLifecycle,
            new FakePublicWebSocketTransportDiagnostics());
    }
}
