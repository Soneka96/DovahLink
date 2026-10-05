using System.Net;
using System.Security.Cryptography;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Runs the dormant ceremony host over the real sas-pairing native library: it opens the runtime,
/// authority, and host, attaches a real loopback listener, drives it on its owner thread, and releases
/// everything on stop so a later host can open them again.
/// </summary>
[Collection(SasPairingNativeTestCollection.Name)]
public sealed class PairingCeremonyHostNativeTests
{
    /// <summary>Verifies a real host runs on loopback, drives, stops cleanly, and leaves nothing behind for the next host.</summary>
    [Fact]
    public void RealHost_RunsOnLoopbackAndReleasesEverythingOnStop()
    {
        string nativeLibrary = SasPairingTestArtifacts.NativeLibraryPath();
        byte[] scope = RandomNumberGenerator.GetBytes(47);

        for (int round = 0; round < 2; round++)
        {
            var observer = new RecordingPairingCeremonyObserver();
            using IPairingCeremonyHost host = PairingCeremonyHost.Create(
                Fixtures.BuildPairingCeremonyHostOptions(nativeLibrary, scope), observer);

            Assert.True(host.Start(), $"round {round}: {host.Failure}");
            Assert.Equal(IPAddress.Loopback, host.ListenerEndpoint!.Address);
            Thread.Sleep(300);
            Assert.Equal(PairingCeremonyHostState.Running, host.State);

            host.Stop();

            Assert.Equal(PairingCeremonyHostState.Stopped, host.State);
            Assert.Null(host.Failure);
            Assert.Empty(observer.Failures);
        }
    }
}
