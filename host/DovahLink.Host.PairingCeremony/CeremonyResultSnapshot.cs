namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// The detached data of one local sas-pairing result, read exactly once before the native result was
/// released. It records that THIS endpoint completed one ceremony locally: not that the peer did, not a
/// Pair, and not trust. Every byte field is this instance's own copy and nothing is parsed.
/// </summary>
public sealed class CeremonyResultSnapshot
{
    /// <summary>The 32-byte ceremony identity.</summary>
    private readonly byte[] ceremonyIdentity;

    /// <summary>The routing and correlation bytes of the ceremony.</summary>
    private readonly byte[] requestId;

    /// <summary>The exact canonical Bootstrap frame the peer authenticated.</summary>
    private readonly byte[] authenticatedPeerBootstrap;

    /// <summary>The authenticated shared context.</summary>
    private readonly byte[] authenticatedSharedContext;

    /// <summary>The protocol profile identifier.</summary>
    private readonly byte[] profileIdentifier;

    /// <summary>Copies the result fields.</summary>
    /// <param name="ceremonyIdentity">The ceremony identity.</param>
    /// <param name="peerRole">The peer's protocol role.</param>
    /// <param name="profileVersion">The protocol profile version.</param>
    /// <param name="requestId">The request ID bytes.</param>
    /// <param name="authenticatedPeerBootstrap">The authenticated peer Bootstrap frame.</param>
    /// <param name="authenticatedSharedContext">The authenticated shared context.</param>
    /// <param name="profileIdentifier">The protocol profile identifier.</param>
    public CeremonyResultSnapshot(
        ReadOnlySpan<byte> ceremonyIdentity,
        CeremonyPeerRole peerRole,
        uint profileVersion,
        ReadOnlySpan<byte> requestId,
        ReadOnlySpan<byte> authenticatedPeerBootstrap,
        ReadOnlySpan<byte> authenticatedSharedContext,
        ReadOnlySpan<byte> profileIdentifier)
    {
        this.ceremonyIdentity = ceremonyIdentity.ToArray();
        PeerRole = peerRole;
        ProfileVersion = profileVersion;
        this.requestId = requestId.ToArray();
        this.authenticatedPeerBootstrap = authenticatedPeerBootstrap.ToArray();
        this.authenticatedSharedContext = authenticatedSharedContext.ToArray();
        this.profileIdentifier = profileIdentifier.ToArray();
    }

    /// <summary>The 32-byte ceremony identity: the authoritative identity of this exact attempt.</summary>
    public ReadOnlySpan<byte> CeremonyIdentity => ceremonyIdentity;

    /// <summary>The PEER's protocol role.</summary>
    public CeremonyPeerRole PeerRole { get; }

    /// <summary>The protocol profile version, as reported.</summary>
    public uint ProfileVersion { get; }

    /// <summary>Routing and correlation bytes only; never an identity or an authority.</summary>
    public ReadOnlySpan<byte> RequestId => requestId;

    /// <summary>The exact canonical Bootstrap frame the peer supplied and the ceremony authenticated.</summary>
    public ReadOnlySpan<byte> AuthenticatedPeerBootstrap => authenticatedPeerBootstrap;

    /// <summary>The authenticated shared context.</summary>
    public ReadOnlySpan<byte> AuthenticatedSharedContext => authenticatedSharedContext;

    /// <summary>The protocol profile identifier bytes.</summary>
    public ReadOnlySpan<byte> ProfileIdentifier => profileIdentifier;
}
