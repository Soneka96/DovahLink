using System.Security.Cryptography;
using DovahLink.Host.Identity;
using DovahLink.Host.PairingCeremony;
using DovahLink.Host.Tests.TestDoubles;

namespace DovahLink.Host.Tests.PairingCeremony;

/// <summary>
/// E-11: proves, across real OS processes of one Windows user, that one Host installation's pairing
/// authority scope has exactly one live owner. A second process gets ownership-unavailable while the
/// first holds it, and may acquire it only after the first released it. Nothing here changes the scope
/// or the native accounting.
/// </summary>
[Collection(SasPairingNativeTestCollection.Name)]
public sealed class PairingAuthorityProcessTests
{
    /// <summary>How long a peer process may take to report.</summary>
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A fixed, test-only Host ID. The native authority lock keeps one lock file per scope, so a fixed ID
    /// keeps repeated runs from accumulating lock files.
    /// </summary>
    private readonly HostId hostId = new(Guid.Parse("7e570000-0000-4000-8000-0000000000a2"));

    /// <summary>
    /// Verifies a running Host integration owns its scope: another process is refused the same scope,
    /// may hold the same installation's client-role scope concurrently, and gets the Host scope only after
    /// the Host stopped.
    /// </summary>
    [Fact]
    public void SecondProcess_GetsHostScopeOnlyAfterTheHostReleasesIt()
    {
        byte[] hostScope = DovahLinkPairingMapping.EncodeHostAuthorityScope(hostId);
        byte[] otherRoleScope = DovahLinkPairingMapping.EncodeAuthorityScope(DovahLinkPairingRole.Client, hostId.Value);
        using IPairingCeremonyHost host = StartHost(hostScope);

        using (TestPeerProcess contender = Hold(hostScope))
        {
            Assert.Equal("UNAVAILABLE OwnershipUnavailable", contender.ReadLine(PeerTimeout));
            Assert.Equal(5, contender.WaitForExit(PeerTimeout));
        }

        using (TestPeerProcess otherRole = Hold(otherRoleScope))
        {
            Assert.Equal("HELD", otherRole.ReadLine(PeerTimeout));
            otherRole.CloseInput();
            Assert.Equal(0, otherRole.WaitForExit(PeerTimeout));
        }

        Assert.Equal(PairingCeremonyHostState.Running, host.State);
        host.Stop();

        using TestPeerProcess replacement = Hold(hostScope);
        Assert.Equal("HELD", replacement.ReadLine(PeerTimeout));
        replacement.CloseInput();
        Assert.Equal("RELEASED", replacement.ReadLine(PeerTimeout));
        Assert.Equal(0, replacement.WaitForExit(PeerTimeout));
    }

    /// <summary>
    /// Verifies the reverse: while another process holds the Host scope, the Host integration fails
    /// closed as authority-unavailable without changing its scope, and a later host acquires it once
    /// the holder released it.
    /// </summary>
    [Fact]
    public void HostIntegration_FailsClosedWhileAnotherProcessHoldsItsScope()
    {
        byte[] hostScope = DovahLinkPairingMapping.EncodeHostAuthorityScope(hostId);
        using TestPeerProcess holder = Hold(hostScope);
        Assert.Equal("HELD", holder.ReadLine(PeerTimeout));

        var observer = new RecordingPairingCeremonyObserver();
        using (IPairingCeremonyHost refused = PairingCeremonyHost.Create(Options(hostScope), observer))
        {
            Assert.False(refused.Start());
            Assert.Equal(PairingCeremonyFailure.AuthorityUnavailable, refused.Failure);
            Assert.False(refused.ProcessRestartRequired);
        }

        holder.CloseInput();
        Assert.Equal(0, holder.WaitForExit(PeerTimeout));

        using IPairingCeremonyHost later = StartHost(hostScope);
        Assert.Equal(PairingCeremonyHostState.Running, later.State);
    }

    /// <summary>Verifies a holder that is killed, not cleanly stopped, still releases the scope to the next owner.</summary>
    [Fact]
    public void KilledHolder_ReleasesTheScope()
    {
        byte[] hostScope = DovahLinkPairingMapping.EncodeHostAuthorityScope(hostId);
        using (TestPeerProcess holder = Hold(hostScope))
        {
            Assert.Equal("HELD", holder.ReadLine(PeerTimeout));
        }

        using IPairingCeremonyHost host = StartHost(hostScope);
        Assert.Equal(PairingCeremonyHostState.Running, host.State);
    }

    /// <summary>Starts a real Host integration over a scope.</summary>
    /// <param name="scope">The authority scope.</param>
    /// <returns>The running host.</returns>
    private static IPairingCeremonyHost StartHost(byte[] scope)
    {
        IPairingCeremonyHost host = PairingCeremonyHost.Create(Options(scope), new RecordingPairingCeremonyObserver());
        Assert.True(host.Start(), host.Failure?.ToString());
        return host;
    }

    /// <summary>Builds real-native options over a scope.</summary>
    /// <param name="scope">The authority scope.</param>
    /// <returns>The options.</returns>
    private static PairingCeremonyHostOptions Options(byte[] scope) =>
        Fixtures.BuildPairingCeremonyHostOptions(SasPairingTestArtifacts.NativeLibraryPath(), scope);

    /// <summary>Starts a test-peer process that registers and holds a scope.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The peer process.</returns>
    private static TestPeerProcess Hold(byte[] scope) =>
        new("hold-authority", SasPairingTestArtifacts.NativeLibraryPath(), Convert.ToHexString(scope));
}
