namespace DovahLink.Host.PairingCeremony;

/// <summary>
/// Detached evidence that THIS Host completed one sas-pairing ceremony locally with a peer whose whole
/// authenticated Bootstrap frame equals the frame expected from candidate values held before the
/// ceremony. It is local completion only: it does not prove that the peer completed, holds the private
/// key of its authenticated public key, was approved by the user, or is trusted, and it authorizes
/// nothing by itself. Only <see cref="ICeremonyResultValidator"/> creates it; it outlives the native
/// result it was read from.
/// </summary>
public sealed class SasCeremonyCompletedLocally
{
    /// <summary>The 32-byte ceremony identity.</summary>
    private readonly byte[] ceremonyIdentity;

    /// <summary>The authenticated peer Bootstrap frame.</summary>
    private readonly byte[] authenticatedPeerBootstrap;

    /// <summary>Copies the validated values.</summary>
    /// <param name="ceremonyIdentity">The ceremony identity.</param>
    /// <param name="authenticatedPeerBootstrap">The authenticated peer frame, already equal to the expected frame.</param>
    internal SasCeremonyCompletedLocally(ReadOnlySpan<byte> ceremonyIdentity, ReadOnlySpan<byte> authenticatedPeerBootstrap)
    {
        this.ceremonyIdentity = ceremonyIdentity.ToArray();
        this.authenticatedPeerBootstrap = authenticatedPeerBootstrap.ToArray();
    }

    /// <summary>The 32-byte ceremony identity: the authoritative identity of this exact attempt, never a peer identity.</summary>
    public ReadOnlySpan<byte> CeremonyIdentity => ceremonyIdentity;

    /// <summary>The exact peer Bootstrap frame the ceremony authenticated, equal to the expected candidate frame.</summary>
    public ReadOnlySpan<byte> AuthenticatedPeerBootstrap => authenticatedPeerBootstrap;
}
