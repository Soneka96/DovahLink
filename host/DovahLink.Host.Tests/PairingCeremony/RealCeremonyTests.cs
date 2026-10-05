using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using DovahLink.Host.Identity;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.Tests.TestDoubles;
using Xunit.Abstractions;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// Runs real sas-pairing ceremonies end to end over the real native library: the Host side is the
/// dormant integration in this process (a real persisted Host key, its Bootstrap and authority scope,
/// the ceremony owner, and the result validator); the Initiator is the separate test-peer process. The
/// honest-path SAS comparison is automated here as test policy only; the integration itself never
/// approves anything without an explicit decision.
/// </summary>
/// <param name="output">Receives the measured responsiveness figures.</param>
[Collection(SasPairingNativeTestCollection.Name)]
public sealed class RealCeremonyTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>How long any single ceremony milestone may take.</summary>
    private static readonly TimeSpan Milestone = TimeSpan.FromSeconds(30);

    /// <summary>The isolated directory of the Host key's public record.</summary>
    private readonly string keyDirectory = Path.Combine(Path.GetTempPath(), $"dovahlink-real-ceremony-{Guid.NewGuid():N}");

    /// <summary>The isolated persisted key-name prefix of the Host key.</summary>
    private readonly string keyNamePrefix = $"DovahLink.Host.Tests.RealCeremony.{Guid.NewGuid():N}.";

    /// <summary>
    /// A fixed, test-only Host ID. The native authority lock keeps one lock file per scope, so fixed IDs
    /// keep repeated runs from accumulating lock files.
    /// </summary>
    private readonly HostId hostId = new(Guid.Parse("7e570000-0000-4000-8000-0000000000a1"));

    /// <summary>A fixed, test-only client ID; see <see cref="hostId"/>.</summary>
    private readonly ClientId clientId = new(Guid.Parse("7e570000-0000-4000-8000-0000000000c1"));

    /// <summary>A real P-256 client key; this test only uses its public SubjectPublicKeyInfo.</summary>
    private readonly ECDsa clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>
    /// Verifies a real ceremony reaches one Host local result whose authenticated peer frame equals,
    /// byte for byte, the frame built from the candidate client values (E-13), that each one-field change
    /// of those candidates is rejected, and that the evidence outlives the released result and the stopped
    /// host. Also measures the drive bound, idle cadence, concurrent thread-pool responsiveness, and stop
    /// latency (E-03).
    /// </summary>
    [Fact]
    public void RealCeremony_ReachesHostLocalResultAndExactFrameValidates()
    {
        (IPairingCeremonyHost host, RecordingPairingCeremonyObserver observer, MeasuringNativeSessionFactory measured, byte[] expectedClientFrame) = StartHost();
        using (host)
        using (var probe = new ResponsivenessProbe())
        using (TestPeerProcess peer = StartPeer(host))
        {
            string peerSas = AwaitPeerLine(peer, "SAS ");
            Eventually(() => !observer.SasRequests.IsEmpty);
            SasComparisonRequest hostSas = observer.SasRequests.Single();
            Assert.Equal(hostSas.DecimalDisplay, peerSas["SAS ".Length..]);

            Assert.True(host.TrySubmitSasDecision(hostSas.CeremonyIdentity, SasComparisonDecision.Match));
            peer.WriteLine("APPROVE");

            Eventually(() => !observer.Completions.IsEmpty);
            (CeremonyAttemptId? attempt, CeremonyResultSnapshot result) = observer.Completions.Single();
            string peerResult = AwaitPeerLine(peer, "RESULT ");
            Assert.Equal(hostSas.Attempt, attempt);
            Assert.Equal($"RESULT {Convert.ToHexStringLower(result.CeremonyIdentity)} Responder", peerResult);
            Assert.True(result.CeremonyIdentity.SequenceEqual(hostSas.CeremonyIdentity));
            Assert.Equal(0, peer.WaitForExit(Milestone));

            CeremonyEvidenceResult accepted = new CeremonyResultValidator().Validate(result, HostFields(), expectedClientFrame);
            Assert.Null(accepted.Rejection);
            foreach ((string field, byte[] frame) in OneFieldCandidateChanges())
            {
                Assert.True(
                    new CeremonyResultValidator().Validate(result, HostFields(), frame).Rejection == CeremonyEvidenceRejection.PeerBootstrapMismatch,
                    field);
            }

            TimeSpan worstPoolLateness = probe.Stop();
            var stopwatch = Stopwatch.StartNew();
            host.Stop();
            TimeSpan stopLatency = stopwatch.Elapsed;

            SasCeremonyCompletedLocally evidence = accepted.Evidence!;
            Assert.True(evidence.CeremonyIdentity.SequenceEqual(hostSas.CeremonyIdentity));
            Assert.True(evidence.AuthenticatedPeerBootstrap.SequenceEqual(expectedClientFrame));
            Assert.Equal(PairingCeremonyHostState.Stopped, host.State);
            ReportResponsiveness(measured, worstPoolLateness, stopLatency);
        }
    }

    /// <summary>Verifies a SAS MISMATCH on both sides ends the attempt with no local result anywhere.</summary>
    [Fact]
    public void RealCeremony_SasMismatch_EndsWithoutResult()
    {
        (IPairingCeremonyHost host, RecordingPairingCeremonyObserver observer, _, _) = StartHost();
        using (host)
        using (TestPeerProcess peer = StartPeer(host))
        {
            AwaitPeerLine(peer, "SAS ");
            Eventually(() => !observer.SasRequests.IsEmpty);
            SasComparisonRequest hostSas = observer.SasRequests.Single();

            Assert.True(host.TrySubmitSasDecision(hostSas.CeremonyIdentity, SasComparisonDecision.Mismatch));
            peer.WriteLine("REJECT");

            Eventually(() => !observer.EndedAttempts.IsEmpty);
            Assert.Equal(hostSas.Attempt, observer.EndedAttempts.Single());
            Assert.StartsWith("ENDED", AwaitPeerLine(peer, "ENDED"));
            Assert.Empty(observer.Completions);
            Assert.Equal(PairingCeremonyHostState.Running, host.State);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        clientKey.Dispose();
        string keyName = keyNamePrefix + hostId;
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            using CngKey key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }

        if (Directory.Exists(keyDirectory))
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }

    /// <summary>Waits until a condition holds, failing after one milestone timeout.</summary>
    /// <param name="condition">The condition.</param>
    private static void Eventually(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(deadline.Elapsed < Milestone, "A real-ceremony milestone did not happen in time.");
            Thread.Sleep(10);
        }
    }

    /// <summary>Waits for the peer line with a prefix, failing on any error line.</summary>
    /// <param name="peer">The peer.</param>
    /// <param name="prefix">The expected prefix.</param>
    /// <returns>The line.</returns>
    private static string AwaitPeerLine(TestPeerProcess peer, string prefix)
    {
        string line = peer.ReadLine(Milestone);
        Assert.True(line.StartsWith(prefix, StringComparison.Ordinal), $"The test peer reported '{line}' instead of '{prefix}…'.");
        return line;
    }

    /// <summary>Builds this test's Host Bootstrap fields from its real persisted Host key.</summary>
    /// <returns>The Host Bootstrap fields.</returns>
    private CeremonyBootstrapFields HostFields()
    {
        HostKeyLoadResult key = new WindowsCngHostKeyStore(keyDirectory, keyNamePrefix, TimeSpan.FromSeconds(10)).LoadOrProvision(hostId);
        Assert.True(key.IsAvailable, key.Status.ToString());
        DovahLinkBootstrap bootstrap = DovahLinkBootstrap.ForHost(hostId, key.PublicKey!);
        return new CeremonyBootstrapFields(bootstrap.ApplicationIdentity, bootstrap.KeyAlgorithm, bootstrap.PublicKey, bootstrap.SharedContext);
    }

    /// <summary>Builds the client Bootstrap the peer supplies, from this test's client ID and real client key.</summary>
    /// <returns>The client Bootstrap.</returns>
    private DovahLinkBootstrap ClientBootstrap() =>
        DovahLinkBootstrap.ForClientCandidate(clientId, P256PublicKey.FromSubjectPublicKeyInfo(clientKey.ExportSubjectPublicKeyInfo()));

    /// <summary>Starts the real dormant Host integration with an auto-authorizing (test-only) observer.</summary>
    /// <returns>The host, its observer, the drive measurements, and the expected client frame.</returns>
    private (IPairingCeremonyHost Host, RecordingPairingCeremonyObserver Observer, MeasuringNativeSessionFactory Measured, byte[] ExpectedClientFrame) StartHost()
    {
        var observer = new RecordingPairingCeremonyObserver();
        var measured = new MeasuringNativeSessionFactory();
        var options = new PairingCeremonyHostOptions(
            SasPairingTestArtifacts.NativeLibraryPath(), DovahLinkPairingMapping.EncodeHostAuthorityScope(hostId), HostFields());
        var host = new PairingCeremonyHost(options, observer, measured);
        observer.OnExposure = attempt => host.TryAuthorizeExposure(attempt);
        Assert.True(host.Start(), host.Failure?.ToString());
        return (host, observer, measured, ClientBootstrap().EncodeCanonicalFrame());
    }

    /// <summary>Starts the Initiator test peer against the host's loopback listener with a distinct client authority scope.</summary>
    /// <param name="host">The running host.</param>
    /// <returns>The peer process.</returns>
    private TestPeerProcess StartPeer(IPairingCeremonyHost host)
    {
        DovahLinkBootstrap client = ClientBootstrap();
        return new TestPeerProcess(
            "initiator",
            SasPairingTestArtifacts.NativeLibraryPath(),
            Convert.ToHexString(DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Client, clientId.Value)),
            Convert.ToHexString(client.ApplicationIdentity),
            Convert.ToHexString(client.KeyAlgorithm),
            Convert.ToHexString(client.PublicKey),
            Convert.ToHexString(client.SharedContext),
            host.ListenerEndpoint!.Port.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Builds expected client frames that each change exactly one candidate field.</summary>
    /// <returns>The changed frames, by field.</returns>
    private Dictionary<string, byte[]> OneFieldCandidateChanges()
    {
        P256PublicKey key = P256PublicKey.FromSubjectPublicKeyInfo(clientKey.ExportSubjectPublicKeyInfo());
        byte[] uuid = clientId.Value.ToByteArray(bigEndian: true);
        uuid[^1] ^= 0x01;
        byte[] algorithm = ClientBootstrap().EncodeCanonicalFrame();
        algorithm[10 + 4 + 50 + 4 + 31] ^= 0x01;
        byte[] context = ClientBootstrap().EncodeCanonicalFrame();
        context[^1] ^= 0x01;
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new Dictionary<string, byte[]>
        {
            ["clientId"] = DovahLinkBootstrap.ForClientCandidate(new ClientId(new Guid(uuid, bigEndian: true)), key).EncodeCanonicalFrame(),
            ["role"] = DovahLinkBootstrap.ForHost(new HostId(clientId.Value), key).EncodeCanonicalFrame(),
            ["keyAlgorithm"] = algorithm,
            ["publicKey"] = DovahLinkBootstrap.ForClientCandidate(clientId, P256PublicKey.FromSubjectPublicKeyInfo(other.ExportSubjectPublicKeyInfo())).EncodeCanonicalFrame(),
            ["sharedContext"] = context,
        };
    }

    /// <summary>Reports and loosely bounds the measured responsiveness figures; these are integration checks, not benchmarks.</summary>
    /// <param name="measured">The drive measurements.</param>
    /// <param name="worstPoolLateness">The worst lateness of concurrent thread-pool work during the ceremony.</param>
    /// <param name="stopLatency">How long stopping the host took.</param>
    private void ReportResponsiveness(MeasuringNativeSessionFactory measured, TimeSpan worstPoolLateness, TimeSpan stopLatency)
    {
        (TimeSpan Duration, bool HadEvents)[] drives = [.. measured.Drives];
        TimeSpan worstDrive = drives.Max(drive => drive.Duration);
        TimeSpan[] idle = [.. drives.Where(drive => !drive.HadEvents).Select(drive => drive.Duration)];
        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"E-03: drives={drives.Length} worstDrive={worstDrive.TotalMilliseconds:F1}ms idleDrives={idle.Length} " +
            $"idleMedian={idle.Order().ElementAt(idle.Length / 2).TotalMilliseconds:F1}ms worstPoolLateness={worstPoolLateness.TotalMilliseconds:F1}ms " +
            $"stopLatency={stopLatency.TotalMilliseconds:F1}ms");
        output.WriteLine(report);
        string evidence = Path.Combine(SasPairingTestArtifacts.RepositoryRoot, "out", "sas-pairing", "evidence");
        Directory.CreateDirectory(evidence);
        File.AppendAllText(Path.Combine(evidence, "e03-responsiveness.txt"), $"{DateTimeOffset.UtcNow:O} {report}{Environment.NewLine}");

        Assert.True(worstDrive < TimeSpan.FromSeconds(1), report);
        Assert.True(worstPoolLateness < TimeSpan.FromMilliseconds(500), report);
        Assert.True(stopLatency < TimeSpan.FromSeconds(2), report);
    }
}
