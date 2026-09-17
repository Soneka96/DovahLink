using DovahLink.Host.Adapter.Ipc;
using DovahLink.Host.Identity;
using DovahLink.Host.Process;
using DovahLink.Host.Tests.TestDoubles;
using DovahLink.Host.Time;

namespace DovahLink.Host.Tests.Adapter.Ipc;

/// <summary>
/// Tests for <see cref="AdapterConnectionFactory"/>. Adapter-IPC protocol behavior (handshake
/// acceptance/rejection, resynchronization, reconnect freshness) is already fully proven against a
/// real socket by <see cref="DovahLink.Host.Tests.Adapter.Ipc.AdapterIpcChannelIntegrationTests"/>
/// and <see cref="DovahLink.Host.Tests.Composition.AdapterIpcServiceExtensionsTests"/>; this file
/// covers only the factory's own responsibility of producing a fresh connection per call.
/// </summary>
public class AdapterConnectionFactoryTests
{
    /// <summary>Verifies that two calls to <see cref="AdapterConnectionFactory.Create"/> return distinct connection instances.</summary>
    [Fact]
    public void Create_CalledTwice_ReturnsDistinctConnections()
    {
        var factory = new AdapterConnectionFactory(
            new IpcFrameCodec(), new AdapterConnectionLifecycle(new FakeAdapterAvailabilityTracker()),
            new AdapterPeerProofVerifier(), new FakeAdapterTrustAdminRequestHandler(), new FakePlayContextTracker(),
            new FakeLiveCaptureSink(), new HostInstanceOptions(default), new SystemClock());

        IAdapterIpcConnection first = factory.Create(new MemoryStream());
        IAdapterIpcConnection second = factory.Create(new MemoryStream());

        Assert.NotSame(first, second);
    }
}
