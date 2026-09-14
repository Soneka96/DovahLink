using System.IO;
using DovahLink.Host.Client.Protocol;
using DovahLink.Host.Composition;
using DovahLink.Host.Identity;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Trust;

namespace DovahLink.Host.Tests.Composition;

/// <summary>Tests for <see cref="TrustServiceExtensions.ComposeTrustServicesAsync"/>.</summary>
public class TrustServiceExtensionsTests
{
    /// <summary>
    /// Verifies that malformed or undecryptable trust persistence fails composition closed --
    /// propagating before any service in the graph is returned -- rather than silently starting with
    /// a reset or partially loaded trust store.
    /// </summary>
    [Fact]
    public async Task ComposeTrustServicesAsync_MalformedTrustPersistence_ThrowsInvalidDataException()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        var persistence = new FakeTrustStorePersistence { ThrowOnLoad = new InvalidDataException("corrupt") };

        await Assert.ThrowsAsync<InvalidDataException>(() => TrustServiceExtensions.ComposeTrustServicesAsync(core, persistence));
    }

    /// <summary>Verifies that the composed session registry's cap comes from the supplied <see cref="CoreServices.Settings"/>.</summary>
    [Fact]
    public async Task ComposeTrustServicesAsync_UsesResolvedCapFromCoreServices()
    {
        using var shutdown = new CancellationTokenSource();
        var hostSettingsProvider = new FakeHostSettingsProvider { Settings = new HostSettings(2) };
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown, hostSettingsProvider);

        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());

        Assert.Equal(2, trust.SessionRegistry.MaxActiveSessions);
    }

    /// <summary>
    /// Verifies that <see cref="TrustServices.TrustAdminService"/> actually mutates the same
    /// <see cref="TrustServices.TrustStore"/> instance returned alongside it -- not an independently
    /// constructed, unwired copy that happens to start with identical loaded data -- by revoking a
    /// pre-seeded trusted device through the admin service and observing the change directly through
    /// the separately returned trust store.
    /// </summary>
    [Fact]
    public async Task ComposeTrustServicesAsync_TrustAdminServiceMutation_ObservableThroughReturnedTrustStore()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);
        var clientId = ClientId.NewId();
        var record = new TrustRecord(clientId, "12345", "Living Room PC", KnownDeviceState.Trusted, new string('a', 64), DateTimeOffset.UtcNow)
        {
            Incarnation = KnownDeviceIncarnationId.NewId(),
        };
        var persistence = new FakeTrustStorePersistence();
        await persistence.SaveAsync([record]);

        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, persistence);
        await trust.TrustAdminService.RevokeAsync(clientId);

        Assert.Equal(KnownDeviceState.Revoked, trust.TrustStore.TryGet(clientId)!.State);
    }

    /// <summary>
    /// Verifies that the composed <see cref="IPublicEnvelopeCodec"/> is actually wired to the same
    /// <see cref="CoreServices.StateAuthorityLifecycle"/> instance -- not an independently constructed,
    /// unconfigured codec -- by encoding a <c>hello_ack</c> (a message type that requires
    /// <c>stateAuthorityId</c>) and observing the stamped value match.
    /// </summary>
    [Fact]
    public async Task ComposeTrustServicesAsync_EnvelopeCodecWiredToCoreStateAuthorityLifecycle()
    {
        using var shutdown = new CancellationTokenSource();
        CoreServices core = CoreServiceExtensions.ComposeCoreServices(shutdown);

        TrustServices trust = await TrustServiceExtensions.ComposeTrustServicesAsync(core, new FakeTrustStorePersistence());

        byte[] encoded = trust.EnvelopeCodec.Encode(
            PublicMessageType.HelloAck, "message-1", "session-1", null, null, null,
            new HelloAckPayload { HostVersion = Constants.PublicProtocolHostVersion, ClientIdentityKind = ClientIdentityKind.Unpaired });
        Assert.True(trust.EnvelopeCodec.TryDecode(encoded, out PublicEnvelope? envelope));
        Assert.Equal(core.StateAuthorityLifecycle.Current.ToString(), envelope!.StateAuthorityId);
    }
}
