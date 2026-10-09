using System.Net;
using DovahLink.Host.Identity;
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
        byte[] scope = DovahLinkPairingMapping.EncodeHostAuthorityScope(new HostId(Guid.Parse("7e570000-0000-4000-8000-0000000000a3")));

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

    /// <summary>
    /// E-03 idle cadence: an idle real host blocks in each bounded drive rather than spinning, so it
    /// makes only a few drives a second, each within the native bound.
    /// </summary>
    [Fact]
    public void RealHost_IdleDrivesAreBoundedAndPaced()
    {
        var measured = new MeasuringNativeSessionFactory();
        using var host = new PairingCeremonyHost(
            Fixtures.BuildPairingCeremonyHostOptions(
                SasPairingTestArtifacts.NativeLibraryPath(),
                DovahLinkPairingMapping.EncodeHostAuthorityScope(new HostId(Guid.Parse("7e570000-0000-4000-8000-0000000000a4")))),
            new RecordingPairingCeremonyObserver(),
            measured);
        Assert.True(host.Start());

        Thread.Sleep(TimeSpan.FromSeconds(2));
        host.Stop();

        TimeSpan[] drives = [.. measured.Drives.Select(drive => drive.Duration)];
        string report = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"E-03 idle: drives={drives.Length} in 2s worst={drives.Max().TotalMilliseconds:F1}ms median={drives.Order().ElementAt(drives.Length / 2).TotalMilliseconds:F1}ms");
        string evidence = Path.Combine(SasPairingTestArtifacts.RepositoryRoot, "out", "sas-pairing", "evidence");
        Directory.CreateDirectory(evidence);
        File.AppendAllText(Path.Combine(evidence, "e03-responsiveness.txt"), $"{DateTimeOffset.UtcNow:O} {report}{Environment.NewLine}");
        Assert.InRange(drives.Length, 2, 40);
        Assert.True(drives.Max() < TimeSpan.FromSeconds(1), report);
    }
}
