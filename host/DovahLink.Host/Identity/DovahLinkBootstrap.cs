using System.Buffers.Binary;
using System.Text;

namespace DovahLink.Host.Identity;

/// <summary>
/// The four byte fields DovahLink supplies as one installation's pairing Bootstrap record: its
/// application identity, the key-algorithm identifier, the full DER SubjectPublicKeyInfo of its
/// long-term key (never a fingerprint), and the locally compiled shared context. All fields are
/// public, non-secret values. The canonical frame is built by encoding only; nothing here parses a
/// frame received from a peer.
/// </summary>
public sealed class DovahLinkBootstrap
{
    /// <summary>The 50-byte application identity.</summary>
    private readonly byte[] applicationIdentity;

    /// <summary>The ASCII key-algorithm identifier.</summary>
    private readonly byte[] keyAlgorithm;

    /// <summary>The 91-byte DER SubjectPublicKeyInfo of the long-term key.</summary>
    private readonly byte[] publicKey;

    /// <summary>The ASCII shared context.</summary>
    private readonly byte[] sharedContext;

    /// <summary>Builds the Bootstrap of one role's installation from local values.</summary>
    /// <param name="role">The installation's role.</param>
    /// <param name="installationId">The non-empty installation UUID.</param>
    /// <param name="longTermKey">The installation's long-term public key.</param>
    private DovahLinkBootstrap(DovahLinkPairingRole role, Guid installationId, P256PublicKey longTermKey)
    {
        ArgumentNullException.ThrowIfNull(longTermKey);
        applicationIdentity = DovahLinkPairingMapping.EncodeApplicationIdentity(role, installationId);
        keyAlgorithm = Encoding.ASCII.GetBytes(Constants.PairingKeyAlgorithm);
        publicKey = longTermKey.SubjectPublicKeyInfo.ToArray();
        sharedContext = Encoding.ASCII.GetBytes(Constants.PairingSharedContext);
    }

    /// <summary>The 50-byte application identity: domain, role byte, and RFC 9562 UUID bytes.</summary>
    public ReadOnlySpan<byte> ApplicationIdentity => applicationIdentity;

    /// <summary>The ASCII key-algorithm identifier.</summary>
    public ReadOnlySpan<byte> KeyAlgorithm => keyAlgorithm;

    /// <summary>The exact 91-byte DER SubjectPublicKeyInfo of the long-term key.</summary>
    public ReadOnlySpan<byte> PublicKey => publicKey;

    /// <summary>The ASCII shared context this side compiled itself.</summary>
    public ReadOnlySpan<byte> SharedContext => sharedContext;

    /// <summary>Builds this Host installation's own Bootstrap.</summary>
    /// <param name="hostId">The persisted Host installation ID.</param>
    /// <param name="hostKey">The Host's long-term public key.</param>
    /// <returns>The Host Bootstrap.</returns>
    /// <exception cref="ArgumentException"><paramref name="hostId"/> is the default, empty ID.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="hostKey"/> is <see langword="null"/>.</exception>
    public static DovahLinkBootstrap ForHost(HostId hostId, P256PublicKey hostKey) =>
        new(DovahLinkPairingRole.Host, hostId.Value, hostKey);

    /// <summary>
    /// Builds the Bootstrap a client must have supplied, from candidate values held before a ceremony.
    /// The candidates become authenticated evidence only if this Bootstrap's canonical frame equals the
    /// whole authenticated peer frame byte for byte.
    /// </summary>
    /// <param name="clientId">The candidate client installation ID.</param>
    /// <param name="clientKey">The candidate client long-term public key.</param>
    /// <returns>The expected client Bootstrap.</returns>
    /// <exception cref="ArgumentException"><paramref name="clientId"/> is the nil UUID.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientKey"/> is <see langword="null"/>.</exception>
    public static DovahLinkBootstrap ForClientCandidate(ClientId clientId, P256PublicKey clientKey) =>
        new(DovahLinkPairingRole.Client, clientId.Value, clientKey);

    /// <summary>
    /// Encodes the canonical Bootstrap record: the fixed header, then each field as a big-endian 32-bit
    /// length followed by its bytes, with no trailing bytes (241 bytes for every DovahLink Bootstrap).
    /// </summary>
    /// <returns>A fresh copy of the canonical frame.</returns>
    public byte[] EncodeCanonicalFrame()
    {
        ReadOnlySpan<byte> header = Constants.PairingBootstrapFrameHeader;
        byte[] frame = new byte[header.Length + (4 * sizeof(uint)) + applicationIdentity.Length +
            keyAlgorithm.Length + publicKey.Length + sharedContext.Length];
        header.CopyTo(frame);
        int offset = header.Length;
        foreach (byte[] field in new[] { applicationIdentity, keyAlgorithm, publicKey, sharedContext })
        {
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(offset), (uint)field.Length);
            field.CopyTo(frame, offset + sizeof(uint));
            offset += sizeof(uint) + field.Length;
        }

        return frame;
    }
}
